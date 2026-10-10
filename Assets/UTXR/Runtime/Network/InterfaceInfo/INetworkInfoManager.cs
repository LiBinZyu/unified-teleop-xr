using System;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Standardized interface for network information queries and lifecycle management.
    /// Hot-path reads are O(1) with 0 GC allocations and 0 OS/kernel calls.
    /// </summary>
    public interface INetworkInfoManager : IDisposable
    {
        /// <summary>
        /// Current version of the active network snapshot.
        /// </summary>
        int CurrentVersion { get; }

        /// <summary>
        /// Operational diagnostics detailing refresh counts, latency, and memory bounds.
        /// </summary>
        NetworkDiagnostics Diagnostics { get; }

        /// <summary>
        /// Reference to the active immutable snapshot.
        /// </summary>
        NetworkSnapshot CurrentSnapshot { get; }

        /// <summary>
        /// Retrieves interface information by index from the active snapshot.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        bool TryGetInterface(int index, out NetworkInterfaceInfo info);

        /// <summary>
        /// Retrieves assigned IP address information for an interface from the active snapshot.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        bool TryGetAddress(int interfaceIndex, int addressIndex, out IPAddressInfo address);

        /// <summary>
        /// Retrieves the index of the system default gateway egress interface.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        bool TryGetDefaultInterface(out int index);

        /// <summary>
        /// Queries the operating system routing table for a destination address on demand.
        /// Zero packets sent over the wire; backed directly by the kernel routing engine.
        /// </summary>
        bool QueryRoute(string destination, out RouteInfo route);

        /// <summary>
        /// Synchronously queries the operating system, rebuilds the snapshot, and publishes it.
        /// </summary>
        bool RefreshNow();

        /// <summary>
        /// Asynchronously requests a system refresh in the background.
        /// </summary>
        void RequestRefresh();

        /// <summary>
        /// Begins listening for OS network topology changes (e.g. Wi-Fi disconnect/reconnect).
        /// </summary>
        void StartWatching();

        /// <summary>
        /// Stops listening for OS network topology changes.
        /// </summary>
        void StopWatching();

        /// <summary>
        /// Event dispatched whenever the active snapshot is updated with a new version.
        /// </summary>
        event Action<int> OnInterfacesChanged;
    }
}
