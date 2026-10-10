using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UTXR.Core;

namespace UTXR.Devices
{
    /// <summary>
    /// General XR device state monitor.
    /// Orchestrates controller lifecycle and hardware action bindings.
    /// Core state evaluation is delegated to XRDeviceStateCore,
    /// and event dispatching is delegated to XRDeviceEventDispatcher.
    /// </summary>
    public class XRGeneralDeviceStateMonitor : MonoBehaviour
    {
        [Header("Modular Controllers")]
        [Tooltip("Dispatcher responsible for status changes and gesture events")]
        [SerializeField] private XRDeviceEventDispatcher eventDispatcher;

        [Header("Left Input Actions")]
        public InputActionProperty leftIsTrackedAction;
        public InputActionProperty leftTrackingStateAction;
        public InputActionProperty leftLinearVelocityAction;
        public InputActionProperty leftAngularVelocityAction;
        public InputActionProperty leftLinearAccelerationAction;
        public InputActionProperty leftAngularAccelerationAction;

        [Header("Right Input Actions")]
        public InputActionProperty rightIsTrackedAction;
        public InputActionProperty rightTrackingStateAction;
        public InputActionProperty rightLinearVelocityAction;
        public InputActionProperty rightAngularVelocityAction;
        public InputActionProperty rightLinearAccelerationAction;
        public InputActionProperty rightAngularAccelerationAction;

        [Tooltip("Optional: specify XRAccelerationDeviceBridge to read acceleration. Automatically uses singleton or controls if null.")]
        public XRAccelerationDeviceBridge accelerationBridge;

        [Header("Filtering Settings")]
        public FilterMethod filterMethod = FilterMethod.None;
        [Tooltip("Require 6DoF (Position + Rotation) tracking state. If false, 3DoF rotation-only is accepted.")]
        [SerializeField] private bool require6DoF = true;
        [SerializeField] private int frameTolerance = 20;
        [SerializeField] private float lostToleranceSeconds = 0.1f;
        [SerializeField] private float maxHumanSpeed = 8.0f;          // Upper bound for linear speed sanity check
        [SerializeField] private float minLinVelForTracking = 0.001f; // Minimum linear speed threshold
        [SerializeField] private float posJumpMinDelta = 0.015f;      // 1.5cm position jump threshold
        [SerializeField] private float posJumpMaxImpliedSpeed = 4.0f; // Implied speed threshold (m/s)
        [SerializeField] private float warningMinDurationSeconds = 2.0f;
        [SerializeField] private float warnCooldownSeconds = 1.0f;

        [Header("Logging Settings")]
        [Tooltip("Print tracking logs to Unity Console")]
        public bool enableDebugLog = true;
        [Tooltip("Write tracking logs to disk file via Logger.LogApp")]
        public bool enableFileLog = false;
        [Tooltip("Periodic continuous logging interval in seconds (0 = log only on state change)")]
        [SerializeField] private float logIntervalSeconds = 0.5f;

        [Header("Shake Detection Settings")]
        [Tooltip("Minimum acceleration (m/s²) required to trigger a swing")]
        [SerializeField] private float shakeAccelThreshold = 200.0f;
        [Tooltip("Minimum time (seconds) between swings")]
        [SerializeField] private float swingCooldownDuration = 0.15f;
        [Tooltip("Maximum window time (seconds) to complete 3 swings")]
        [SerializeField] private float maxComboInterval = 1.0f;

        private XRController _leftDeviceCache;
        private XRController _rightDeviceCache;
        private float _lastDeviceSearchTime = 0f;

        private bool _leftWasTracked = false;
        private bool _rightWasTracked = false;
        private bool _leftWasWarning = false;
        private bool _rightWasWarning = false;
        private bool _isInitialized = false;

        private bool _leftWasOK = false;
        private bool _rightWasOK = false;
        private float _lastWarnTime = -999f;

        private ControllerTrackingState _leftState;
        private ControllerTrackingState _rightState;
        private HandLogCache _leftLogCache;
        private HandLogCache _rightLogCache;

        private bool _leftDiagDone = false;
        private bool _rightDiagDone = false;

        #region Public Properties & Query APIs

        public XRDeviceEventDispatcher EventDispatcher
        {
            get
            {
                if (eventDispatcher == null)
                {
                    eventDispatcher = GetComponent<XRDeviceEventDispatcher>();
                    if (eventDispatcher == null)
                    {
                        eventDispatcher = gameObject.AddComponent<XRDeviceEventDispatcher>();
                    }
                }
                return eventDispatcher;
            }
        }

        public bool IsLeftTracked => _leftWasTracked;
        public bool IsRightTracked => _rightWasTracked;
        public bool IsLeftWarning => _leftWasWarning;
        public bool IsRightWarning => _rightWasWarning;

