using UnityEngine;
using UTXR.Devices;

namespace UTXR.UI
{
    /// <summary>
    /// Visual tracking state of an XR controller on the HUD.
    /// </summary>
    public enum TrackingVisualState
    {
        Untracked,
        Tracked,
        Warning
    }

    /// <summary>
    /// UI presentation controller for XR controller tracking states.
    /// Subscribes to XRDeviceEventDispatcher events to update Tracked, Untracked, and Warning panels.
    /// Buffers state transitions to prevent HUD flickering while ensuring immediate response when tracking recovers.
    /// </summary>
    [DisallowMultipleComponent]
    public class XRDeviceTrackingUIController : MonoBehaviour
    {
        [Header("Left Hand Tracking UI")]
        [SerializeField] private GameObject leftTrackedUI;
        [SerializeField] private GameObject leftUntrackedUI;
        [SerializeField] private GameObject leftWarningUI;

        [Header("Right Hand Tracking UI")]
        [SerializeField] private GameObject rightTrackedUI;
        [SerializeField] private GameObject rightUntrackedUI;
        [SerializeField] private GameObject rightWarningUI;

        [Header("Event Source (Auto-resolved if unassigned)")]
        [Tooltip("Dispatcher providing tracking state change events")]
        [SerializeField] private XRDeviceEventDispatcher eventDispatcher;
        [Tooltip("Device monitor providing initial state queries")]
        [SerializeField] private XRGeneralDeviceStateMonitor stateMonitor;

        [Header("Buffering & Delay Settings")]
        [Tooltip("Buffer delay (in seconds) before switching from Tracked to Untracked to prevent micro-flickering from transient 1-frame tracking drops.")]
        [SerializeField] private float untrackedBufferSeconds = 0.15f;

        [Tooltip("Hold duration (in seconds) for Warning UI so the user has sufficient time to clearly see it.")]
        [SerializeField] private float warningHoldDurationSeconds = 2.0f;

        [Tooltip("Minimum hold duration (in seconds) for Untracked UI once displayed to avoid rapid vanishing.")]
        [SerializeField] private float untrackedMinHoldSeconds = 0.5f;

        // Left hand tracking states & timers
        private bool _leftTargetTracked = false;
        private bool _leftTargetWarning = false;
        private TrackingVisualState _leftVisualState = TrackingVisualState.Untracked;
        private float _leftWarningTimer = 0f;
        private float _leftUntrackedBufferTimer = 0f;
        private float _leftUntrackedHoldTimer = 0f;
        private bool _leftPendingUntracked = false;

        // Right hand tracking states & timers
        private bool _rightTargetTracked = false;
        private bool _rightTargetWarning = false;
        private TrackingVisualState _rightVisualState = TrackingVisualState.Untracked;
        private float _rightWarningTimer = 0f;
        private float _rightUntrackedBufferTimer = 0f;
        private float _rightUntrackedHoldTimer = 0f;
        private bool _rightPendingUntracked = false;

        #region Public Properties

        public bool HasReferences => leftTrackedUI != null || rightTrackedUI != null ||
                                      leftUntrackedUI != null || rightUntrackedUI != null ||
                                      leftWarningUI != null || rightWarningUI != null;

        public GameObject LeftTrackedUI => leftTrackedUI;
        public GameObject LeftUntrackedUI => leftUntrackedUI;
        public GameObject LeftWarningUI => leftWarningUI;
        public GameObject RightTrackedUI => rightTrackedUI;
        public GameObject RightUntrackedUI => rightUntrackedUI;
        public GameObject RightWarningUI => rightWarningUI;

        public TrackingVisualState LeftVisualState => _leftVisualState;
        public TrackingVisualState RightVisualState => _rightVisualState;

        #endregion

        #region Unity Lifecycle & Event Subscriptions

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();

            if (eventDispatcher != null)
            {
                eventDispatcher.OnLeftTrackingStateChanged += HandleLeftStateReceived;
                eventDispatcher.OnRightTrackingStateChanged += HandleRightStateReceived;
            }

            // Sync with current monitor state if available
            if (stateMonitor != null)
            {
                HandleLeftStateReceived(stateMonitor.IsLeftTracked, stateMonitor.IsLeftWarning);
                HandleRightStateReceived(stateMonitor.IsRightTracked, stateMonitor.IsRightWarning);
            }
            else
            {
                ResetUI();
            }
        }

