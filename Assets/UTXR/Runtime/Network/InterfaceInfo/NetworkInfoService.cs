using System;
using System.Collections.Generic;
using UnityEngine;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Unity MonoBehaviour service hosting the standardized NetworkInfoManager.
    /// Polls the network topology and dispatches snapshot updates on the Unity main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetworkInfoService : MonoBehaviour
    {
        public static NetworkInfoService Instance { get; private set; }

        [Header("Capacity Configuration")]
        [Tooltip("Maximum network interface capacity. Exceeding this reports a diagnostics error.")]
        [SerializeField] private int maxInterfaces = NetworkInfoManager.DefaultMaxInterfaces;

        [Tooltip("Maximum IP address capacity. Exceeding this reports a diagnostics error.")]
        [SerializeField] private int maxAddresses = NetworkInfoManager.DefaultMaxAddresses;

        [Header("Lifecycle & Monitoring")]
        [Tooltip("Network topology refresh interval (seconds). Values <= 0 in old scenes use 5 seconds.")]
        [SerializeField] private float pollInterval = 5f;

        [Header("Traffic Monitoring Configuration")]
        [Tooltip("Master switch to enable or disable real-time RX/TX network traffic throughput monitoring.")]
        [SerializeField] private bool enableTrafficMonitor = true;

        [Tooltip("Sampling and display update interval in seconds for throughput monitoring.")]
        [SerializeField] private float trafficUpdateInterval = 1.0f;

        [Tooltip("Display format for traffic throughput text (Mbps, AutoBytes, AutoBits).")]
        [SerializeField] private TrafficUnitFormat trafficFormat = TrafficUnitFormat.Mbps;

        [Header("Logging Configuration")]
        [Tooltip("Master switch to enable or disable Unity console Debug.Log output for the network module.")]
        [SerializeField] private bool enableDebugLog = true;

        [Tooltip("Master switch to enable or disable persistent file logging (UTXR.Core.Logger) for the network module.")]
        [SerializeField] private bool enableFileLog = true;

        private NetworkInfoManager _manager;
        private volatile int _pendingDispatchedVersion = -1;
        private float _lastPollTime;
        private float _lastTrafficTime;
        private volatile bool _refreshRequested;
        private readonly Dictionary<string, NetworkTrafficMonitor.Sampler> _trafficSamplers =
            new Dictionary<string, NetworkTrafficMonitor.Sampler>(StringComparer.Ordinal);

        /// <summary>
        /// Global switch state indicating whether RX/TX traffic monitoring is currently enabled.
        /// Directly delegates to NetworkTrafficMonitor.IsEnabled.
        /// </summary>
        public bool IsTrafficMonitoringEnabled => NetworkTrafficMonitor.IsEnabled;

        /// <summary>
        /// Sampling and display update interval in seconds for traffic monitoring.
        /// </summary>
        public float TrafficUpdateInterval => trafficUpdateInterval;

        /// <summary>
        /// Active display formatting rule for traffic throughput text.
        /// </summary>
        public TrafficUnitFormat TrafficFormat => trafficFormat;

        /// <summary>
        /// Dispatched on the Unity main thread when traffic monitoring is toggled on or off.
        /// </summary>
        public event Action<bool> OnTrafficMonitoringChanged
        {
            add => NetworkTrafficMonitor.OnMonitoringToggled += value;
            remove => NetworkTrafficMonitor.OnMonitoringToggled -= value;
        }

        /// <summary>
        /// Underlying pure C# network manager engine.
        /// </summary>
        public INetworkInfoManager Manager => _manager;

        /// <summary>
        /// Reference to the active immutable snapshot.
        /// Hot path: O(1), 0 GC.
        /// </summary>
        public NetworkSnapshot CurrentSnapshot => _manager != null ? _manager.CurrentSnapshot : NetworkSnapshot.Empty;

        /// <summary>
        /// Current version of the active network snapshot.
        /// </summary>
        public int CurrentVersion => _manager != null ? _manager.CurrentVersion : 0;

        /// <summary>
        /// Operational diagnostics detailing refresh counts, latency, and memory bounds.
        /// </summary>
        public NetworkDiagnostics Diagnostics => _manager != null ? _manager.Diagnostics : default;

        /// <summary>
        /// Standardized event dispatched on the Unity main thread when interfaces change.
        /// Passes the new snapshot version number.
        /// </summary>
        public event Action<int> OnInterfacesChanged;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                if (transform.parent == null && Application.isPlaying)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            NetworkLogger.EnableDebugLog = enableDebugLog;
            NetworkLogger.EnableFileLog = enableFileLog;
            NetworkTrafficMonitor.SetEnabled(enableTrafficMonitor);
            NetworkTrafficMonitor.OnMonitoringToggled += HandleMonitoringToggled;
            InitializeManager();
        }

        private void OnValidate()
        {
            NetworkLogger.EnableDebugLog = enableDebugLog;
            NetworkLogger.EnableFileLog = enableFileLog;
        }

        private void InitializeManager()
        {
            if (_manager != null) return;

            _manager = new NetworkInfoManager(maxInterfaces, maxAddresses);
            _manager.OnInterfacesChanged += HandleManagerInterfacesChanged;

            _manager.RefreshNow();

            // Unity keeps Inspector values serialized: migrate the former event-only (0s) setting.
            if (pollInterval <= 0f) pollInterval = 5f;
            _lastPollTime = Time.realtimeSinceStartup;
            _lastTrafficTime = Time.realtimeSinceStartup - Mathf.Max(0.05f, trafficUpdateInterval);
        }

        private void Update()
        {
            if (_refreshRequested)
            {
                _refreshRequested = false;
                RefreshNow();
                _lastPollTime = Time.realtimeSinceStartup;
            }

            int pending = _pendingDispatchedVersion;
            if (pending > 0)
            {
                _pendingDispatchedVersion = -1;
                try
                {
                    OnInterfacesChanged?.Invoke(pending);
                }
                catch (Exception ex)
                {
                    NetworkLogger.LogWarning($"[Network] OnInterfacesChanged handler threw: {ex.Message}");
                }
            }

            if (NetworkTrafficMonitor.IsEnabled && Time.realtimeSinceStartup - _lastTrafficTime >= Mathf.Max(0.05f, trafficUpdateInterval))
            {
                _lastTrafficTime = Time.realtimeSinceStartup;
                SampleTraffic();
            }

            if (pollInterval > 0f)
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastPollTime >= pollInterval)
                {
                    _lastPollTime = now;
                    // Poll on Unity main thread: Android Wi-Fi JNI stays on the attached Unity thread.
                    // Typical refresh is a few ms and happens only once per poll interval.
                    RefreshNow();
                }
            }
        }

        private void HandleManagerInterfacesChanged(int version)
        {
            _pendingDispatchedVersion = version;
        }

        // Keep samplers across topology polls; restarting every snapshot prevents usable deltas.
        private void SampleTraffic()
        {
            var snapshot = CurrentSnapshot;
            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                var iface = snapshot.Interfaces[i];
                // Allow valid IP interfaces even if Android reports an unreliable status.
                if (iface.IsLoopback || iface.AddressCount == 0 ||
                    string.IsNullOrEmpty(iface.Name) ||
                    (iface.IsVirtual && string.IsNullOrEmpty(snapshot.GetPrimaryIPv4(i)))) continue;
                if (!_trafficSamplers.TryGetValue(iface.Name, out var sampler))
                {
                    sampler = new NetworkTrafficMonitor.Sampler();
                    _trafficSamplers.Add(iface.Name, sampler);
                }
                sampler.UpdateSample(iface.Name, iface.Id);
            }
        }

        /// <summary>Live per-interface traffic, separate from the topology snapshot.
        /// False means not sampled/unsupported; zero is a valid measured rate.</summary>
        public bool TryGetTraffic(string interfaceIdOrName, out double rxMbps, out double txMbps)
        {
            rxMbps = -1;
            txMbps = -1;
            if (!NetworkTrafficMonitor.IsEnabled || string.IsNullOrEmpty(interfaceIdOrName)) return false;

            var snapshot = CurrentSnapshot;
            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                var iface = snapshot.Interfaces[i];
                if (!string.Equals(iface.Id, interfaceIdOrName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(iface.Name, interfaceIdOrName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!_trafficSamplers.TryGetValue(iface.Name, out var sampler) || !sampler.HasSample) return false;
                rxMbps = sampler.RxMbps;
                txMbps = sampler.TxMbps;
                return true;
            }
            return false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Refresh on the next Update after resuming, on Unity's main thread.
        private void OnApplicationPause(bool paused)
        {
            if (!paused && _manager != null) RequestRefresh();
        }
#endif

        private void HandleMonitoringToggled(bool enabled)
        {
            if (!enabled) _trafficSamplers.Clear();
            else _lastTrafficTime = Time.realtimeSinceStartup - Mathf.Max(0.05f, trafficUpdateInterval);
        }

        private void OnDestroy()
        {
            NetworkTrafficMonitor.OnMonitoringToggled -= HandleMonitoringToggled;
            _trafficSamplers.Clear();
            if (_manager != null)
            {
                _manager.OnInterfacesChanged -= HandleManagerInterfacesChanged;
                _manager.Dispose();
                _manager = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Triggers an immediate synchronous system refresh.
        /// </summary>
        public bool RefreshNow()
        {
            if (_manager == null) InitializeManager();
            return _manager.RefreshNow();
        }

        /// <summary>
        /// Requests a refresh on the next Unity Update; safe for Android JNI calls.
        /// </summary>
        [ContextMenu("Request Refresh")]
        public void RequestRefresh()
        {
            _refreshRequested = true;
        }

        #region Traffic Monitoring Controls

        /// <summary>
        /// Enables or disables real-time RX/TX traffic monitoring across the application.
        /// Directly invokes NetworkTrafficMonitor.SetEnabled(enabled).
        /// When disabled, sampling timers pause, completely eliminating background system calls and GC allocations.
        /// </summary>
        public void SetTrafficMonitoringEnabled(bool enabled)
        {
            enableTrafficMonitor = enabled;
            NetworkTrafficMonitor.SetEnabled(enabled);
        }

        /// <summary>
        /// Turns ON real-time RX/TX traffic throughput monitoring.
        /// </summary>
        [ContextMenu("Enable Traffic Monitor")]
        public void EnableTrafficMonitor()
        {
            NetworkTrafficMonitor.Enable();
        }

        /// <summary>
        /// Turns OFF real-time RX/TX traffic throughput monitoring to save CPU and stop polling.
        /// </summary>
        [ContextMenu("Disable Traffic Monitor")]
        public void DisableTrafficMonitor()
        {
            NetworkTrafficMonitor.Disable();
        }

        /// <summary>
        /// Toggles real-time RX/TX traffic throughput monitoring state.
        /// </summary>
        [ContextMenu("Toggle Traffic Monitor")]
        public void ToggleTrafficMonitor()
        {
            NetworkTrafficMonitor.Toggle();
        }

        #endregion

        [ContextMenu("Debug Network Interfaces")]
        public void DebugNetworkInterfaces()
        {
            var snapshot = CurrentSnapshot;
            var diagnostics = Diagnostics;
            NetworkLogger.Log($"[Network] {diagnostics} | LastError: {diagnostics.LastErrorMessage} | " +
                      $"ServiceActive={isActiveAndEnabled} TrafficEnabled={NetworkTrafficMonitor.IsEnabled} " +
                      $"Poll={pollInterval:F1}s SampleInterval={trafficUpdateInterval:F1}s Samplers={_trafficSamplers.Count}");
            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                if (!snapshot.TryGetInterface(i, out var nic)) continue;
                bool ok = TryGetTraffic(nic.Id, out double rx, out double tx);
                bool hasSampler = _trafficSamplers.TryGetValue(nic.Name, out var sampler);
                string traffic = ok ? $"RX={rx:F3} TX={tx:F3} Mbps" : "N/A";
                bool rawOk = NetworkTrafficMonitor.TryReadBytes(nic.Name, nic.Id, out long rawRx, out long rawTx);
                NetworkLogger.Log($"[Network] #{i} {nic.Name} ({nic.Type}) IP={snapshot.GetPrimaryIPv4(i)} " +
                          $"Addresses={nic.AddressCount} | SamplerExists={hasSampler} " +
                          $"HasSample={(hasSampler && sampler.HasSample)} | Traffic={traffic} " +
                          $"| RawCounter={(rawOk ? $"{rawRx}/{rawTx}" : "unsupported")}");
            }
        }

        #region Subnet Matching & Interface Lookup API

        /// <summary>
        /// Finds the network interface matching the target IP's subnet.
        /// (LocalIP & SubnetMask) == (TargetIP & SubnetMask), prioritizing physical interfaces.
        /// </summary>
        public static bool FindInterfaceBySubnet(string targetIp, out NetworkInterfaceInfo info, out string localIpv4)
        {
            var snapshot = GetSnapshotSafe();
            return snapshot.TryFindInterfaceBySubnet(targetIp, out _, out info, out localIpv4);
        }

        /// <summary>
        /// Resolves the IPv4 address for an interface identified by unique Id, Name, or DisplayName.
        /// </summary>
        public static string GetIpv4ForInterface(string interfaceId)
        {
            var snapshot = GetSnapshotSafe();
            if (snapshot.TryFindInterfaceById(interfaceId, out _, out _, out string localIpv4))
            {
                return localIpv4;
            }
            return null;
        }

        /// <summary>
        /// Returns the active network snapshot safely, generating an on-demand snapshot if the service instance is not yet initialized.
        /// </summary>
        public static NetworkSnapshot GetSnapshotSafe()
        {
            if (Instance != null && Instance.CurrentSnapshot != null && Instance.CurrentSnapshot.InterfaceCount > 0)
            {
                return Instance.CurrentSnapshot;
            }
            var tempMgr = new NetworkInfoManager(32, 128);
            tempMgr.RefreshNow();
            return tempMgr.CurrentSnapshot;
        }

        /// <summary>
        /// Retrieves all operational, non-loopback network interfaces and their primary IPv4 addresses.
        /// Pure data list completely decoupled from any UI framework.
        /// </summary>
        public static List<(NetworkInterfaceInfo Info, string LocalIp)> GetActiveInterfaces()
        {
            var snapshot = GetSnapshotSafe();
            var list = new List<(NetworkInterfaceInfo, string)>();
            for (int i = 0; i < snapshot.InterfaceCount; i++)
            {
                var iface = snapshot.Interfaces[i];
                if (iface.IsLoopback || !iface.IsUp ||
                    (iface.IsVirtual && iface.Name.StartsWith("dummy", StringComparison.Ordinal))) continue;
                string ip = snapshot.GetPrimaryIPv4(i);
                if (!string.IsNullOrEmpty(ip) &&
                    System.Net.IPAddress.TryParse(ip, out var address) &&
                    !System.Net.IPAddress.IsLoopback(address))
                {
                    list.Add((iface, ip));
                }
            }
            return list;
        }

        #endregion
    }
}