        public int GetRawLeftTrackingState() => XRDeviceStateCore.GetRawTrackingState(leftTrackingStateAction, _leftDeviceCache);
        public int GetRawRightTrackingState() => XRDeviceStateCore.GetRawTrackingState(rightTrackingStateAction, _rightDeviceCache);

        public int GetLeftTrackingState() => !IsLeftTracked ? 0 : GetRawLeftTrackingState();
        public int GetRightTrackingState() => !IsRightTracked ? 0 : GetRawRightTrackingState();

        public Vector3 GetLinearVelocity(bool isLeft, XRController device = null)
            => XRDeviceStateCore.GetLinearVelocity(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache));

        public Vector3 GetAngularVelocity(bool isLeft, XRController device = null)
            => XRDeviceStateCore.GetAngularVelocity(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache));

        public Vector3 GetLinearAcceleration(bool isLeft, XRController device = null)
            => XRDeviceStateCore.GetLinearAcceleration(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache), accelerationBridge, isLeft);

        public Vector3 GetAngularAcceleration(bool isLeft, XRController device = null)
            => XRDeviceStateCore.GetAngularAcceleration(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache), accelerationBridge, isLeft);

        #endregion

        #region Event Properties (Backward Compatibility)

        public UnityEvent OnLeftTracked => eventDispatcher != null ? eventDispatcher.OnLeftTracked : null;
        public UnityEvent OnLeftUntracked => eventDispatcher != null ? eventDispatcher.OnLeftUntracked : null;
        public UnityEvent OnRightTracked => eventDispatcher != null ? eventDispatcher.OnRightTracked : null;
        public UnityEvent OnRightUntracked => eventDispatcher != null ? eventDispatcher.OnRightUntracked : null;
        public UnityEvent OnControllerTrackWarn => eventDispatcher != null ? eventDispatcher.OnControllerTrackWarn : null;
        public UnityEvent OnLeftSwing => eventDispatcher != null ? eventDispatcher.OnLeftSwing : null;
        public UnityEvent OnRightSwing => eventDispatcher != null ? eventDispatcher.OnRightSwing : null;
        public UnityEvent OnLeftTripleSwing => eventDispatcher != null ? eventDispatcher.OnLeftTripleSwing : null;
        public UnityEvent OnRightTripleSwing => eventDispatcher != null ? eventDispatcher.OnRightTripleSwing : null;
        public UnityEvent OnAnyShake => eventDispatcher != null ? eventDispatcher.OnAnyShake : null;

        #endregion

        private void Awake()
        {
            EnsureDependencies();
        }

        private void EnsureDependencies()
        {
            if (eventDispatcher == null)
            {
                eventDispatcher = GetComponent<XRDeviceEventDispatcher>();
                if (eventDispatcher == null)
                {
                    eventDispatcher = gameObject.AddComponent<XRDeviceEventDispatcher>();
                }
            }
        }

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            XRDeviceStateCore.FindControllers(out _leftDeviceCache, out _rightDeviceCache);

            EnableActionProperty(leftIsTrackedAction);
            EnableActionProperty(rightIsTrackedAction);
            EnableActionProperty(leftTrackingStateAction);
            EnableActionProperty(rightTrackingStateAction);
            EnableActionProperty(leftLinearVelocityAction);
            EnableActionProperty(rightLinearVelocityAction);
            EnableActionProperty(leftAngularVelocityAction);
            EnableActionProperty(rightAngularVelocityAction);
            EnableActionProperty(leftLinearAccelerationAction);
            EnableActionProperty(rightLinearAccelerationAction);
            EnableActionProperty(leftAngularAccelerationAction);
            EnableActionProperty(rightAngularAccelerationAction);
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;

            DisableActionProperty(leftIsTrackedAction);
            DisableActionProperty(rightIsTrackedAction);
            DisableActionProperty(leftTrackingStateAction);
            DisableActionProperty(rightTrackingStateAction);
            DisableActionProperty(leftLinearVelocityAction);
            DisableActionProperty(rightLinearVelocityAction);
            DisableActionProperty(leftAngularVelocityAction);
            DisableActionProperty(rightAngularVelocityAction);
            DisableActionProperty(leftLinearAccelerationAction);
            DisableActionProperty(rightLinearAccelerationAction);
            DisableActionProperty(leftAngularAccelerationAction);
            DisableActionProperty(rightAngularAccelerationAction);
        }

        private void Start()
        {
            _leftState.Reset();
            _rightState.Reset();
        }

        private void Update()
        {
            // Controller reconnect & scan
            if (_leftDeviceCache == null || !_leftDeviceCache.added || _rightDeviceCache == null || !_rightDeviceCache.added)
            {
                if (Time.unscaledTime - _lastDeviceSearchTime > 1.0f)
                {
                    _lastDeviceSearchTime = Time.unscaledTime;
                    XRDeviceStateCore.FindControllers(out _leftDeviceCache, out _rightDeviceCache);
                }
            }

            // Input binding diagnostics on first connection
            DiagnoseActionsOnce();

            // 1. Core tracking evaluation via XRDeviceStateCore
            bool isLeftCurrentlyTracked = XRDeviceStateCore.EvaluateControllerState(
                "[L]", _leftDeviceCache, leftIsTrackedAction, leftTrackingStateAction,
                filterMethod, require6DoF, frameTolerance, lostToleranceSeconds,
                maxHumanSpeed, minLinVelForTracking, posJumpMinDelta, posJumpMaxImpliedSpeed,
                enableDebugLog || enableFileLog, ref _leftState);

            bool isRightCurrentlyTracked = XRDeviceStateCore.EvaluateControllerState(
                "[R]", _rightDeviceCache, rightIsTrackedAction, rightTrackingStateAction,
                filterMethod, require6DoF, frameTolerance, lostToleranceSeconds,
                maxHumanSpeed, minLinVelForTracking, posJumpMinDelta, posJumpMaxImpliedSpeed,
                enableDebugLog || enableFileLog, ref _rightState);

            _leftState.isTrackedUIState = isLeftCurrentlyTracked;
            _rightState.isTrackedUIState = isRightCurrentlyTracked;

            float dt = Time.deltaTime;
            int rawLeftState = GetRawLeftTrackingState();
            int rawRightState = GetRawRightTrackingState();

            // 2. Swing & combo detection: calculated by XRDeviceStateCore, dispatched by eventDispatcher
            ProcessShakeDetection(true, _leftDeviceCache, rawLeftState);
            ProcessShakeDetection(false, _rightDeviceCache, rawRightState);

            // 3. Warning evaluation
            bool isLeftWarning = XRDeviceStateCore.ProcessWarningTimer(_leftDeviceCache, rawLeftState, isLeftCurrentlyTracked, warningMinDurationSeconds, dt, ref _leftState);
            bool isRightWarning = XRDeviceStateCore.ProcessWarningTimer(_rightDeviceCache, rawRightState, isRightCurrentlyTracked, warningMinDurationSeconds, dt, ref _rightState);

            bool isLeftOK = isLeftCurrentlyTracked && !isLeftWarning;
            bool isRightOK = isRightCurrentlyTracked && !isRightWarning;

            if (_isInitialized && ((_leftWasOK && !isLeftOK) || (_rightWasOK && !isRightOK)))
            {
                if (Time.time - _lastWarnTime >= warnCooldownSeconds)
                {
                    _lastWarnTime = Time.time;
                    eventDispatcher?.DispatchControllerTrackWarn();
                }
            }

            _leftWasOK = isLeftOK;
            _rightWasOK = isRightOK;

            // 4. Tracking and warning status event dispatch: delegated to XRDeviceEventDispatcher
            bool leftStateChanged = !_isInitialized || isLeftCurrentlyTracked != _leftWasTracked || isLeftWarning != _leftWasWarning;
            if (leftStateChanged)
            {
                if (_isInitialized)
                {
                    if (isLeftCurrentlyTracked != _leftWasTracked)
                    {
                        eventDispatcher?.DispatchTrackingChanged(true, isLeftCurrentlyTracked);
                    }
                    eventDispatcher?.DispatchTrackingState(true, isLeftCurrentlyTracked, isLeftWarning);
                }
                _leftWasTracked = isLeftCurrentlyTracked;
                _leftWasWarning = isLeftWarning;
            }

            bool rightStateChanged = !_isInitialized || isRightCurrentlyTracked != _rightWasTracked || isRightWarning != _rightWasWarning;
            if (rightStateChanged)
            {
                if (_isInitialized)
                {
                    if (isRightCurrentlyTracked != _rightWasTracked)
                    {
                        eventDispatcher?.DispatchTrackingChanged(false, isRightCurrentlyTracked);
                    }
                    eventDispatcher?.DispatchTrackingState(false, isRightCurrentlyTracked, isRightWarning);
                }
                _rightWasTracked = isRightCurrentlyTracked;
                _rightWasWarning = isRightWarning;
            }

            if (!_isInitialized)
            {
                eventDispatcher?.DispatchTrackingState(true, isLeftCurrentlyTracked, isLeftWarning);
                eventDispatcher?.DispatchTrackingState(false, isRightCurrentlyTracked, isRightWarning);
            }

            // 6. Logging formatting & output
            if (enableDebugLog || enableFileLog)
            {
                Vector3 leftLinVel = GetLinearVelocity(true, _leftDeviceCache);
                Vector3 leftAngVel = GetAngularVelocity(true, _leftDeviceCache);
                Vector3 leftLinAccel = GetLinearAcceleration(true, _leftDeviceCache);
                Vector3 leftAngAccel = GetAngularAcceleration(true, _leftDeviceCache);

                XRDeviceStateCore.LogHandState("[L]", _leftDeviceCache, rawLeftState, isLeftCurrentlyTracked, isLeftWarning,
                    logIntervalSeconds, enableDebugLog, enableFileLog, leftLinVel, leftAngVel, leftLinAccel, leftAngAccel,
                    ref _leftState, ref _leftLogCache);

                Vector3 rightLinVel = GetLinearVelocity(false, _rightDeviceCache);
                Vector3 rightAngVel = GetAngularVelocity(false, _rightDeviceCache);
                Vector3 rightLinAccel = GetLinearAcceleration(false, _rightDeviceCache);
                Vector3 rightAngAccel = GetAngularAcceleration(false, _rightDeviceCache);

                XRDeviceStateCore.LogHandState("[R]", _rightDeviceCache, rawRightState, isRightCurrentlyTracked, isRightWarning,
                    logIntervalSeconds, enableDebugLog, enableFileLog, rightLinVel, rightAngVel, rightLinAccel, rightAngAccel,
                    ref _rightState, ref _rightLogCache);
            }

            _isInitialized = true;
        }

        private void ProcessShakeDetection(bool isLeft, XRController device, int rawTrackingState)
        {
            if (rawTrackingState == 0)
            {
                if (isLeft)
                {
                    _leftState.swingHistory1 = -999f;
                    _leftState.swingHistory2 = -999f;
                }
                else
                {
                    _rightState.swingHistory1 = -999f;
                    _rightState.swingHistory2 = -999f;
                }
                return;
            }

            Vector3 linAccel = GetLinearAcceleration(isLeft, device);
            bool isSingleSwing, isTripleSwing, isAnyShake;

            if (isLeft)
            {
                XRDeviceStateCore.DetectShakeWithAcceleration(linAccel, shakeAccelThreshold, swingCooldownDuration, maxComboInterval,
                    ref _leftState, out isSingleSwing, out isTripleSwing, out isAnyShake);
            }
            else
            {
                XRDeviceStateCore.DetectShakeWithAcceleration(linAccel, shakeAccelThreshold, swingCooldownDuration, maxComboInterval,
                    ref _rightState, out isSingleSwing, out isTripleSwing, out isAnyShake);
            }

            if (eventDispatcher != null)
            {
                if (isSingleSwing) eventDispatcher.DispatchSwing(isLeft);
                if (isTripleSwing) eventDispatcher.DispatchTripleSwing(isLeft);
                if (isAnyShake) eventDispatcher.DispatchAnyShake();
            }
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected ||
                change == InputDeviceChange.ConfigurationChanged)
            {
                XRDeviceStateCore.FindControllers(out _leftDeviceCache, out _rightDeviceCache);
            }
        }

        private void EnableActionProperty(InputActionProperty property)
        {
            if (property.action != null)
            {
                property.action.Enable();
            }
        }

        private void DisableActionProperty(InputActionProperty property)
        {
            if (property.action != null)
            {
                property.action.Disable();
            }
        }

        private void DiagnoseActionsOnce()
        {
            if ((!enableDebugLog && !enableFileLog) || _leftDeviceCache == null || !_leftDeviceCache.added) return;

            if (!_leftDiagDone)
            {
                _leftDiagDone = true;
                DiagnoseAction("L linVel", leftLinearVelocityAction);
                DiagnoseAction("L angVel", leftAngularVelocityAction);
                DiagnoseAction("L linAccel", leftLinearAccelerationAction);
                DiagnoseAction("L angAccel", leftAngularAccelerationAction);
            }

            if (!_rightDiagDone && _rightDeviceCache != null && _rightDeviceCache.added)
            {
                _rightDiagDone = true;
                DiagnoseAction("R linVel", rightLinearVelocityAction);
                DiagnoseAction("R angVel", rightAngularVelocityAction);
                DiagnoseAction("R linAccel", rightLinearAccelerationAction);
                DiagnoseAction("R angAccel", rightAngularAccelerationAction);
            }
        }

        private void DiagnoseAction(string label, InputActionProperty property)
        {
            var action = property.action;
            if (action == null)
            {
                XRDeviceStateCore.DispatchLog($"[XR][DIAG] {label}: action null", LogType.Log, enableDebugLog, enableFileLog);
                return;
            }
            string enabledStr = action.enabled ? "enabled" : "DISABLED";
            string activeCtrl = action.activeControl != null ? action.activeControl.path : "none";
            int bindCount = action.bindings.Count;
            XRDeviceStateCore.DispatchLog($"[XR][DIAG] {label}: {enabledStr} | bindings={bindCount} | activeCtrl={activeCtrl} | val={action.ReadValue<Vector3>()}", LogType.Log, enableDebugLog, enableFileLog);
        }
    }
}