        private void OnDisable()
        {
            if (eventDispatcher != null)
            {
                eventDispatcher.OnLeftTrackingStateChanged -= HandleLeftStateReceived;
                eventDispatcher.OnRightTrackingStateChanged -= HandleRightStateReceived;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            // --- Left hand timer updates ---
            if (_leftPendingUntracked)
            {
                _leftUntrackedBufferTimer -= dt;
                if (_leftUntrackedBufferTimer <= 0f)
                {
                    _leftPendingUntracked = false;
                    if (!_leftTargetTracked && !_leftTargetWarning)
                    {
                        _leftUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyLeftVisualState(TrackingVisualState.Untracked);
                    }
                }
            }

            if (_leftVisualState == TrackingVisualState.Warning)
            {
                if (_leftWarningTimer > 0f)
                {
                    _leftWarningTimer -= dt;
                }

                if (_leftWarningTimer <= 0f && !_leftTargetWarning)
                {
                    if (_leftTargetTracked)
                    {
                        ApplyLeftVisualState(TrackingVisualState.Tracked);
                    }
                    else
                    {
                        _leftUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyLeftVisualState(TrackingVisualState.Untracked);
                    }
                }
            }

            if (_leftVisualState == TrackingVisualState.Untracked && _leftUntrackedHoldTimer > 0f)
            {
                _leftUntrackedHoldTimer -= dt;
            }

            // --- Right hand timer updates ---
            if (_rightPendingUntracked)
            {
                _rightUntrackedBufferTimer -= dt;
                if (_rightUntrackedBufferTimer <= 0f)
                {
                    _rightPendingUntracked = false;
                    if (!_rightTargetTracked && !_rightTargetWarning)
                    {
                        _rightUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyRightVisualState(TrackingVisualState.Untracked);
                    }
                }
            }

            if (_rightVisualState == TrackingVisualState.Warning)
            {
                if (_rightWarningTimer > 0f)
                {
                    _rightWarningTimer -= dt;
                }

                if (_rightWarningTimer <= 0f && !_rightTargetWarning)
                {
                    if (_rightTargetTracked)
                    {
                        ApplyRightVisualState(TrackingVisualState.Tracked);
                    }
                    else
                    {
                        _rightUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyRightVisualState(TrackingVisualState.Untracked);
                    }
                }
            }

            if (_rightVisualState == TrackingVisualState.Untracked && _rightUntrackedHoldTimer > 0f)
            {
                _rightUntrackedHoldTimer -= dt;
            }
        }

        private void ResolveDependencies()
        {
            if (eventDispatcher == null && stateMonitor != null)
            {
                eventDispatcher = stateMonitor.EventDispatcher;
            }
            if (eventDispatcher == null)
            {
                eventDispatcher = GetComponent<XRDeviceEventDispatcher>();
                if (eventDispatcher == null)
                {
                    eventDispatcher = FindFirstObjectByType<XRDeviceEventDispatcher>();
                }
            }
            if (stateMonitor == null)
            {
                stateMonitor = GetComponent<XRGeneralDeviceStateMonitor>();
                if (stateMonitor == null)
                {
                    stateMonitor = FindFirstObjectByType<XRGeneralDeviceStateMonitor>();
                }
            }
        }

        #endregion

        #region Event Handlers & State Processing

        private void HandleLeftStateReceived(bool isTracked, bool isWarning)
        {
            _leftTargetTracked = isTracked;
            _leftTargetWarning = isWarning;

            // 1. 恢复追踪立马响应 (Tracking recovered: immediate response, zero lag)
            if (isTracked && !isWarning)
            {
                _leftPendingUntracked = false;
                _leftUntrackedBufferTimer = 0f;
                _leftWarningTimer = 0f;
                _leftUntrackedHoldTimer = 0f;
                ApplyLeftVisualState(TrackingVisualState.Tracked);
                return;
            }

            // 2. 警告状态：进入延迟期并保持显示 (Warning: hold duration to ensure user sees it)
            if (isWarning)
            {
                _leftPendingUntracked = false;
                _leftUntrackedBufferTimer = 0f;
                _leftWarningTimer = warningHoldDurationSeconds;
                ApplyLeftVisualState(TrackingVisualState.Warning);
                return;
            }

            // 3. 丢失追踪状态：缓冲期防闪烁 (Untracked: buffer delay to prevent 1-frame flutter)
            if (!isTracked)
            {
                if (_leftVisualState == TrackingVisualState.Tracked)
                {
                    if (untrackedBufferSeconds > 0f)
                    {
                        _leftPendingUntracked = true;
                        _leftUntrackedBufferTimer = untrackedBufferSeconds;
                    }
                    else
                    {
                        _leftUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyLeftVisualState(TrackingVisualState.Untracked);
                    }
                }
                else if (_leftVisualState == TrackingVisualState.Warning)
                {
                    if (_leftWarningTimer <= 0f)
                    {
                        _leftUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyLeftVisualState(TrackingVisualState.Untracked);
                    }
                }
                else
                {
                    ApplyLeftVisualState(TrackingVisualState.Untracked);
                }
            }
        }

        private void HandleRightStateReceived(bool isTracked, bool isWarning)
        {
            _rightTargetTracked = isTracked;
            _rightTargetWarning = isWarning;

            // 1. 恢复追踪立马响应 (Tracking recovered: immediate response, zero lag)
            if (isTracked && !isWarning)
            {
                _rightPendingUntracked = false;
                _rightUntrackedBufferTimer = 0f;
                _rightWarningTimer = 0f;
                _rightUntrackedHoldTimer = 0f;
                ApplyRightVisualState(TrackingVisualState.Tracked);
                return;
            }

            // 2. 警告状态：进入延迟期并保持显示 (Warning: hold duration to ensure user sees it)
            if (isWarning)
            {
                _rightPendingUntracked = false;
                _rightUntrackedBufferTimer = 0f;
                _rightWarningTimer = warningHoldDurationSeconds;
                ApplyRightVisualState(TrackingVisualState.Warning);
                return;
            }

            // 3. 丢失追踪状态：缓冲期防闪烁 (Untracked: buffer delay to prevent 1-frame flutter)
            if (!isTracked)
            {
                if (_rightVisualState == TrackingVisualState.Tracked)
                {
                    if (untrackedBufferSeconds > 0f)
                    {
                        _rightPendingUntracked = true;
                        _rightUntrackedBufferTimer = untrackedBufferSeconds;
                    }
                    else
                    {
                        _rightUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyRightVisualState(TrackingVisualState.Untracked);
                    }
                }
                else if (_rightVisualState == TrackingVisualState.Warning)
                {
                    if (_rightWarningTimer <= 0f)
                    {
                        _rightUntrackedHoldTimer = untrackedMinHoldSeconds;
                        ApplyRightVisualState(TrackingVisualState.Untracked);
                    }
                }
                else
                {
                    ApplyRightVisualState(TrackingVisualState.Untracked);
                }
            }
        }

        #endregion

        #region Visual Presentation with Dirty-Checking

        private void ApplyLeftVisualState(TrackingVisualState state, bool force = false)
        {
            if (!force && _leftVisualState == state) return;
            _leftVisualState = state;

            SetUIActive(leftTrackedUI, state == TrackingVisualState.Tracked);
            SetUIActive(leftUntrackedUI, state == TrackingVisualState.Untracked);
            SetUIActive(leftWarningUI, state == TrackingVisualState.Warning);
        }

        private void ApplyRightVisualState(TrackingVisualState state, bool force = false)
        {
            if (!force && _rightVisualState == state) return;
            _rightVisualState = state;

            SetUIActive(rightTrackedUI, state == TrackingVisualState.Tracked);
            SetUIActive(rightUntrackedUI, state == TrackingVisualState.Untracked);
            SetUIActive(rightWarningUI, state == TrackingVisualState.Warning);
        }

        private static void SetUIActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        #endregion

        #region Public Control & Configuration APIs

        /// <summary>
        /// Explicitly updates left hand UI (can be called manually or for testing).
        /// </summary>
        public void UpdateLeftUI(bool isTracked, bool isWarning, bool force = false)
        {
            if (force)
            {
                _leftTargetTracked = isTracked;
                _leftTargetWarning = isWarning;
                _leftPendingUntracked = false;
                _leftWarningTimer = isWarning ? warningHoldDurationSeconds : 0f;
                _leftUntrackedBufferTimer = 0f;
                _leftUntrackedHoldTimer = 0f;
                ApplyLeftVisualState(isWarning ? TrackingVisualState.Warning : (isTracked ? TrackingVisualState.Tracked : TrackingVisualState.Untracked), true);
                return;
            }

            HandleLeftStateReceived(isTracked, isWarning);
        }

        /// <summary>
        /// Explicitly updates right hand UI (can be called manually or for testing).
        /// </summary>
        public void UpdateRightUI(bool isTracked, bool isWarning, bool force = false)
        {
            if (force)
            {
                _rightTargetTracked = isTracked;
                _rightTargetWarning = isWarning;
                _rightPendingUntracked = false;
                _rightWarningTimer = isWarning ? warningHoldDurationSeconds : 0f;
                _rightUntrackedBufferTimer = 0f;
                _rightUntrackedHoldTimer = 0f;
                ApplyRightVisualState(isWarning ? TrackingVisualState.Warning : (isTracked ? TrackingVisualState.Tracked : TrackingVisualState.Untracked), true);
                return;
            }

            HandleRightStateReceived(isTracked, isWarning);
        }

        /// <summary>
        /// Resets both left and right UI panels to untracked state immediately.
        /// </summary>
        public void ResetUI()
        {
            UpdateLeftUI(false, false, true);
            UpdateRightUI(false, false, true);
        }

        /// <summary>
        /// Explicitly configures UI references at runtime if needed.
        /// </summary>
        public void SetupReferences(
            GameObject lTracked, GameObject lUntracked, GameObject lWarn,
            GameObject rTracked, GameObject rUntracked, GameObject rWarn)
        {
            leftTrackedUI = lTracked;
            leftUntrackedUI = lUntracked;
            leftWarningUI = lWarn;
            rightTrackedUI = rTracked;
            rightUntrackedUI = rUntracked;
            rightWarningUI = rWarn;

            ResetUI();
        }

        #endregion
    }
}
