using System;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Thread-safe, immutable snapshot of network interfaces and assigned addresses.
    /// Provides zero-GC, O(1) reads over preallocated contiguous arrays.
    /// </summary>
    public sealed class NetworkSnapshot
    {
        public static readonly NetworkSnapshot Empty = new NetworkSnapshot(0, 0, -1, 0, DateTime.MinValue, Array.Empty<NetworkInterfaceInfo>(), Array.Empty<IPAddressInfo>());

        public readonly int Version;
        public readonly int InterfaceCount;
        public readonly int AddressCount;
        public readonly int DefaultInterfaceIndex; // -1 if no default route
        public readonly DateTime TimestampUtc;

        internal readonly NetworkInterfaceInfo[] Interfaces;
        internal readonly IPAddressInfo[] Addresses;

        public NetworkSnapshot(
            int version,
            int interfaceCount,
            int defaultInterfaceIndex,
            int addressCount,
            DateTime timestampUtc,
            NetworkInterfaceInfo[] interfaces,
            IPAddressInfo[] addresses)
        {
            Version = version;
            InterfaceCount = interfaceCount;
            DefaultInterfaceIndex = defaultInterfaceIndex;
            AddressCount = addressCount;
            TimestampUtc = timestampUtc;
            Interfaces = interfaces ?? Array.Empty<NetworkInterfaceInfo>();
            Addresses = addresses ?? Array.Empty<IPAddressInfo>();
        }

        /// <summary>
        /// Retrieves interface information by index.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        public bool TryGetInterface(int index, out NetworkInterfaceInfo info)
        {
            if ((uint)index < (uint)InterfaceCount)
            {
                info = Interfaces[index];
                return true;
            }

            info = default;
            return false;
        }

        /// <summary>
        /// Retrieves address information directly by global index in the snapshot address table.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        public bool TryGetAddress(int globalAddressIndex, out IPAddressInfo address)
        {
            if ((uint)globalAddressIndex < (uint)AddressCount)
            {
                address = Addresses[globalAddressIndex];
                return true;
            }

            address = default;
            return false;
        }

        /// <summary>
        /// Retrieves address information for an interface.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        public bool TryGetAddress(int interfaceIndex, int addressIndex, out IPAddressInfo address)
        {
            if ((uint)interfaceIndex < (uint)InterfaceCount)
            {
                ref readonly var iface = ref Interfaces[interfaceIndex];
                if ((uint)addressIndex < (uint)iface.AddressCount)
                {
                    int targetIdx = iface.AddressStartIndex + addressIndex;
                    if ((uint)targetIdx < (uint)AddressCount)
                    {
                        address = Addresses[targetIdx];
                        return true;
                    }
                }
            }

            address = default;
            return false;
        }

        /// <summary>
        /// Retrieves the index of the system default gateway egress interface.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        public bool TryGetDefaultInterface(out int index)
        {
            index = DefaultInterfaceIndex;
            return index >= 0 && index < InterfaceCount;
        }

        /// <summary>
        /// Finds the interface index matching the provided local IP address.
        /// </summary>
        public bool TryFindInterfaceByLocalIp(string localIp, out int interfaceIndex, out NetworkInterfaceInfo info)
        {
            if (!string.IsNullOrEmpty(localIp))
            {
                for (int i = 0; i < InterfaceCount; i++)
                {
                    ref readonly var iface = ref Interfaces[i];
                    for (int a = 0; a < iface.AddressCount; a++)
                    {
                        int addrIdx = iface.AddressStartIndex + a;
                        if ((uint)addrIdx < (uint)AddressCount && string.Equals(Addresses[addrIdx].Address, localIp, StringComparison.Ordinal))
                        {
                            interfaceIndex = i;
                            info = iface;
                            return true;
                        }
                    }
                }
            }

            interfaceIndex = -1;
            info = default;
            return false;
        }

        /// <summary>
        /// Finds the interface matching the provided target IP by comparing subnet masks.
        /// (LocalIP & SubnetMask) == (TargetIP & SubnetMask).
        /// Prioritizes physical interfaces (Ethernet, WiFi) over VPN and virtual interfaces.
        /// </summary>
        public bool TryFindInterfaceBySubnet(string targetIp, out int interfaceIndex, out NetworkInterfaceInfo info, out string localIpv4)
        {
            interfaceIndex = -1;
            info = default;
            localIpv4 = null;

            if (string.IsNullOrWhiteSpace(targetIp)) return false;
            if (!System.Net.IPAddress.TryParse(targetIp.Trim(), out var targetAddr) ||
                targetAddr.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;

            byte[] targetBytes = targetAddr.GetAddressBytes();

            int bestScore = int.MaxValue;
            int bestIdx = -1;
            string bestIp = null;

            for (int i = 0; i < InterfaceCount; i++)
            {
                ref readonly var iface = ref Interfaces[i];
                if (iface.IsLoopback || !iface.IsUp) continue;

                for (int a = 0; a < iface.AddressCount; a++)
                {
                    int addrIdx = iface.AddressStartIndex + a;
                    if ((uint)addrIdx >= (uint)AddressCount) continue;

                    ref readonly var addrInfo = ref Addresses[addrIdx];
                    if (!addrInfo.IsIPv4) continue;

                    if (System.Net.IPAddress.TryParse(addrInfo.Address, out var localAddr) &&
                        System.Net.IPAddress.TryParse(addrInfo.Mask, out var maskAddr))
                    {
                        byte[] localBytes = localAddr.GetAddressBytes();
                        byte[] maskBytes = maskAddr.GetAddressBytes();

                        bool match = true;
                        for (int b = 0; b < 4; b++)
                        {
                            if ((localBytes[b] & maskBytes[b]) != (targetBytes[b] & maskBytes[b]))
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            int score = 5;
                            if (iface.Type == NetworkType.Ethernet) score = 1;
                            else if (iface.Type == NetworkType.WiFi) score = 2;
                            else if (iface.IsVpn || iface.IsVirtual) score = 10;

                            if (score < bestScore)
                            {
                                bestScore = score;
                                bestIdx = i;
                                bestIp = addrInfo.Address;
                            }
                        }
                    }
                }
            }

            if (bestIdx >= 0)
            {
                interfaceIndex = bestIdx;
                info = Interfaces[bestIdx];
                localIpv4 = bestIp;
                return true;
            }

            // Fallback: /24 prefix string match
            int lastDot = targetIp.LastIndexOf('.');
            if (lastDot > 0)
            {
                string targetSubnet = targetIp.Substring(0, lastDot);
                for (int i = 0; i < InterfaceCount; i++)
                {
                    ref readonly var iface = ref Interfaces[i];
                    if (iface.IsLoopback || !iface.IsUp) continue;

                    string ip = GetPrimaryIPv4(i);
                    if (!string.IsNullOrEmpty(ip) && ip.StartsWith(targetSubnet, StringComparison.OrdinalIgnoreCase))
                    {
                        interfaceIndex = i;
                        info = iface;
                        localIpv4 = ip;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the interface matching the given unique Id or Name, and returns its primary IPv4.
        /// </summary>
        public bool TryFindInterfaceById(string interfaceId, out int interfaceIndex, out NetworkInterfaceInfo info, out string localIpv4)
        {
            interfaceIndex = -1;
            info = default;
            localIpv4 = null;

            if (string.IsNullOrWhiteSpace(interfaceId)) return false;

            for (int i = 0; i < InterfaceCount; i++)
            {
                ref readonly var iface = ref Interfaces[i];
                if (string.Equals(iface.Id, interfaceId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(iface.Name, interfaceId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(iface.DisplayName, interfaceId, StringComparison.OrdinalIgnoreCase))
                {
                    interfaceIndex = i;
                    info = iface;
                    localIpv4 = GetPrimaryIPv4(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Retrieves the primary IPv4 unicast address for the interface at index.
        /// </summary>
        public string GetPrimaryIPv4(int interfaceIndex)
        {
            if ((uint)interfaceIndex < (uint)InterfaceCount)
            {
                ref readonly var iface = ref Interfaces[interfaceIndex];
                for (int a = 0; a < iface.AddressCount; a++)
                {
                    int addrIdx = iface.AddressStartIndex + a;
                    if ((uint)addrIdx < (uint)AddressCount && Addresses[addrIdx].IsIPv4)
                    {
                        return Addresses[addrIdx].Address;
                    }
                }
            }
            return null;
        }
    }
}
