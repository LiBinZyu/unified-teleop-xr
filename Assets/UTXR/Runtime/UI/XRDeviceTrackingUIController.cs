using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using UTXR.Devices;

namespace UTXR.UI
{
    /// <summary>
    /// Visual tracking state for either input mode.
    /// </summary>
    public enum TrackingVisualState
    {
        Untracked,
        Tracked,
        Warning
    }

    /// <summary>
    /// UI presentation for the selected Controller/Hand provider through IXRDeviceState.
    /// Buffers state transitions to prevent HUD flickering while ensuring immediate response when tracking recovers.
    /// Updates individual state indicators; group visibility belongs to the scene's Inspector bindings.
    /// </summary>
    [DisallowMultipleComponent]
    public class XRDeviceTrackingUIController : MonoBehaviour
    {
        [Header("Left Controller Tracking UI")]
        [FormerlySerializedAs("leftTrackedUI")]
        [SerializeField] private GameObject leftControllerTrackedUI;
        [FormerlySerializedAs("leftUntrackedUI")]
        [SerializeField] private GameObject leftControllerUntrackedUI;
        [FormerlySerializedAs("leftWarningUI")]
        [SerializeField] private GameObject leftControllerWarningUI;

        [Header("Right Controller Tracking UI")]
        [FormerlySerializedAs("rightTrackedUI")]
        [SerializeField] private GameObject rightControllerTrackedUI;
        [FormerlySerializedAs("rightUntrackedUI")]
        [SerializeField] private GameObject rightControllerUntrackedUI;
        [FormerlySerializedAs("rightWarningUI")]
        [SerializeField] private GameObject rightControllerWarningUI;

        [Header("Left Hand Tracking UI")]
        [SerializeField] private GameObject leftHandTrackedUI;
        [SerializeField] private GameObject leftHandUntrackedUI;
        [SerializeField] private GameObject leftHandWarningUI;

        [Header("Right Hand Tracking UI")]
        [SerializeField] private GameObject rightHandTrackedUI;
        [SerializeField] private GameObject rightHandUntrackedUI;
        [SerializeField] private GameObject rightHandWarningUI;

        [Header("Input Mode Switch")]
        [Tooltip("On = Hand, Off = Controller. Controls input mode only; bind group visibility in the Inspector.")]
        [SerializeField] private Toggle modeSwitchToggle;

        [Header("Explicit Event Source")]
        [Tooltip("Device monitor providing initial state queries")]
        [SerializeField] private XRGeneralDeviceStateMonitor stateMonitor;
        private IXRDeviceState _deviceState;
        private XRInputMode _displayMode;

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

        public bool HasReferences => leftControllerTrackedUI != null || rightControllerTrackedUI != null ||
                                      leftControllerUntrackedUI != null || rightControllerUntrackedUI != null ||
                                      leftControllerWarningUI != null || rightControllerWarningUI != null ||
                                      leftHandTrackedUI != null || leftHandUntrackedUI != null || leftHandWarningUI != null ||
                                      rightHandTrackedUI != null || rightHandUntrackedUI != null || rightHandWarningUI != null;

        public GameObject LeftControllerTrackedUI => leftControllerTrackedUI;
        public GameObject LeftControllerUntrackedUI => leftControllerUntrackedUI;
        public GameObject LeftControllerWarningUI => leftControllerWarningUI;
        public GameObject RightControllerTrackedUI => rightControllerTrackedUI;
        public GameObject RightControllerUntrackedUI => rightControllerUntrackedUI;
        public GameObject RightControllerWarningUI => rightControllerWarningUI;
        public GameObject LeftHandTrackedUI => leftHandTrackedUI;
        public GameObject LeftHandUntrackedUI => leftHandUntrackedUI;
        public GameObject LeftHandWarningUI => leftHandWarningUI;
        public GameObject RightHandTrackedUI => rightHandTrackedUI;
        public GameObject RightHandUntrackedUI => rightHandUntrackedUI;
        public GameObject RightHandWarningUI => rightHandWarningUI;

