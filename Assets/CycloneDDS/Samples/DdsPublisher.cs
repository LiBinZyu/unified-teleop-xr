using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace CycloneDDS
{
    /// <summary>
    /// Data-driven CycloneDDS Publisher using DdsInputActionChannel ScriptableAssets.
    /// Publishes at the hardware native refresh rate (via Application.onBeforeRender),
    /// with publishRateHz acting as an upper rate limit (throttle cap).
    /// Safe from cross-thread race conditions with InputSystem buffers.
    /// </summary>
    public class DdsPublisher : MonoBehaviour
    {
        public static DdsPublisher Instance { get; private set; }

        [Header("DDS Network Settings")]
        [Tooltip("Target Peer IP address (e.g. 192.168.8.181). Leave empty to auto-detect and broadcast to all devices on the local subnet.")]
        public string targetIp = "192.168.8.181";

        [Tooltip("DDS Domain ID (Default 0)")]
        public int domainId = 0;

        [Tooltip("Direct DDS traffic to bind strictly to the matching local network interface (does NOT affect Wi-Fi or other apps)")]
        public bool bindMatchingInterface = true;

        [Tooltip("Allow UDP multicast discovery (Default true)")]
        public bool allowMulticast = true;

        [Tooltip("Allow CycloneDDS to autodetermine network interface")]
        public bool autodetermine = false;

        [Header("Publish")]
        [Range(1, 300)]
        [Tooltip("Maximum publishing frequency in Hz. Serves as an upper cap; actual rate is capped by native hardware display refresh rate.")]
        public int publishRateHz = 90;

        [Tooltip("If enabled, only publish topic data when at least one DDS subscriber/receiver is connected. Avoids useless transmissions when no receiver is listening.")]
        public bool publishOnlyWhenSubscribersMatched = true;

        [Tooltip("Interval in seconds to check for matched subscribers on active channels (default 0.5s)")]
        public float subscriberCheckInterval = 0.5f;

        [Tooltip("List of DdsInputActionChannel assets. Each asset represents a single InputAction stream.")]
        public List<DdsInputActionChannel> channels = new List<DdsInputActionChannel>();

        [Header("Logging Settings")]
        [Tooltip("Enable verbose debug logging for packet transmission")]
        public bool enableDebugLog = false;
        public DdsLogLevel logLevel = DdsLogLevel.Info;

        [NonSerialized] public bool isInitialized = false;
        [NonSerialized] public float measuredPublishRate = 0f;
        [NonSerialized] public long totalPublishedCount = 0;
        [NonSerialized] public int totalMatchedSubscribers = 0;
        [NonSerialized] public bool hasMatchedSubscribers = false;

        [Header("Network Auto-Discovery & Monitoring")]
        [Tooltip("Automatically poll self IP and scan subnet devices in the background every 5 seconds")]
        public bool autoPollNetwork = true;

        [Tooltip("Interval in seconds for background network polling (default 5.0s)")]
        public float networkPollInterval = 5f;

        [NonSerialized] public string currentSelfIpSummary = "";
        [NonSerialized] public List<string> discoveredIps = new List<string>();

        public event Action<string> OnSelfIpChanged;
        public event Action<List<string>> OnDiscoveredIpsChanged;

        #region Private Fields and Diagnostics

        private bool _isHooked = false;
        private long _lastPublishTicks = 0;
        private float _fpsTimer = 0f;
        private int _fpsCounter = 0;
        private float _subscriberCheckTimer = 0f;
        private CancellationTokenSource _netPollCts;
        private volatile string _pendingSelfIp = null;
        private List<string> _pendingDiscoveredIps = null;

        #endregion

        private void Awake()
        {
            if (Instance == null) Instance = this;
            SyncLogging();

            // Immediate synchronous frame-0 cache (<0.2ms) so UI shows IP instantly without waiting
            currentSelfIpSummary = DdsNetworkHelper.GetSelfIpSummary();
            discoveredIps = DdsNetworkHelper.GetActiveSubnetIps();
        }

        private void OnEnable()
        {
            if (channels != null)
            {
                foreach (var ch in channels)
                {
                    if (ch != null) ch.EnableActions();
                }
            }

            if (isInitialized && !_isHooked)
            {
                Application.onBeforeRender += OnBeforeRender;
                _isHooked = true;
            }

            if (autoPollNetwork)
            {
                StartNetworkPolling();
            }
        }

        private void OnDisable()
        {
            if (_isHooked)
            {
                Application.onBeforeRender -= OnBeforeRender;
                _isHooked = false;
            }

            StopNetworkPolling();

            if (channels != null)
            {
                foreach (var ch in channels)
                {
                    // Do not disable shared input actions
                }
            }
        }

        private void OnValidate()
        {
            SyncLogging();
#if UNITY_EDITOR
            if (channels == null || channels.Count == 0)
            {
                LoadDefaultEssentialChannels();
            }
#endif
        }

#if UNITY_EDITOR
        private void Reset()
        {
            LoadDefaultEssentialChannels();
        }

        /// <summary>
        /// Populates channels with the essential Head, Hand, and Button channels.
        /// </summary>
        [ContextMenu("Load Essential Channels (Head, Hands, Buttons)")]
        public void LoadDefaultEssentialChannels()
        {
            var defaultRelativePaths = new string[]
            {
                // Head Tracking (Pose + State)
                "Assets/CycloneDDS/Samples/Channels/XRIHead/XRI_Head_Position.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIHead/XRI_Head_Rotation.asset",

                // Left Controller Tracking (Pose, Aim)
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_Position.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_Rotation.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_Aim_Position.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_Aim_Rotation.asset",

                // Left Inputs (Thumbstick, Buttons, Triggers)
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_Thumbstick.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_Thumbstick_Click.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_ButtonX.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeft/XRI_Left_ButtonY.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeftInteraction/XRI_Left_Interaction_Activate_Value.asset",
                "Assets/CycloneDDS/Samples/Channels/XRILeftInteraction/XRI_Left_Interaction_Select_Value.asset",

                // Right Controller Tracking (Pose, Aim)
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_Position.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_Rotation.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_Aim_Position.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_Aim_Rotation.asset",

                // Right Inputs (Thumbstick, Buttons, Triggers)
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_Thumbstick.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_Thumbstick_Click.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_ButtonA.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRight/XRI_Right_ButtonB.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRightInteraction/XRI_Right_Interaction_Activate_Value.asset",
                "Assets/CycloneDDS/Samples/Channels/XRIRightInteraction/XRI_Right_Interaction_Select_Value.asset"
            };

            channels = new List<DdsInputActionChannel>();
            foreach (var p in defaultRelativePaths)
            {
                var ch = UnityEditor.AssetDatabase.LoadAssetAtPath<DdsInputActionChannel>(p);
                if (ch != null)
                {
                    channels.Add(ch);
                }
            }
        }
#endif

        public void SyncLogging()
        {
            DdsConfig.SetDebugLog(enableDebugLog);
            DdsConfig.SetLogLevel(logLevel);
        }

        private void Start()
        {
            InitializeDds();
        }

        public void InitializeDds()
        {
            if (isInitialized) return;

            SyncLogging();
            string config = DdsConfig.BuildCycloneDdsXml(targetIp, bindMatchingInterface, allowMulticast, autodetermine);
            int rc = CddsNative.cdds_init(domainId, config);
            if (rc != 0)
            {
                DdsLog.Error($"cdds_init failed: {rc}");
                return;
            }

            int initializedChannels = 0;
            if (channels != null)
            {
                foreach (var ch in channels)
                {
                    if (ch != null && ch.Initialize(domainId))
                    {
                        initializedChannels++;
                    }
                }
            }

            isInitialized = true;
            _lastPublishTicks = 0;

            if (!_isHooked)
            {
                Application.onBeforeRender += OnBeforeRender;
                _isHooked = true;
            }

            RefreshMatchedSubscribers();
            if (autoPollNetwork && _netPollCts == null)
            {
                StartNetworkPolling();
            }
            DdsLog.Info($"DDS initialized on Domain {domainId}, Target: {(string.IsNullOrWhiteSpace(targetIp) ? "Subnet Auto-Discovery" : targetIp)} with {initializedChannels} active channel(s). Rate Cap: {publishRateHz} Hz, OnlyWhenMatched: {publishOnlyWhenSubscribersMatched}");
        }

        /// <summary>
        /// Frame-aligned publishing callback triggered by Application.onBeforeRender.
        /// Samples OpenXR at the exact moment latest tracking poses are refreshed for the frame.
        /// Throttles to publishRateHz as an upper cap.
        /// </summary>
        private void OnBeforeRender()
        {
            if (!isInitialized) return;

            // Rate limiter: publishRateHz acts as an upper cap
            long ticksPerSec = Stopwatch.Frequency;
            int targetHz = Mathf.Clamp(publishRateHz, 1, 1000);
            long minIntervalTicks = (long)(ticksPerSec / (double)targetHz);
            long now = Stopwatch.GetTimestamp();

            // 50 microseconds tolerance to prevent skipping frames due to slight vsync jitter
            if (_lastPublishTicks > 0 && (now - _lastPublishTicks) < minIntervalTicks - (ticksPerSec / 20000))
            {
                return;
            }

            // Pause publishing if configured and no receiver is listening
            if (publishOnlyWhenSubscribersMatched && !hasMatchedSubscribers)
            {
                return;
            }

            _lastPublishTicks = now;
            long ts = DdsConfig.GetCurrentTimestampNs();

            if (channels != null)
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    var ch = channels[i];
                    if (ch != null && ch.isEnabled && ch.ddsHandle > 0)
                    {
                        ch.Publish(ts);
                        _fpsCounter++;
                        totalPublishedCount++;
                    }
                }
            }
        }

        private void Update()
        {
            // Update FPS diagnostics on main thread
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 1.0f)
            {
                measuredPublishRate = _fpsCounter / _fpsTimer;
                _fpsCounter = 0;
                _fpsTimer = 0f;
            }

            if (isInitialized)
            {
                _subscriberCheckTimer += Time.unscaledDeltaTime;
                if (_subscriberCheckTimer >= subscriberCheckInterval)
                {
                    _subscriberCheckTimer = 0f;
                    RefreshMatchedSubscribers();
                }
            }

            // Main-thread safe dispatch of network polling results to Unity UI
            if (_pendingSelfIp != null)
            {
                string ip = _pendingSelfIp;
                _pendingSelfIp = null;
                if (currentSelfIpSummary != ip)
                {
                    currentSelfIpSummary = ip;
                    OnSelfIpChanged?.Invoke(currentSelfIpSummary);
                }
            }

            if (_pendingDiscoveredIps != null)
            {
                var ips = _pendingDiscoveredIps;
                _pendingDiscoveredIps = null;
                if (!AreIpListsEqual(discoveredIps, ips))
                {
                    discoveredIps = new List<string>(ips);
                    OnDiscoveredIpsChanged?.Invoke(discoveredIps);
                }
            }
        }

        /// <summary>
        /// Queries DDS runtime for all currently matched subscribers across all active channels.
        /// </summary>
        public int RefreshMatchedSubscribers()
        {
            int count = 0;
            if (channels != null)
            {
                for (int i = 0; i < channels.Count; i++)
                {
                    var ch = channels[i];
                    if (ch != null && ch.isEnabled && ch.ddsHandle > 0)
                    {
                        int matched = CddsNative.cdds_get_matched_subscriptions(ch.ddsHandle);
                        ch.matchedSubscribers = matched;
                        count += matched;
                    }
                }
            }
            totalMatchedSubscribers = count;
            hasMatchedSubscribers = (count > 0);
            return count;
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        /// <summary>
        /// Switches target IP and restarts DDS if currently initialized.
        /// </summary>
        public void SwitchTargetIp(string newTargetIp)
        {
            string trimmed = newTargetIp?.Trim() ?? "";
            if (targetIp == trimmed && isInitialized) return;

            targetIp = trimmed;
            if (isInitialized)
            {
                Shutdown();
                InitializeDds();
            }
        }

        /// <summary>
        /// Explicitly starts or stops the DDS service.
        /// </summary>
        public void SetServiceRunning(bool run)
        {
            if (run)
            {
                if (!isInitialized) InitializeDds();
            }
            else
            {
                if (isInitialized) Shutdown();
            }
        }

        #region Background Network Polling (Zero Main-Thread Stall)

        public void StartNetworkPolling()
        {
            StopNetworkPolling();

            // Immediate fresh query on start/enable
            currentSelfIpSummary = DdsNetworkHelper.GetSelfIpSummary();
            discoveredIps = DdsNetworkHelper.GetActiveSubnetIps();
            OnSelfIpChanged?.Invoke(currentSelfIpSummary);
            OnDiscoveredIpsChanged?.Invoke(discoveredIps);

            _netPollCts = new CancellationTokenSource();
            _ = RunNetworkPollLoopAsync(_netPollCts.Token);
        }

        public void StopNetworkPolling()
        {
            _netPollCts?.Cancel();
            _netPollCts?.Dispose();
            _netPollCts = null;
        }

        public void ForceDiscoveredIps(List<string> ips)
        {
            discoveredIps = ips != null ? new List<string>(ips) : new List<string>();
            OnDiscoveredIpsChanged?.Invoke(discoveredIps);
        }

        private async Task RunNetworkPollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                int delayMs = Mathf.Max(1000, (int)(networkPollInterval * 1000f));
                try { await Task.Delay(delayMs, token); }
                catch (OperationCanceledException) { break; }

                if (token.IsCancellationRequested) break;

                try
                {
                    // 1. Off-thread: Retrieve self IP breakdown (WLAN vs ETH) in < 0.2ms
                    string selfIp = await Task.Run(() => DdsNetworkHelper.GetSelfIpSummary(), token);
                    if (token.IsCancellationRequested) break;
                    _pendingSelfIp = selfIp;

                    // 2. Off-thread: Retrieve subnet devices via OS ARP cache in < 0.2ms
                    var newIps = await Task.Run(() => DdsNetworkHelper.GetActiveSubnetIps(), token);
                    if (token.IsCancellationRequested) break;
                    _pendingDiscoveredIps = newIps;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    DdsLog.Warning($"Network polling warning: {ex.Message}");
                }
            }
        }

        private static bool AreIpListsEqual(List<string> a, List<string> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        #endregion

        public void Shutdown()
        {
            if (_isHooked)
            {
                Application.onBeforeRender -= OnBeforeRender;
                _isHooked = false;
            }

            StopNetworkPolling();

            if (!isInitialized) return;

            isInitialized = false;

            totalMatchedSubscribers = 0;
            hasMatchedSubscribers = false;
            measuredPublishRate = 0f;

            if (channels != null)
            {
                foreach (var ch in channels)
                {
                    if (ch != null)
                    {
                        ch.ddsHandle = -1;
                        ch.matchedSubscribers = 0;
                        // Do not disable shared input actions
                    }
                }
            }

            CddsNative.cdds_shutdown();
            DdsLog.Info("CycloneDDS Shutdown complete");
        }
    }
}
