using System;
using UnityEngine;

namespace Network.InterfaceInfo
{
    /// <summary>
    /// Unity MonoBehaviour service hosting the standardized NetworkInfoManager.
    /// Manages Unity lifecycle, Inspector configuration, and dispatches
    /// network change events safely to the Unity main thread.
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
        [Tooltip("Automatically register for OS network change events (reactive, event-driven).")]
        [SerializeField] private bool autoStartWatching = true;

        [Tooltip("Optional background polling interval in seconds. 0 = disabled (recommended, purely reactive).")]
        [SerializeField] private float pollInterval = 0f;

        [Header("Traffic Monitoring Configuration")]
        [Tooltip("Master switch to enable or disable real-time RX/TX network traffic throughput monitoring.")]
        [SerializeField] private bool enableTrafficMonitor = true;

        [Tooltip("Sampling and display update interval in seconds for throughput monitoring.")]
        [SerializeField] private float trafficUpdateInterval = 1.0f;

        [Tooltip("Display format for traffic throughput text (Mbps, AutoBytes, AutoBits).")]
        [SerializeField] private TrafficUnitFormat trafficFormat = TrafficUnitFormat.Mbps;

        private NetworkInfoManager _manager;
        private volatile int _pendingDispatchedVersion = -1;
        private float _lastPollTime;

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

            NetworkTrafficMonitor.SetEnabled(enableTrafficMonitor);
            InitializeManager();
        }

        private void InitializeManager()
        {
            if (_manager != null) return;

            _manager = new NetworkInfoManager(maxInterfaces, maxAddresses);
            _manager.OnInterfacesChanged += HandleManagerInterfacesChanged;

            _manager.RefreshNow();

            if (autoStartWatching)
            {
                _manager.StartWatching();
            }

            _lastPollTime = Time.realtimeSinceStartup;
        }

        private void Update()
        {
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
                    UnityEngine.Debug.LogWarning($"[NetworkInfoService] OnInterfacesChanged handler threw: {ex.Message}");
                }
            }

            if (pollInterval > 0f)
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastPollTime >= pollInterval)
                {
                    _lastPollTime = now;
                    RequestRefresh();
                }
            }
        }

        private void HandleManagerInterfacesChanged(int version)
        {
            _pendingDispatchedVersion = version;
        }

        private void OnDestroy()
        {
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
        /// Triggers an immediate synchronous system refresh and dispatches change events.
        /// </summary>
        /// []
        public bool RefreshNow()
        {
            if (_manager == null) InitializeManager();
            return _manager.RefreshNow();
        }

        /// <summary>
        /// Requests an asynchronous background system refresh.
        /// </summary>
        [ContextMenu("Request Refresh")]
        public void RequestRefresh()
        {
            if (_manager == null) InitializeManager();
            _manager.RequestRefresh();
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
    }
}