        public TrackingVisualState LeftVisualState => _leftVisualState;
        public TrackingVisualState RightVisualState => _rightVisualState;

        #endregion

        #region Unity Lifecycle & Event Subscriptions

        private void OnEnable()
        {
            if (!ResolveDependencies()) return;
            _deviceState.ModeChanged += HandleModeChanged;
            _deviceState.TrackingChanged += HandleDeviceStateChanged;
            modeSwitchToggle.onValueChanged.AddListener(SetHandMode);
            SynchronizeUI();
        }

        private void OnDisable()
        {
            if (_deviceState != null)
            {
                _deviceState.ModeChanged -= HandleModeChanged;
                _deviceState.TrackingChanged -= HandleDeviceStateChanged;
            }
            if (modeSwitchToggle != null) modeSwitchToggle.onValueChanged.RemoveListener(SetHandMode);
            _deviceState = null;
            HideAllUI();
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

        private bool ResolveDependencies()
        {
            try
            {
                if (stateMonitor == null) throw new InvalidOperationException("Assign State Monitor explicitly.");
                if (modeSwitchToggle == null) throw new InvalidOperationException("Assign Mode Switch Toggle explicitly.");
                if (modeSwitchToggle.group != null) throw new InvalidOperationException("Mode Switch Toggle must not belong to a ToggleGroup.");
                ValidateUIReferences();
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogError($"[XRDeviceTrackingUI] {exception.Message}", this);
                HideAllUI();
                enabled = false;
                return false;
            }
            _deviceState = stateMonitor;
            return true;
        }

        #endregion

        #region Event Handlers & State Processing

        private void HandleModeChanged(XRInputMode mode)
        {
            SynchronizeUI();
        }

        private void SynchronizeUI()
        {
            _displayMode = _deviceState.Mode;
            ToggleImageHelper.SetIsOnWithoutNotify(modeSwitchToggle, _displayMode == XRInputMode.Hand);
            HideAllUI();
            var left = _deviceState.GetState(XRSide.Left);
            var right = _deviceState.GetState(XRSide.Right);
            UpdateLeftUI(left.Status.IsReady && left.IsTracked, left.Status.IsReady && left.IsWarning, true);
            UpdateRightUI(right.Status.IsReady && right.IsTracked, right.Status.IsReady && right.IsWarning, true);
        }

        private void HandleDeviceStateChanged(XRSide side, XRDeviceState state)
        {
            if (side == XRSide.Left) HandleLeftStateReceived(state.IsTracked, state.IsWarning);
            else HandleRightStateReceived(state.IsTracked, state.IsWarning);
        }

        private void HandleLeftStateReceived(bool isTracked, bool isWarning)
        {
            if (_deviceState != null && !_deviceState.GetState(XRSide.Left).Status.IsReady)
            {
                UpdateLeftUI(false, false, true);
                return;
            }
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
            if (_deviceState != null && !_deviceState.GetState(XRSide.Right).Status.IsReady)
            {
                UpdateRightUI(false, false, true);
                return;
            }
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

            bool controller = _displayMode == XRInputMode.Controller;
            SetUIActive(leftControllerTrackedUI, controller && state == TrackingVisualState.Tracked);
            SetUIActive(leftControllerUntrackedUI, controller && state == TrackingVisualState.Untracked);
            SetUIActive(leftControllerWarningUI, controller && state == TrackingVisualState.Warning);
            SetUIActive(leftHandTrackedUI, !controller && state == TrackingVisualState.Tracked);
            SetUIActive(leftHandUntrackedUI, !controller && state == TrackingVisualState.Untracked);
            SetUIActive(leftHandWarningUI, !controller && state == TrackingVisualState.Warning);
        }

        private void ApplyRightVisualState(TrackingVisualState state, bool force = false)
        {
            if (!force && _rightVisualState == state) return;
            _rightVisualState = state;

            bool controller = _displayMode == XRInputMode.Controller;
            SetUIActive(rightControllerTrackedUI, controller && state == TrackingVisualState.Tracked);
            SetUIActive(rightControllerUntrackedUI, controller && state == TrackingVisualState.Untracked);
            SetUIActive(rightControllerWarningUI, controller && state == TrackingVisualState.Warning);
            SetUIActive(rightHandTrackedUI, !controller && state == TrackingVisualState.Tracked);
            SetUIActive(rightHandUntrackedUI, !controller && state == TrackingVisualState.Untracked);
            SetUIActive(rightHandWarningUI, !controller && state == TrackingVisualState.Warning);
        }

        private static void SetUIActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private GameObject[] GetUIObjects() => new[]
        {
            leftControllerTrackedUI, leftControllerUntrackedUI, leftControllerWarningUI,
            rightControllerTrackedUI, rightControllerUntrackedUI, rightControllerWarningUI,
            leftHandTrackedUI, leftHandUntrackedUI, leftHandWarningUI,
            rightHandTrackedUI, rightHandUntrackedUI, rightHandWarningUI
        };

        private void HideAllUI()
        {
            foreach (var panel in GetUIObjects()) SetUIActive(panel, false);
        }

        private void ValidateUIReferences()
        {
            var panels = GetUIObjects();
            for (int i = 0; i < panels.Length; i++)
            {
                var panel = panels[i];
                if (panel == null)
                    throw new InvalidOperationException("Assign all Controller and Hand tracking UI references (Tracked, Untracked, Warning for each side).");
                if (transform.IsChildOf(panel.transform) || modeSwitchToggle.transform.IsChildOf(panel.transform))
                    throw new InvalidOperationException("State indicators must not contain this component or the mode toggle. Assign individual state objects, not group roots.");
                for (int j = 0; j < i; j++)
                    if (panel.transform.IsChildOf(panels[j].transform) || panels[j].transform.IsChildOf(panel.transform))
                        throw new InvalidOperationException("State indicator references must be distinct, non-overlapping objects.");
            }
        }

        #endregion

        #region Public Control & Configuration APIs

        /// <summary>True selects Hand; false selects Controller. Availability never changes the selection.</summary>
        public void SetHandMode(bool useHands)
        {
            if (_deviceState == null)
                throw new InvalidOperationException("Enable and configure the tracking UI before switching modes.");
            try { _deviceState.SetMode(useHands ? XRInputMode.Hand : XRInputMode.Controller); }
            finally { SynchronizeUI(); }
        }

        /// <summary>
        /// Updates the left indicator for the selected mode.
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
        /// Updates the right indicator for the selected mode.
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
        /// Configures controller UI references explicitly.
        /// </summary>
        public void SetupControllerReferences(
            GameObject lTracked, GameObject lUntracked, GameObject lWarn,
            GameObject rTracked, GameObject rUntracked, GameObject rWarn)
        {
            leftControllerTrackedUI = lTracked;
            leftControllerUntrackedUI = lUntracked;
            leftControllerWarningUI = lWarn;
            rightControllerTrackedUI = rTracked;
            rightControllerUntrackedUI = rUntracked;
            rightControllerWarningUI = rWarn;

            if (_deviceState != null) SynchronizeUI();
        }

        public void SetupHandReferences(
            GameObject lTracked, GameObject lUntracked, GameObject lWarn,
            GameObject rTracked, GameObject rUntracked, GameObject rWarn)
        {
            leftHandTrackedUI = lTracked;
            leftHandUntrackedUI = lUntracked;
            leftHandWarningUI = lWarn;
            rightHandTrackedUI = rTracked;
            rightHandUntrackedUI = rUntracked;
            rightHandWarningUI = rWarn;

            if (_deviceState != null) SynchronizeUI();
        }

        #endregion
    }
}
