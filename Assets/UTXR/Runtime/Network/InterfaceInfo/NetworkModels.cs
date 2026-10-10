using System;
using System.Net.Sockets;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Classification of network interfaces.
    /// </summary>
    public enum NetworkType : byte
    {
        Unknown = 0,
        WiFi,
        Ethernet,
        Cellular,
        VPN,
        Bluetooth,
        Loopback,
        Virtual,
        Other
    }

    /// <summary>
    /// Operational status of a network interface reported by the OS.
    /// </summary>
    public enum NetworkStatus : byte
    {
        Unknown = 0,
        Up,
        Down,
        Testing,
        Dormant,
        NotPresent,
        LowerLayerDown
    }

    /// <summary>
    /// Lightweight, zero-GC descriptor for an assigned IP address.
    /// </summary>
    public readonly struct IPAddressInfo : IEquatable<IPAddressInfo>
    {
        public readonly string Address;
        public readonly string Mask;
        public readonly AddressFamily Family;

        public bool IsIPv4 => Family == AddressFamily.InterNetwork;
        public bool IsIPv6 => Family == AddressFamily.InterNetworkV6;

        public IPAddressInfo(string address, string mask, AddressFamily family)
        {
            Address = address ?? string.Empty;
            Mask = mask ?? string.Empty;
            Family = family;
        }

        public bool Equals(IPAddressInfo other) =>
            string.Equals(Address, other.Address, StringComparison.Ordinal) &&
            string.Equals(Mask, other.Mask, StringComparison.Ordinal) &&
            Family == other.Family;

        public override bool Equals(object obj) => obj is IPAddressInfo other && Equals(other);
        public override int GetHashCode() => (Address != null ? Address.GetHashCode() : 0) ^ (int)Family;
        public override string ToString() => string.IsNullOrEmpty(Mask) ? Address : $"{Address}/{Mask}";

        public static bool operator ==(IPAddressInfo left, IPAddressInfo right) => left.Equals(right);
        public static bool operator !=(IPAddressInfo left, IPAddressInfo right) => !left.Equals(right);
    }

    /// <summary>
    /// Normalized, zero-allocation network interface descriptor.
    /// Immutable readonly struct ensuring O(1) safe reads and zero GC overhead on the hot path.
    /// </summary>
    public readonly struct NetworkInterfaceInfo
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string DisplayName;
        public readonly string Ssid;
        public readonly NetworkType Type;
        public readonly NetworkStatus Status;
        public readonly bool IsDefaultRoute;
        public readonly long LinkSpeedMbps;          // PHY/hardware link speed in Mbps (-1 if unavailable)
        public readonly long EstimatedBandwidthMbps;  // OS downstream throughput estimation (-1 if unavailable)
        public readonly double RxMbps;               // Live measured receive throughput in Mbps (-1 if unavailable)
        public readonly double TxMbps;               // Live measured send throughput in Mbps (-1 if unavailable)
        public readonly int AddressStartIndex;       // Start index in the snapshot's contiguous address table
        public readonly int AddressCount;            // Number of assigned IP addresses

        public bool IsUp => Status == NetworkStatus.Up;
        public bool IsLoopback => Type == NetworkType.Loopback;
        public bool IsVpn => Type == NetworkType.VPN;
        public bool IsVirtual => Type == NetworkType.Virtual;

        public NetworkInterfaceInfo(
            string id,
            string name,
            string displayName,
            string ssid,
            NetworkType type,
            NetworkStatus status,
            bool isDefaultRoute,
            long linkSpeedMbps,
            long estimatedBandwidthMbps,
            double rxMbps,
            double txMbps,
            int addressStartIndex,
            int addressCount)
        {
            Id = id ?? string.Empty;
            Name = name ?? string.Empty;
            DisplayName = !string.IsNullOrEmpty(displayName) ? displayName : (!string.IsNullOrEmpty(ssid) ? ssid : (name ?? string.Empty));
            Ssid = ssid ?? string.Empty;
            Type = type;
            Status = status;
            IsDefaultRoute = isDefaultRoute;
            LinkSpeedMbps = linkSpeedMbps;
            EstimatedBandwidthMbps = estimatedBandwidthMbps;
            RxMbps = rxMbps;
            TxMbps = txMbps;
            AddressStartIndex = addressStartIndex;
            AddressCount = addressCount;
        }

        public override string ToString()
        {
            string speed = LinkSpeedMbps > 0 ? $"{LinkSpeedMbps} Mbps" : "N/A";
            string rx = RxMbps >= 0 ? $"{RxMbps:F1} Mbps" : "N/A";
            string tx = TxMbps >= 0 ? $"{TxMbps:F1} Mbps" : "N/A";
            return $"[{Type}] {DisplayName} ({Name}) - Status: {Status}, DefaultRoute: {IsDefaultRoute}, LinkSpeed: {speed}, RX: {rx}, TX: {tx}, Addrs: {AddressCount}";
        }
    }

    /// <summary>
    /// Result of an on-demand OS routing table lookup.
    /// </summary>
    public readonly struct RouteInfo
    {
        public readonly int InterfaceIndex;
        public readonly string LocalIp;
        public readonly bool IsValid;

        public RouteInfo(int interfaceIndex, string localIp)
        {
            InterfaceIndex = interfaceIndex;
            LocalIp = localIp ?? string.Empty;
            IsValid = interfaceIndex >= 0 && !string.IsNullOrEmpty(localIp);
        }

        public override string ToString() => IsValid ? $"{LocalIp} via Interface[{InterfaceIndex}]" : "No Route";
    }

    /// <summary>
    /// Diagnostics snapshot providing operational metrics and memory bounds information.
    /// </summary>
    public readonly struct NetworkDiagnostics
    {
        public readonly int Version;
        public readonly int RefreshCount;
        public readonly long LastRefreshDurationMs;
        public readonly int ErrorCount;
        public readonly int MaxInterfaceCapacity;
        public readonly int MaxAddressCapacity;
        public readonly int CurrentInterfaceCount;
        public readonly int CurrentAddressCount;
        public readonly string LastErrorMessage;

        public NetworkDiagnostics(
            int version,
            int refreshCount,
            long lastRefreshDurationMs,
            int errorCount,
            int maxInterfaceCapacity,
            int maxAddressCapacity,
            int currentInterfaceCount,
            int currentAddressCount,
            string lastErrorMessage)
        {
            Version = version;
            RefreshCount = refreshCount;
            LastRefreshDurationMs = lastRefreshDurationMs;
            ErrorCount = errorCount;
            MaxInterfaceCapacity = maxInterfaceCapacity;
            MaxAddressCapacity = maxAddressCapacity;
            CurrentInterfaceCount = currentInterfaceCount;
            CurrentAddressCount = currentAddressCount;
            LastErrorMessage = lastErrorMessage ?? string.Empty;
        }

        public override string ToString() =>
            $"v{Version} | Refreshes: {RefreshCount} ({LastRefreshDurationMs}ms) | Errors: {ErrorCount} | " +
            $"Interfaces: {CurrentInterfaceCount}/{MaxInterfaceCapacity} | Addresses: {CurrentAddressCount}/{MaxAddressCapacity}";
    }

    /// <summary>
    /// Cold-path mutable builder struct used during snapshot generation and platform enrichment.
    /// </summary>
    internal struct RawInterfaceData
    {
        public string Id;
        public string Name;
        public string DisplayName;
        public string Ssid;
        public NetworkType Type;
        public NetworkStatus Status;
        public bool IsDefaultRoute;
        public long LinkSpeedMbps;
        public long EstimatedBandwidthMbps;
        public double RxMbps;
        public double TxMbps;
        public int AddressStartIndex;
        public int AddressCount;
        public int PriorityScore;
    }
}
