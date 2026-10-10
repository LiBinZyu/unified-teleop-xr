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
    }
}
