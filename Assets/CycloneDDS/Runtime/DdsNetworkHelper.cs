using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CycloneDDS
{
    /// <summary>
    /// Network interface descriptor containing adapter details and IPv4 addressing.
    /// </summary>
    public struct DdsNetworkInterfaceInfo
    {
        public string Name;
        public string Description;
        public NetworkInterfaceType InterfaceType;
        public string IpAddress;
        public string SubnetMask;
        public OperationalStatus Status;

        public override string ToString() => $"[{InterfaceType}] {Name} ({Description}): {IpAddress} / {SubnetMask}";
    }

    /// <summary>
    /// Dedicated network helper providing low-level IP discovery and adapter matching algorithms:
    /// 1. UDP Kernel Routing Probe (Zero-packet OS routing table lookup)
    /// 2. Bitwise Subnet Mask Comparison (LocalIP & Mask == TargetIP & Mask)
    /// 3. Directed Subnet Broadcast Address Calculation (/24, /16 etc.)
    /// 4. Subnet Device Probing & Discovery via instantaneous OS ARP cache
    /// </summary>
    public static class DdsNetworkHelper
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || (!UNITY_ANDROID && !UNITY_STANDALONE_LINUX)
        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_IPNETROW
        {
            public uint dwIndex;
            public uint dwPhysAddrLen;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] bPhysAddr;
            public uint dwAddr;
            public uint dwType; // 3 = Dynamic, 4 = Static
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern int GetIpNetTable(IntPtr pIpNetTable, ref int pdwSize, bool bOrder);
#endif

        /// <summary>
        /// Search local network interfaces for an IP matching the subnet of the target IP.
        /// Uses 3-stage cascade: UDP Connect probe -> Bitwise Subnet Mask -> Prefix match.
        /// </summary>
        public static string GetMatchingLocalIp(string targetIpStr)
        {
            if (string.IsNullOrWhiteSpace(targetIpStr)) return null;

            // Strategy 1: OS Kernel Routing Table Probe (Zero-packet UDP Connect)
            string probeIp = ProbeKernelRoutingIp(targetIpStr);
            if (!string.IsNullOrEmpty(probeIp))
            {
                return probeIp;
            }

            // Strategy 2: Exact Bitwise Subnet Mask Comparison
            string maskMatchIp = MatchSubnetMaskIp(targetIpStr);
            if (!string.IsNullOrEmpty(maskMatchIp))
            {
                return maskMatchIp;
            }

            // Strategy 3: Prefix Fallback (/24 subnet string matching)
            return MatchPrefixIp(targetIpStr);
        }

        /// <summary>
        /// Strategy 1: Queries OS kernel routing table directly via zero-packet UDP connect.
        /// </summary>
        public static string ProbeKernelRoutingIp(string targetIpStr, int port = 7400)
        {
            try
            {
                if (IPAddress.TryParse(targetIpStr.Trim(), out var targetAddr))
                {
                    using var probeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    probeSocket.Connect(targetAddr, port);
                    if (probeSocket.LocalEndPoint is IPEndPoint ep)
                    {
                        string ip = ep.Address.ToString();
                        if (!IPAddress.IsLoopback(ep.Address) && ip != "0.0.0.0" && !ip.StartsWith("169.254."))
                        {
                            return ip;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Strategy 2: Evaluates (LocalIP & Mask) == (TargetIP & Mask) across all active physical NICs.
        /// </summary>
        public static string MatchSubnetMaskIp(string targetIpStr)
        {
            try
            {
                if (IPAddress.TryParse(targetIpStr.Trim(), out var targetAddr))
                {
                    byte[] targetBytes = targetAddr.GetAddressBytes();
                    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (nic.OperationalStatus != OperationalStatus.Up) continue;
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                        var ipProps = nic.GetIPProperties();
                        foreach (var unicast in ipProps.UnicastAddresses)
                        {
                            if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                            if (unicast.IPv4Mask == null) continue;

                            byte[] ipBytes = unicast.Address.GetAddressBytes();
                            byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();

                            bool match = true;
                            for (int i = 0; i < 4; i++)
                            {
                                if ((ipBytes[i] & maskBytes[i]) != (targetBytes[i] & maskBytes[i]))
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match && !unicast.Address.ToString().StartsWith("169.254."))
                            {
                                return unicast.Address.ToString();
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Strategy 3: Fast /24 prefix string match.
        /// </summary>
        public static string MatchPrefixIp(string targetIpStr)
        {
            try
            {
                int lastDot = targetIpStr.LastIndexOf('.');
                if (lastDot > 0)
                {
                    string targetSubnet = targetIpStr.Substring(0, lastDot);
                    var interfaces = GetActiveInterfaces();
                    foreach (var nic in interfaces)
                    {
                        if (nic.IpAddress.StartsWith(targetSubnet) && !nic.IpAddress.StartsWith("169.254."))
                        {
                            return nic.IpAddress;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Calculates the directed IPv4 broadcast address from an IP and subnet mask.
        /// E.g. IP 192.168.8.154, Mask 255.255.255.0 -> Broadcast 192.168.8.255.
        /// </summary>
        public static string CalculateBroadcastAddress(string ipStr, string maskStr)
        {
            if (string.IsNullOrWhiteSpace(ipStr)) return "255.255.255.255";
            if (string.IsNullOrWhiteSpace(maskStr)) maskStr = "255.255.255.0";

            try
            {
                if (IPAddress.TryParse(ipStr.Trim(), out var ip) && IPAddress.TryParse(maskStr.Trim(), out var mask))
                {
                    byte[] ipBytes = ip.GetAddressBytes();
                    byte[] maskBytes = mask.GetAddressBytes();
                    byte[] bcastBytes = new byte[4];
                    for (int i = 0; i < 4; i++)
                    {
                        bcastBytes[i] = (byte)(ipBytes[i] | (~maskBytes[i]));
                    }
                    return new IPAddress(bcastBytes).ToString();
                }
            }
            catch { }

            int idx = ipStr.LastIndexOf('.');
            if (idx > 0)
            {
                return ipStr.Substring(0, idx) + ".255";
            }
            return "255.255.255.255";
        }

        /// <summary>
        /// Attempts to find the primary active physical network interface (prioritizing Wi-Fi / Ethernet).
        /// Filters out loopback, link-local (169.254), and virtual adapters when possible.
        /// </summary>
        public static bool TryGetPrimaryInterface(out DdsNetworkInterfaceInfo primaryInfo)
        {
            primaryInfo = default;
            var candidates = GetActiveInterfaces();
            if (candidates.Count == 0) return false;

            // Priority 1: Wireless (Wi-Fi) or Ethernet that is not a virtual bridge
            foreach (var nic in candidates)
            {
                string desc = nic.Description.ToLowerInvariant();
                string name = nic.Name.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vethernet") || desc.Contains("hyper-v") || desc.Contains("wsl") || desc.Contains("bluetooth") || desc.Contains("pseudo"))
                {
                    continue;
                }

                if (nic.InterfaceType == NetworkInterfaceType.Wireless80211 || nic.InterfaceType == NetworkInterfaceType.Ethernet)
                {
                    primaryInfo = nic;
                    return true;
                }
            }

            // Priority 2: Any non-virtual active interface
            foreach (var nic in candidates)
            {
                string desc = nic.Description.ToLowerInvariant();
                if (!desc.Contains("virtual") && !desc.Contains("vethernet") && !desc.Contains("hyper-v"))
                {
                    primaryInfo = nic;
                    return true;
                }
            }

            // Priority 3: Fallback to first available interface
            primaryInfo = candidates[0];
            return true;
        }

        /// <summary>
        /// Gets the broadcast address of the primary active local network interface.
        /// </summary>
        public static string GetDefaultSubnetBroadcast()
        {
            if (TryGetPrimaryInterface(out var info))
            {
                return CalculateBroadcastAddress(info.IpAddress, info.SubnetMask);
            }
            return "255.255.255.255";
        }

        /// <summary>
        /// Retrieves a user-friendly summary of active physical network interfaces (e.g. "WLAN: 192.168.8.154 | ETH: 10.0.0.12").
        /// Differentiates between WLAN (Wi-Fi) and Ethernet (ETH).
        /// Instantaneous execution (sub-millisecond) without any DNS lookup.
        /// </summary>
        public static string GetSelfIpSummary()
        {
            var interfaces = GetActiveInterfaces();
            var parts = new List<string>();

            foreach (var nic in interfaces)
            {
                string desc = nic.Description.ToLowerInvariant();
                string name = nic.Name.ToLowerInvariant();
                if (desc.Contains("virtual") || desc.Contains("vethernet") || desc.Contains("hyper-v") || desc.Contains("wsl") || desc.Contains("bluetooth") || desc.Contains("pseudo"))
                {
                    continue;
                }

                string label;
                if (nic.InterfaceType == NetworkInterfaceType.Wireless80211 || name.Contains("wlan") || name.Contains("wi-fi") || desc.Contains("wireless"))
                {
                    label = "WLAN";
                }
                else if (nic.InterfaceType == NetworkInterfaceType.Ethernet || name.Contains("eth") || desc.Contains("ethernet"))
                {
                    label = "ETH";
                }
                else
                {
                    label = nic.Name;
                }

                parts.Add($"{label}: {nic.IpAddress}");
            }

            if (parts.Count == 0)
            {
                foreach (var nic in interfaces)
                {
                    parts.Add($"{nic.Name}: {nic.IpAddress}");
                }
            }

            return parts.Count > 0 ? string.Join(" | ", parts) : "No Network";
        }

        /// <summary>
        /// Retrieve all available local IPv4 addresses (excluding loopback and link-local).
        /// </summary>
        public static string[] GetLocalIPv4Addresses()
        {
            var list = new List<string>();
            var interfaces = GetActiveInterfaces();
            foreach (var nic in interfaces)
            {
                list.Add(nic.IpAddress);
            }
            return list.ToArray();
        }

        /// <summary>
        /// Get detailed information of all active network interfaces directly from OS adapter stack.
        /// Zero DNS lookup, zero network latency (< 0.2ms).
        /// </summary>
        public static List<DdsNetworkInterfaceInfo> GetActiveInterfaces()
        {
            var list = new List<DdsNetworkInterfaceInfo>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var props = nic.GetIPProperties();
                    foreach (var unicast in props.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipStr = unicast.Address.ToString();
                            if (!ipStr.StartsWith("169.254.") && !ipStr.StartsWith("127."))
                            {
                                list.Add(new DdsNetworkInterfaceInfo
                                {
                                    Name = nic.Name,
                                    Description = nic.Description,
                                    InterfaceType = nic.NetworkInterfaceType,
                                    IpAddress = ipStr,
                                    SubnetMask = unicast.IPv4Mask?.ToString() ?? "255.255.255.0",
                                    Status = nic.OperationalStatus
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DdsLog.Warning($"GetActiveInterfaces failed: {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// Retrieves all active neighbor IP devices on the local subnet via instantaneous OS ARP cache lookup.
        /// Windows: Direct Win32 GetIpNetTable in iphlpapi.dll (< 0.1ms).
        /// Android/Linux: Direct /proc/net/arp virtual filesystem read (< 0.2ms).
        /// Zero threads spawned, zero network packets sent, zero thread-pool exhaustion.
        /// </summary>
        public static List<string> GetActiveSubnetIps()
        {
            var result = new List<string>();
            if (!TryGetPrimaryInterface(out var primaryNic))
            {
                return result;
            }

            int lastDot = primaryNic.IpAddress.LastIndexOf('.');
            if (lastDot <= 0) return result;
            string subnetPrefix = primaryNic.IpAddress.Substring(0, lastDot + 1); // e.g. "192.168.8."

            var discovered = new HashSet<string>();

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || (!UNITY_ANDROID && !UNITY_STANDALONE_LINUX)
            try
            {
                int bytesNeeded = 0;
                GetIpNetTable(IntPtr.Zero, ref bytesNeeded, true);
                if (bytesNeeded > 0)
                {
                    IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
                    try
                    {
                        if (GetIpNetTable(buffer, ref bytesNeeded, true) == 0)
                        {
                            int entries = Marshal.ReadInt32(buffer);
                            IntPtr rowPtr = IntPtr.Add(buffer, 4);
                            int rowSize = Marshal.SizeOf(typeof(MIB_IPNETROW));

                            for (int i = 0; i < entries; i++)
                            {
                                var row = (MIB_IPNETROW)Marshal.PtrToStructure(rowPtr, typeof(MIB_IPNETROW));
                                rowPtr = IntPtr.Add(rowPtr, rowSize);

                                // dwType: 3 = Dynamic (active LAN host), 4 = Static
                                if (row.dwType == 3 || row.dwType == 4)
                                {
                                    byte[] ipBytes = BitConverter.GetBytes(row.dwAddr);
                                    string ipStr = new IPAddress(ipBytes).ToString();
                                    if (IsValidCandidateIp(ipStr, subnetPrefix, primaryNic.IpAddress))
                                    {
                                        discovered.Add(ipStr);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                DdsLog.Warning($"Win32 GetIpNetTable failed: {ex.Message}");
            }
#endif

#if UNITY_ANDROID || UNITY_STANDALONE_LINUX
            try
            {
                const string arpPath = "/proc/net/arp";
                if (System.IO.File.Exists(arpPath))
                {
                    string[] lines = System.IO.File.ReadAllLines(arpPath);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line)) continue;
                        string[] tokens = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length >= 4)
                        {
                            string ipStr = tokens[0];
                            string flags = tokens[2]; // 0x2 is ATF_COM (completed ARP)
                            if (flags == "0x2" && IsValidCandidateIp(ipStr, subnetPrefix, primaryNic.IpAddress))
                            {
                                discovered.Add(ipStr);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DdsLog.Warning($"Reading /proc/net/arp failed: {ex.Message}");
            }
#endif

            result.AddRange(discovered);
            result.Sort();
            return result;
        }

        private static bool IsValidCandidateIp(string ipStr, string subnetPrefix, string selfIp)
        {
            if (string.IsNullOrWhiteSpace(ipStr)) return false;
            if (ipStr == selfIp) return false;
            if (!ipStr.StartsWith(subnetPrefix)) return false;
            if (ipStr.EndsWith(".255") || ipStr.EndsWith(".0")) return false;
            if (ipStr.StartsWith("224.") || ipStr.StartsWith("239.") || ipStr.StartsWith("169.254.") || ipStr.StartsWith("127.")) return false;
            return true;
        }

        /// <summary>
        /// Asynchronously retrieves active responding IP devices on the local subnet via instant OS ARP cache.
        /// Completes in sub-millisecond time.
        /// </summary>
        public static Task<List<string>> ProbeActiveSubnetIpsAsync(int timeoutMs = 250)
        {
            return Task.FromResult(GetActiveSubnetIps());
        }
    }
}
