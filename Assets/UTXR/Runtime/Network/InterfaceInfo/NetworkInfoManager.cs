using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Thread-safe, low-overhead network manager implementing double-buffered snapshots.
    /// Hot-path reads are O(1) with 0 GC allocations and 0 OS/kernel calls.
    /// Cold-path refreshes query system adapters and update immutable snapshots atomically.
    /// </summary>
    public sealed class NetworkInfoManager : INetworkInfoManager
    {
        public const int DefaultMaxInterfaces = 32;
        public const int DefaultMaxAddresses = 128;

        private readonly int _maxInterfaces;
        private readonly int _maxAddresses;

        // Double-buffered contiguous managed arrays
        private readonly NetworkInterfaceInfo[] _interfacesBuffer0;
        private readonly IPAddressInfo[] _addressesBuffer0;
        private readonly NetworkInterfaceInfo[] _interfacesBuffer1;
        private readonly IPAddressInfo[] _addressesBuffer1;

        // Cold-path preallocated scratch buffers
        private readonly RawInterfaceData[] _scratchRawInterfaces;
        private readonly IPAddressInfo[] _scratchAddresses;

        private int _activeBufferIndex;
        private NetworkSnapshot _activeSnapshot;

        // Concurrency controls
        private readonly object _refreshLock = new object();
        private int _refreshPending;
        private volatile bool _isDisposed;

        // Diagnostics tracking
        private int _version;
        private int _refreshCount;
        private long _lastRefreshDurationMs;
        private int _errorCount;
        private string _lastErrorMessage = string.Empty;

        public int CurrentVersion => _activeSnapshot.Version;

        public NetworkDiagnostics Diagnostics => new NetworkDiagnostics(
            _version,
            _refreshCount,
            _lastRefreshDurationMs,
            _errorCount,
            _maxInterfaces,
            _maxAddresses,
            _activeSnapshot.InterfaceCount,
            _activeSnapshot.AddressCount,
            _lastErrorMessage
        );

        public NetworkSnapshot CurrentSnapshot => _activeSnapshot;

        public event Action<int> OnInterfacesChanged;

        public NetworkInfoManager(int maxInterfaces = DefaultMaxInterfaces, int maxAddresses = DefaultMaxAddresses)
        {
            _maxInterfaces = Math.Max(4, maxInterfaces);
            _maxAddresses = Math.Max(8, maxAddresses);

            _interfacesBuffer0 = new NetworkInterfaceInfo[_maxInterfaces];
            _addressesBuffer0 = new IPAddressInfo[_maxAddresses];
            _interfacesBuffer1 = new NetworkInterfaceInfo[_maxInterfaces];
            _addressesBuffer1 = new IPAddressInfo[_maxAddresses];

            _scratchRawInterfaces = new RawInterfaceData[_maxInterfaces];
            _scratchAddresses = new IPAddressInfo[_maxAddresses];

            _activeBufferIndex = 0;
            _activeSnapshot = NetworkSnapshot.Empty;
        }

        #region Hot Path Reads (O(1), 0 GC, 0 OS Calls)

        public bool TryGetInterface(int index, out NetworkInterfaceInfo info) => _activeSnapshot.TryGetInterface(index, out info);

        public bool TryGetAddress(int interfaceIndex, int addressIndex, out IPAddressInfo address) => _activeSnapshot.TryGetAddress(interfaceIndex, addressIndex, out address);

        public bool TryGetDefaultInterface(out int index) => _activeSnapshot.TryGetDefaultInterface(out index);

        #endregion

        #region Cold / On-Demand System Queries

        public bool QueryRoute(string destination, out RouteInfo route)
        {
            route = default;
            if (string.IsNullOrEmpty(destination) || _isDisposed)
                return false;

            try
            {
                if (!IPAddress.TryParse(destination, out IPAddress targetIp))
                {
                    var addresses = Dns.GetHostAddresses(destination);
                    if (addresses == null || addresses.Length == 0)
                        return false;
                    targetIp = addresses[0];
                }

                // Zero-packet routing table query using UDP socket connect
                using var socket = new Socket(targetIp.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(targetIp, 1);
                if (socket.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    string localIp = localEndPoint.Address.ToString();
                    var snapshot = _activeSnapshot;
                    if (snapshot.TryFindInterfaceByLocalIp(localIp, out int ifaceIndex, out _))
                    {
                        route = new RouteInfo(ifaceIndex, localIp);
                        return true;
                    }

                    route = new RouteInfo(-1, localIp);
                    return true;
                }
            }
            catch (Exception ex)
            {
                RecordError($"QueryRoute to '{destination}' failed: {ex.Message}");
            }

            return false;
        }

        public bool RefreshNow()
        {
            if (_isDisposed) return false;

            lock (_refreshLock)
            {
                return PerformRefreshInternal();
            }
        }

        public void RequestRefresh()
        {
            if (_isDisposed || Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    lock (_refreshLock)
                    {
                        if (!_isDisposed) PerformRefreshInternal();
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _refreshPending, 0);
                }
            });
        }

        private bool PerformRefreshInternal()
        {
            var stopwatch = Stopwatch.StartNew();

            NetworkInterface[] nics;
            try
            {
                nics = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (Exception ex)
            {
                RecordError($"GetAllNetworkInterfaces query failed: {ex.Message}");
                stopwatch.Stop();
                _lastRefreshDurationMs = stopwatch.ElapsedMilliseconds;
                return false;
            }

            if (nics == null)
            {
                RecordError("GetAllNetworkInterfaces returned null array.");
                stopwatch.Stop();
                _lastRefreshDurationMs = stopwatch.ElapsedMilliseconds;
                return false;
            }

            int nicsCount = nics.Length;
            if (nicsCount > _maxInterfaces)
            {
                RecordError($"OS returned {nicsCount} interfaces exceeding MaxInterfaces capacity ({_maxInterfaces}). Refresh rejected to prevent truncation.");
                stopwatch.Stop();
                _lastRefreshDurationMs = stopwatch.ElapsedMilliseconds;
                return false;
            }

            int scratchAddrIndex = 0;
            NetworkLogger.Log($"[Network] Enumerated {nicsCount} network interfaces.");

            // 1. Base interface enumeration
            for (int i = 0; i < nicsCount; i++)
            {
                var nic = nics[i];
                int startAddrIdx = scratchAddrIndex;
                int addrCount = 0;

                try
                {
                    var ipProps = nic.GetIPProperties();
                    var unicastList = ipProps.UnicastAddresses;
                    if (unicastList != null)
                    {
                        foreach (var unicast in unicastList)
                        {
                            if (unicast?.Address == null) continue;

                            if (scratchAddrIndex >= _maxAddresses)
                            {
                                RecordError($"Total IP addresses exceeded MaxAddresses capacity ({_maxAddresses}). Refresh rejected.");
                                stopwatch.Stop();
                                _lastRefreshDurationMs = stopwatch.ElapsedMilliseconds;
                                return false;
                            }

                            string maskStr = string.Empty;
                            try
                            {
                                if (unicast.IPv4Mask != null)
                                {
                                    maskStr = unicast.IPv4Mask.ToString();
                                }
                            }
                            catch { }

                            _scratchAddresses[scratchAddrIndex++] = new IPAddressInfo(
                                unicast.Address.ToString(),
                                maskStr,
                                unicast.Address.AddressFamily
                            );
                            addrCount++;
                        }
                    }
                }
                catch { }

                long speedMbps = -1;
                try
                {
                    if (nic.Speed > 0)
                    {
                        speedMbps = nic.Speed / 1_000_000L;
                    }
                }
                catch { }

                string displayName = !string.IsNullOrEmpty(nic.Name) ? nic.Name : nic.Description;

                _scratchRawInterfaces[i] = new RawInterfaceData
                {
#if UNITY_ANDROID && !UNITY_EDITOR
                    // Android's managed enumerator may expose the same kernel NIC more than once.
                    // Linux interface name is the stable identity; managed Id is not authoritative.
                    Id = !string.IsNullOrEmpty(nic.Name) ? nic.Name : (nic.Id ?? string.Empty),
#else
                    Id = nic.Id ?? string.Empty,
#endif
                    Name = nic.Name ?? string.Empty,
                    DisplayName = displayName ?? string.Empty,
                    Ssid = string.Empty,
                    Type = ClassifyInterfaceType(nic),
                    Status = MapOperationalStatus(nic.OperationalStatus),
                    IsDefaultRoute = false,
                    LinkSpeedMbps = speedMbps,
                    EstimatedBandwidthMbps = -1,
                    AddressStartIndex = startAddrIdx,
                    AddressCount = addrCount,
                    PriorityScore = 0
                };

                NetworkLogger.Log($"[Network] Found iface [{nic.Name}]: Type={_scratchRawInterfaces[i].Type}, Status={_scratchRawInterfaces[i].Status}");
            }

            // 2. Platform-specific enrichment (Windows WLAN/DefaultRoute, Android XR, Linux sysfs)
            WindowsNetworkEnricher.Enrich(_scratchRawInterfaces, nicsCount, nics);
            AndroidNetworkEnricher.Enrich(_scratchRawInterfaces, nicsCount);
            LinuxNetworkEnricher.Enrich(_scratchRawInterfaces, nicsCount);
            EnrichDefaultGatewayFallback(_scratchRawInterfaces, nicsCount, nics);

            // 3. Android: one logical NIC per kernel interface name, with union of its addresses.
#if UNITY_ANDROID && !UNITY_EDITOR
            int preMergeCount = nicsCount;
            nicsCount = MergeAndroidDuplicates(nicsCount);
            if (preMergeCount != nicsCount)
            {
                NetworkLogger.Log($"[Network] Merged duplicate interfaces: {preMergeCount} -> {nicsCount}");
            }
#endif

            // 4. Compute priority scores for user interface sorting
            for (int i = 0; i < nicsCount; i++)
            {
                _scratchRawInterfaces[i].PriorityScore = ComputePriorityScore(ref _scratchRawInterfaces[i]);
            }

            // 4. Sort raw interfaces (Quicksort on preallocated array slice)
            Array.Sort(_scratchRawInterfaces, 0, nicsCount, RawInterfaceComparer.Instance);

            // 5. Select back buffer
            int targetBufferIndex = _activeBufferIndex == 0 ? 1 : 0;
            var targetInterfaces = targetBufferIndex == 0 ? _interfacesBuffer0 : _interfacesBuffer1;
            var targetAddresses = targetBufferIndex == 0 ? _addressesBuffer0 : _addressesBuffer1;

            int publishedAddrCount = 0;
            int defaultInterfaceIndex = -1;

            for (int i = 0; i < nicsCount; i++)
            {
                ref readonly var raw = ref _scratchRawInterfaces[i];
                int targetAddrStart = publishedAddrCount;

                for (int a = 0; a < raw.AddressCount; a++)
                {
                    targetAddresses[publishedAddrCount++] = _scratchAddresses[raw.AddressStartIndex + a];
                }

                targetInterfaces[i] = new NetworkInterfaceInfo(
                    raw.Id,
                    raw.Name,
                    raw.DisplayName,
                    raw.Ssid,
                    raw.Type,
                    raw.Status,
                    raw.IsDefaultRoute,
                    raw.LinkSpeedMbps,
                    raw.EstimatedBandwidthMbps,
                    targetAddrStart,
                    raw.AddressCount
                );

                if (raw.IsDefaultRoute && defaultInterfaceIndex < 0)
                {
                    defaultInterfaceIndex = i;
                }
            }

            int newVersion = Interlocked.Increment(ref _version);
            var newSnapshot = new NetworkSnapshot(
                newVersion,
                nicsCount,
                defaultInterfaceIndex,
                publishedAddrCount,
                DateTime.UtcNow,
                targetInterfaces,
                targetAddresses
            );

            // Atomic snapshot publication
            Interlocked.Exchange(ref _activeSnapshot, newSnapshot);
            _activeBufferIndex = targetBufferIndex;
            _refreshCount++;

            stopwatch.Stop();
            _lastRefreshDurationMs = stopwatch.ElapsedMilliseconds;

            string defaultName = (defaultInterfaceIndex >= 0 && defaultInterfaceIndex < nicsCount) ? targetInterfaces[defaultInterfaceIndex].Name : "none";
            NetworkLogger.Log($"[Network] Snapshot v{newVersion}: {nicsCount} interfaces (default='{defaultName}')");
            for (int i = 0; i < nicsCount; i++)
            {
                ref readonly var info = ref targetInterfaces[i];
                string primaryIp = info.AddressCount > 0 ? targetAddresses[info.AddressStartIndex].Address : "no-ip";
                string ssidInfo = !string.IsNullOrEmpty(info.Ssid) ? $", SSID='{info.Ssid}'" : "";
                NetworkLogger.Log($"[Network]   [{info.Name}] {info.Type} {info.Status} IP={primaryIp}{ssidInfo}");
            }

            // Notify subscribers
            try
            {
                OnInterfacesChanged?.Invoke(newVersion);
            }
            catch (Exception ex)
            {
                NetworkLogger.LogWarning($"[Network] OnInterfacesChanged handler threw: {ex.Message}");
            }

            return true;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Cold path only. Uses the inactive address buffer for compaction, so no extra allocation.
        private int MergeAndroidDuplicates(int count)
        {
            var mergedAddresses = _activeBufferIndex == 0 ? _addressesBuffer1 : _addressesBuffer0;
            int uniqueCount = 0;
            int addressCount = 0;

            for (int i = 0; i < count; i++)
            {
                var raw = _scratchRawInterfaces[i];
                int found = -1;
                if (!string.IsNullOrEmpty(raw.Name))
                {
                    for (int j = 0; j < uniqueCount; j++)
                    {
                        if (string.Equals(_scratchRawInterfaces[j].Name, raw.Name, StringComparison.Ordinal))
                        {
                            found = j;
                            break;
                        }
                    }
                }

                if (found < 0)
                {
                    found = uniqueCount++;
                    var compact = raw;
                    compact.AddressStartIndex = addressCount;
                    compact.AddressCount = 0;
                    _scratchRawInterfaces[found] = compact;
                }
                else
                {
                    var compact = _scratchRawInterfaces[found];
                    compact.IsDefaultRoute |= raw.IsDefaultRoute;
                    if (raw.Status == NetworkStatus.Up) compact.Status = NetworkStatus.Up;
                    if (string.IsNullOrEmpty(compact.Ssid) && !string.IsNullOrEmpty(raw.Ssid))
                    {
                        compact.Ssid = raw.Ssid;
                        compact.DisplayName = raw.DisplayName;
                    }
                    if (compact.LinkSpeedMbps <= 0) compact.LinkSpeedMbps = raw.LinkSpeedMbps;
                    _scratchRawInterfaces[found] = compact;
                }

                // Preserve distinct IPv4/IPv6 addresses across all reported copies.
                for (int a = 0; a < raw.AddressCount; a++)
                {
                    var candidate = _scratchAddresses[raw.AddressStartIndex + a];
                    var compact = _scratchRawInterfaces[found];
                    int existing = -1;
                    for (int k = 0; k < compact.AddressCount; k++)
                    {
                        int idx = compact.AddressStartIndex + k;
                        if (mergedAddresses[idx].Family == candidate.Family &&
                            string.Equals(mergedAddresses[idx].Address, candidate.Address, StringComparison.Ordinal))
                        {
                            existing = idx;
                            break;
                        }
                    }
                    if (existing >= 0)
                    {
                        if (string.IsNullOrEmpty(mergedAddresses[existing].Mask) && !string.IsNullOrEmpty(candidate.Mask))
                            mergedAddresses[existing] = candidate;
                    }
                    else
                    {
                        // Distinct interfaces may be interleaved; preserve contiguous address ranges.
                        // Address insertions for previously seen interfaces must shift later ranges.
                        int insertAt = compact.AddressStartIndex + compact.AddressCount;
                        if (insertAt < addressCount)
                        {
                            Array.Copy(mergedAddresses, insertAt, mergedAddresses, insertAt + 1, addressCount - insertAt);
                            for (int j = found + 1; j < uniqueCount; j++)
                            {
                                var later = _scratchRawInterfaces[j];
                                later.AddressStartIndex++;
                                _scratchRawInterfaces[j] = later;
                            }
                        }
                        mergedAddresses[insertAt] = candidate;
                        addressCount++;
                        compact.AddressCount++;
                        _scratchRawInterfaces[found] = compact;
                    }
                }
            }

            Array.Copy(mergedAddresses, _scratchAddresses, addressCount);
            return uniqueCount;
        }
#endif

        private static void EnrichDefaultGatewayFallback(RawInterfaceData[] rawInterfaces, int count, NetworkInterface[] nics)
        {
            if (nics == null) return;

            bool hasDefault = false;
            for (int i = 0; i < count; i++)
            {
                if (rawInterfaces[i].IsDefaultRoute)
                {
                    hasDefault = true;
                    break;
                }
            }

            if (!hasDefault)
            {
                for (int i = 0; i < count; i++)
                {
                    if (i < nics.Length && nics[i] != null && rawInterfaces[i].Status == NetworkStatus.Up)
                    {
                        try
                        {
                            var gateways = nics[i].GetIPProperties()?.GatewayAddresses;
                            if (gateways != null && gateways.Count > 0)
                            {
                                foreach (var gw in gateways)
                                {
                                    if (gw?.Address != null && gw.Address.ToString() != "0.0.0.0")
                                    {
                                        rawInterfaces[i].IsDefaultRoute = true;
                                        return;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        private static int ComputePriorityScore(ref RawInterfaceData data)
        {
            int score = 0;

            if (data.Status == NetworkStatus.Up) score += 1000;
            if (data.IsDefaultRoute) score += 500;

            switch (data.Type)
            {
                case NetworkType.WiFi:
                case NetworkType.Ethernet:
                    score += 200;
                    break;
                case NetworkType.Cellular:
                    score += 150;
                    break;
                case NetworkType.VPN:
                    score += 50;
                    break;
                case NetworkType.Loopback:
                    score -= 1000;
                    break;
                case NetworkType.Virtual:
                    score -= 200;
                    break;
            }

            if (data.LinkSpeedMbps > 0)
            {
                score += (int)Math.Min(data.LinkSpeedMbps, 500);
            }

            return score;
        }

        private static NetworkType ClassifyInterfaceType(NetworkInterface nic)
        {
            if (nic == null) return NetworkType.Unknown;

            string name = nic.Name ?? string.Empty;
            if (name == "lo" || name.StartsWith("lo:", StringComparison.Ordinal))
                return NetworkType.Loopback;
            if (name == "dummy" || name.StartsWith("dummy", StringComparison.Ordinal) ||
                name.StartsWith("ifb", StringComparison.Ordinal))
                return NetworkType.Virtual;

            var type = nic.NetworkInterfaceType;
            if (type == NetworkInterfaceType.Loopback)
                return NetworkType.Loopback;

            if (type == NetworkInterfaceType.Wireless80211)
                return NetworkType.WiFi;

            if (type == NetworkInterfaceType.Ethernet ||
                type == NetworkInterfaceType.GigabitEthernet ||
                type == NetworkInterfaceType.FastEthernetFx ||
                type == NetworkInterfaceType.FastEthernetT)
                return NetworkType.Ethernet;

            if (type == NetworkInterfaceType.Wwanpp ||
                type == NetworkInterfaceType.Wwanpp2)
                return NetworkType.Cellular;

            if (type == NetworkInterfaceType.Tunnel ||
                type == NetworkInterfaceType.Ppp ||
                type == NetworkInterfaceType.Slip)
                return NetworkType.VPN;

            string desc = nic.Description ?? string.Empty;

            if (name.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
                return NetworkType.Bluetooth;

            if (name.IndexOf("wi-fi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("wlan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0)
                return NetworkType.WiFi;

            if (name.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("wireguard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("tap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("tailscale", StringComparison.OrdinalIgnoreCase) >= 0)
                return NetworkType.VPN;

            if (name.IndexOf("vethernet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("hyper-v", StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf("vmware", StringComparison.OrdinalIgnoreCase) >= 0)
                return NetworkType.Virtual;

            return NetworkType.Other;
        }

        private static NetworkStatus MapOperationalStatus(OperationalStatus status)
        {
            return status switch
            {
                OperationalStatus.Up => NetworkStatus.Up,
                OperationalStatus.Down => NetworkStatus.Down,
                OperationalStatus.Testing => NetworkStatus.Testing,
                OperationalStatus.Dormant => NetworkStatus.Dormant,
                OperationalStatus.NotPresent => NetworkStatus.NotPresent,
                OperationalStatus.LowerLayerDown => NetworkStatus.LowerLayerDown,
                _ => NetworkStatus.Unknown
            };
        }

        private void RecordError(string message)
        {
            _errorCount++;
            _lastErrorMessage = message;
            NetworkLogger.LogWarning($"[Network] {message}");
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

        }

        private sealed class RawInterfaceComparer : System.Collections.Generic.IComparer<RawInterfaceData>
        {
            public static readonly RawInterfaceComparer Instance = new RawInterfaceComparer();

            public int Compare(RawInterfaceData x, RawInterfaceData y)
            {
                // Descending priority: higher scores first
                return y.PriorityScore.CompareTo(x.PriorityScore);
            }
        }

        #endregion
    }
}
