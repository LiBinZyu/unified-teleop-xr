using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

public class XRGeneralDeviceStateMonitor : MonoBehaviour
{
    public enum FilterMethod
    {
        None,
        BitwiseDebounce,
        KinematicDrift,
        Combined
    }

    private struct ControllerState
    {
        public bool isTrackedUIState; // Current output state for UI
        public float warningTimer;    // Timer to keep warning UI active to prevent flickering
        
        // Bitwise Debounce cache
        public uint bitHistory;       
        
        // Kinematic Drift cache
        public float lostTimer;       
        public string lastRejectReason;

        // === Simple Shake Detection Cache ===
        public float lastSwingTime;    
        public float swingHistory1;    
        public float swingHistory2;

        // === Velocity Jump Log Cooldown ===
        public float lastVelJumpLogTime;   // Last time a VelocityJump was logged (to prevent per-frame spam)

        // === LinVel Freeze Detection ===
        public float linVelLostTimer;      // How long linVel has been below the minimum threshold

        // === Position Jump Detection ===
        public Vector3 lastTrackedPos;
        public float lastTrackedPosTime;
        public bool hasLastTrackedPos;
    }

    [Header("UI References")]
    public GameObject leftTrackedUI;
    public GameObject leftUntrackedUI;
    public GameObject leftWarningUI;
    public GameObject rightTrackedUI;
    public GameObject rightUntrackedUI;
    public GameObject rightWarningUI;

    [Header("Left")]
    public UnityEngine.InputSystem.InputActionProperty leftIsTrackedAction;
    public UnityEngine.InputSystem.InputActionProperty leftTrackingStateAction;
    public UnityEngine.InputSystem.InputActionProperty leftLinearVelocityAction;
    public UnityEngine.InputSystem.InputActionProperty leftAngularVelocityAction;
    public UnityEngine.InputSystem.InputActionProperty leftLinearAccelerationAction;
    public UnityEngine.InputSystem.InputActionProperty leftAngularAccelerationAction;
    [Header("Right")]
    public UnityEngine.InputSystem.InputActionProperty rightIsTrackedAction;
    public UnityEngine.InputSystem.InputActionProperty rightTrackingStateAction;
    public UnityEngine.InputSystem.InputActionProperty rightLinearVelocityAction;
    public UnityEngine.InputSystem.InputActionProperty rightAngularVelocityAction;
    public UnityEngine.InputSystem.InputActionProperty rightLinearAccelerationAction;
    public UnityEngine.InputSystem.InputActionProperty rightAngularAccelerationAction;

    [Tooltip("Optional: assign XRAccelerationBridge to read native PICO acceleration. If null, falls back to device control lookup.")]
    public Apex.Utils.XRAccelerationBridge accelerationBridge;

    [Header("Filtering Settings")]
    public FilterMethod filterMethod = FilterMethod.None;
    [Tooltip("Require full 6DoF (Position + Rotation) tracking state. If false, 3DoF rotation-only will be accepted.")]
    private bool require6DoF = true;
    private int frameTolerance = 20;
    private float lostToleranceSeconds = 0.1f;
    private float maxHumanSpeed = 8.0f;       // linVel sanity upper-bound (garbage sentinel rejection)
    private float minLinVelForTracking = 0.001f;  // linVel below this for lostToleranceSeconds = tracking lost
    private float posJumpMinDelta = 0.015f;       // 1.5cm minimum Δpos to consider as jump
    private float posJumpMaxImpliedSpeed = 4.0f;  // m/s – Δpos/Δt above this = position jump
    private float warningMinDurationSeconds = 2.0f;
    private float warnCooldownSeconds = 1.0f;

    [Header("Logging Settings")]
    [Tooltip("Print tracking logs to Unity Console")]
    public bool enableDebugLog = true;
    [Tooltip("Write tracking logs to disk file via Logger.LogApp")]
    public bool enableFileLog = false;
    [Tooltip("Interval in seconds for periodic continuous logging (0 = log only on state change)")]
    private float logIntervalSeconds = 0.5f;

    [Header("Shake Detection Settings")]
    [Tooltip("Minimum acceleration (m/s²) required to trigger a swing")]
    private float shakeAccelThreshold = 200.0f;
    [Tooltip("Minimum time (seconds) between swings")]
    private float swingCooldownDuration = 0.15f;
    [Tooltip("Total time (seconds) allowed to complete 3 swings")]
    private float maxComboInterval = 1.0f;

    private XRController _leftDeviceCache;
    private XRController _rightDeviceCache;

    [Header("Events")]
    public UnityEngine.Events.UnityEvent OnLeftTracked;
    public UnityEngine.Events.UnityEvent OnLeftUntracked;
    public UnityEngine.Events.UnityEvent OnRightTracked;
    public UnityEngine.Events.UnityEvent OnRightUntracked;
    public UnityEngine.Events.UnityEvent OnControllerTrackWarn;
    
    [Header("Shake Events")]
    public UnityEngine.Events.UnityEvent OnLeftSwing;
    public UnityEngine.Events.UnityEvent OnRightSwing;
    public UnityEngine.Events.UnityEvent OnLeftTripleSwing;
    public UnityEngine.Events.UnityEvent OnRightTripleSwing;
    public UnityEngine.Events.UnityEvent OnAnyShake;
    
    private bool _leftWasTracked = false;
    private bool _rightWasTracked = false;
    private bool _leftWasWarning = false;
    private bool _rightWasWarning = false;
    private bool _isInitialized = false;

    private bool _leftWasOK = false;
    private bool _rightWasOK = false;
    private float _lastWarnTime = -999f;

    private ControllerState _leftState;
    private ControllerState _rightState;

    public bool IsLeftTracked => _leftWasTracked;
    public bool IsRightTracked => _rightWasTracked;

    public int GetRawLeftTrackingState()
    {
        if (leftTrackingStateAction.action != null && leftTrackingStateAction.action.enabled)
        {
            try { return leftTrackingStateAction.action.ReadValue<int>(); } catch { }
        }
        
        if (_leftDeviceCache != null && _leftDeviceCache.added)
        {
            return _leftDeviceCache.trackingState.ReadValue();
        }
        return 0;
    }

    public int GetRawRightTrackingState()
    {
        if (rightTrackingStateAction.action != null && rightTrackingStateAction.action.enabled)
        {
            try { return rightTrackingStateAction.action.ReadValue<int>(); } catch { }
        }

        if (_rightDeviceCache != null && _rightDeviceCache.added)
        {
            return _rightDeviceCache.trackingState.ReadValue();
        }
        return 0;
    }

    public int GetLeftTrackingState()
    {
        if (!IsLeftTracked) return 0;
        return GetRawLeftTrackingState();
    }

    public int GetRightTrackingState()
    {
        if (!IsRightTracked) return 0;
        return GetRawRightTrackingState();
    }

    private const float kMaxSanitySpeed = 1000f; // anything beyond this is a garbage sentinel from the XR runtime

    private Vector3 ReadDeviceControl(XRController device, params string[] candidates)
    {
        if (device == null) return Vector3.zero;
        foreach (var name in candidates)
        {
            var ctrl = device.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>(name);
            if (ctrl == null) continue;
            Vector3 val = ctrl.ReadValue();
            // Reject garbage sentinel values (e.g. Pico returns ~-4.3e8 when pose not yet initialized)
            if (val.sqrMagnitude < kMaxSanitySpeed * kMaxSanitySpeed) return val;
        }
        return Vector3.zero;
    }

    public Vector3 GetLinearVelocity(bool isLeft, XRController device = null)
        => ReadDeviceControl(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache),
            "deviceVelocity", "devicePose/velocity");

    public Vector3 GetAngularVelocity(bool isLeft, XRController device = null)
        => ReadDeviceControl(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache),
            "deviceAngularVelocity", "devicePose/angularVelocity");

    public Vector3 GetLinearAcceleration(bool isLeft, XRController device = null)
    {
        if (accelerationBridge != null)
            return isLeft ? accelerationBridge.LeftLinearAccel : accelerationBridge.RightLinearAccel;
        return ReadDeviceControl(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache),
            "deviceAcceleration", "devicelinearacceleration", "linearAcceleration", "devicePose/acceleration");
    }

    public Vector3 GetAngularAcceleration(bool isLeft, XRController device = null)
    {
        if (accelerationBridge != null)
            return isLeft ? accelerationBridge.LeftAngularAccel : accelerationBridge.RightAngularAccel;
        return ReadDeviceControl(device ?? (isLeft ? _leftDeviceCache : _rightDeviceCache),
            "deviceAngularAcceleration", "deviceangularacceleration", "angularAcceleration", "devicePose/angularAcceleration");
    }

    private float _lastDeviceSearchTime = 0f;

    private void OnEnable()
    {
        InputSystem.onDeviceChange += OnDeviceChange;
        FindControllers(out _leftDeviceCache, out _rightDeviceCache);

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

    private void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, InputDeviceChange change)
    {
        if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed ||
            change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected ||
            change == InputDeviceChange.ConfigurationChanged)
        {
            FindControllers(out _leftDeviceCache, out _rightDeviceCache);
        }
    }

    private void EnableActionProperty(UnityEngine.InputSystem.InputActionProperty property)
    {
        if (property.action != null)
        {
            property.action.Enable();
        }
    }

    private void DisableActionProperty(UnityEngine.InputSystem.InputActionProperty property)
    {
        if (property.action != null)
        {
            property.action.Disable();
        }
    }

    private void DiagnoseAction(string label, UnityEngine.InputSystem.InputActionProperty property)
    {
        if (!enableDebugLog && !enableFileLog) return;
        var action = property.action;
        if (action == null)
        {
            DispatchLog($"[XR][DIAG] {label}: action null", LogType.Log);
            return;
        }
        string enabledStr = action.enabled ? "enabled" : "DISABLED";
        string activeCtrl = action.activeControl != null ? action.activeControl.path : "none";
        int bindCount = action.bindings.Count;
        DispatchLog($"[XR][DIAG] {label}: {enabledStr} | bindings={bindCount} | activeCtrl={activeCtrl} | val={action.ReadValue<Vector3>()}", LogType.Log);
    }

    private void Start()
    {
        _leftState = default;
        _rightState = default;
        
        _leftState.swingHistory1 = -999f;
        _leftState.swingHistory2 = -999f;
        _rightState.swingHistory1 = -999f;
        _rightState.swingHistory2 = -999f;

        UpdateLeftUIState(false, false);
        UpdateRightUIState(false, false);
    }

    private bool _leftDiagDone = false;
    private bool _rightDiagDone = false;

    private void Update()
    {
        if (_leftDeviceCache == null || !_leftDeviceCache.added || _rightDeviceCache == null || !_rightDeviceCache.added)
        {
            if (Time.unscaledTime - _lastDeviceSearchTime > 1.0f)
            {
                _lastDeviceSearchTime = Time.unscaledTime;
                FindControllers(out _leftDeviceCache, out _rightDeviceCache);
            }
        }

        // 设备连接后且开启日志时才有意义做 binding 诊断
        if ((enableDebugLog || enableFileLog) && _leftDeviceCache != null && _leftDeviceCache.added && !_leftDiagDone)
        {
            _leftDiagDone = true;
            DiagnoseAction("L linVel",  leftLinearVelocityAction);
            DiagnoseAction("L angVel",  leftAngularVelocityAction);
            DiagnoseAction("L linAccel",leftLinearAccelerationAction);
            DiagnoseAction("L angAccel",leftAngularAccelerationAction);
        }
        if ((enableDebugLog || enableFileLog) && _rightDeviceCache != null && _rightDeviceCache.added && !_rightDiagDone)
        {
            _rightDiagDone = true;
            DiagnoseAction("R linVel",  rightLinearVelocityAction);
            DiagnoseAction("R angVel",  rightAngularVelocityAction);
            DiagnoseAction("R linAccel",rightLinearAccelerationAction);
            DiagnoseAction("R angAccel",rightAngularAccelerationAction);
        }

        bool isLeftCurrentlyTracked = EvaluateControllerState("[L]", _leftDeviceCache, ref _leftState);
        bool isRightCurrentlyTracked = EvaluateControllerState("[R]", _rightDeviceCache, ref _rightState);

        _leftState.isTrackedUIState = isLeftCurrentlyTracked;
        _rightState.isTrackedUIState = isRightCurrentlyTracked;

        float dt = Time.deltaTime;

        // 甩动/摇晃检测（基于硬件/Action加速度，包含高速 0 物理缺失保护）
        if (GetRawLeftTrackingState() != 0)
        {
            DetectShakeWithAcceleration(true, _leftDeviceCache, ref _leftState, dt);
        }
        else
        {
            _leftState.swingHistory1 = -999f;
            _leftState.swingHistory2 = -999f;
        }

        if (GetRawRightTrackingState() != 0)
        {
            DetectShakeWithAcceleration(false, _rightDeviceCache, ref _rightState, dt);
        }
        else
        {
            _rightState.swingHistory1 = -999f;
            _rightState.swingHistory2 = -999f;
        }

        // Calculate warnings (UI 逻辑)
        bool isLeftWarning = ProcessWarningTimer(true, _leftDeviceCache, isLeftCurrentlyTracked, ref _leftState);
        bool isRightWarning = ProcessWarningTimer(false, _rightDeviceCache, isRightCurrentlyTracked, ref _rightState);

        bool isLeftOK = isLeftCurrentlyTracked && !isLeftWarning;
        bool isRightOK = isRightCurrentlyTracked && !isRightWarning;

        if (_isInitialized && ((_leftWasOK && !isLeftOK) || (_rightWasOK && !isRightOK)))
        {
            TriggerTrackWarning();
        }

        _leftWasOK = isLeftOK;
        _rightWasOK = isRightOK;

        UpdateUI(true, isLeftCurrentlyTracked, isLeftWarning, ref _leftWasTracked, ref _leftWasWarning);
        UpdateUI(false, isRightCurrentlyTracked, isRightWarning, ref _rightWasTracked, ref _rightWasWarning);

        if (enableDebugLog || enableFileLog)
        {
            LogHandState("[L]", _leftDeviceCache, isLeftCurrentlyTracked, isLeftWarning, ref _leftState, ref _leftLogCache);
            LogHandState("[R]", _rightDeviceCache, isRightCurrentlyTracked, isRightWarning, ref _rightState, ref _rightLogCache);
        }

        _isInitialized = true;
    }

    private bool ProcessWarningTimer(bool isLeft, XRController device, bool isTracked, ref ControllerState state)
    {
        if (device != null)
        {
            int rawState = isLeft ? GetRawLeftTrackingState() : GetRawRightTrackingState();
            bool isRawTracked = rawState != 0;
            bool isPositionOrRotationLost = (rawState & 3) != 3;
            bool filterRejected = isRawTracked && !isTracked;

            if (isPositionOrRotationLost || filterRejected)
            {
                state.warningTimer = warningMinDurationSeconds;
            }
        }

        if (state.warningTimer > 0f)
        {
            state.warningTimer -= Time.deltaTime;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 极简版加速度 Shake 与连击检测
    /// </summary>
    private void DetectShakeWithAcceleration(bool isLeft, XRController device, ref ControllerState state, float dt)
    {
        Vector3 linAccel = GetLinearAcceleration(isLeft, device);
        float accelMag = linAccel.magnitude;

        // 1. 判断是否超出加速度阈值，且过了冷却时间
        if (accelMag >= shakeAccelThreshold && Time.time - state.lastSwingTime > swingCooldownDuration)
        {
            state.lastSwingTime = Time.time;

            // 触发单次挥动
            if (isLeft) OnLeftSwing?.Invoke();
            else OnRightSwing?.Invoke();

            // 2. 检查 3 连击：距离前两下挥动的时间是否在允许的总时间窗口内
            if (Time.time - state.swingHistory2 <= maxComboInterval)
            {
                // 连击成功，清空历史，防止紧接着触发 4 连击、5 连击
                state.swingHistory1 = -999f;
                state.swingHistory2 = -999f;

                if (isLeft) OnLeftTripleSwing?.Invoke();
                else OnRightTripleSwing?.Invoke();

                OnAnyShake?.Invoke();
            }
            else
            {
                // 未达成连击，滚动更新历史记录
                state.swingHistory2 = state.swingHistory1;
                state.swingHistory1 = Time.time;
            }
        }
    }

    private static bool IsHandTrackingLayout(string layoutName)
    {
        if (string.IsNullOrEmpty(layoutName)) return false;
        return layoutName.IndexOf("handinteraction", StringComparison.OrdinalIgnoreCase) >= 0 ||
               layoutName.IndexOf("handtracking", StringComparison.OrdinalIgnoreCase) >= 0 ||
               layoutName.IndexOf("metaquesthand", StringComparison.OrdinalIgnoreCase) >= 0 ||
               layoutName.IndexOf("oculushand", StringComparison.OrdinalIgnoreCase) >= 0 ||
               layoutName.IndexOf("picohand", StringComparison.OrdinalIgnoreCase) >= 0 ||
               layoutName.IndexOf("customhand", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void FindControllers(out XRController leftDevice, out XRController rightDevice)
    {
        leftDevice = null;
        rightDevice = null;

        var devices = InputSystem.devices;
        for (int i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            if (device is XRController xrDevice)
            {
                bool isLeft = false;
                bool isRight = false;
                var usages = xrDevice.usages;
                for (int u = 0; u < usages.Count; u++)
                {
                    if (usages[u] == UnityEngine.InputSystem.CommonUsages.LeftHand) isLeft = true;
                    else if (usages[u] == UnityEngine.InputSystem.CommonUsages.RightHand) isRight = true;
                }

                if (isLeft || isRight)
                {
                    if (IsHandTrackingLayout(xrDevice.layout))
                    {
                        continue;
                    }

                    if (isLeft) leftDevice = xrDevice;
                    if (isRight) rightDevice = xrDevice;
                }
            }
        }
    }

    private bool EvaluateControllerState(string handTag, XRController device, ref ControllerState state)
    {
        bool isLeft = handTag == "[L]";

        if (device == null || !device.added)
        {
            state.hasLastTrackedPos = false;
            state.lastRejectReason = "Device Disconnected";
            return false;
        }

        // Primary gate: use IsTracked action if assigned (most reliable optical tracking indicator)
        var trackedProperty = isLeft ? leftIsTrackedAction : rightIsTrackedAction;
        bool hasIsTrackedAction = trackedProperty.action != null && trackedProperty.action.enabled;
        bool isTrackedByAction = false;
        if (hasIsTrackedAction)
        {
            try { isTrackedByAction = trackedProperty.action.ReadValue<float>() >= 0.5f; }
            catch { hasIsTrackedAction = false; }
        }

        // Fallback: read directly from XRController device
        int rawDeviceState = device.trackingState.ReadValue();
        bool isTrackedByDevice = require6DoF ? (rawDeviceState & 3) == 3 : rawDeviceState != 0;

        // Combine: if isTracked action assigned, trust it as primary gate
        bool isRawTracked = hasIsTrackedAction ? (isTrackedByAction && isTrackedByDevice) : isTrackedByDevice;

        if (!isRawTracked)
        {
            state.lastRejectReason = "Hardware Tracking Lost";
            return false;
        }

        switch (filterMethod)
        {
            case FilterMethod.BitwiseDebounce:
                return EvaluateState_BitwiseDebounce(handTag, device, ref state);
            case FilterMethod.KinematicDrift:
                return EvaluateState_KinematicDrift(handTag, device, ref state);
            case FilterMethod.Combined:
                return EvaluateState_Combined(handTag, device, ref state);
            case FilterMethod.None:
            default:
                return true;
        }
    }

    private void TriggerTrackWarning()
    {
        if (Time.time - _lastWarnTime >= warnCooldownSeconds)
        {
            _lastWarnTime = Time.time;
            OnControllerTrackWarn?.Invoke();
        }
    }

    private void UpdateUI(bool isLeft, bool isTracked, bool isWarning, ref bool wasTracked, ref bool wasWarning)
    {
        if (!_isInitialized || isTracked != wasTracked || isWarning != wasWarning)
        {
            if (isLeft)
            {
                UpdateLeftUIState(isTracked, isWarning);
                if (_isInitialized && isTracked != wasTracked)
                {
                    if (isTracked) OnLeftTracked?.Invoke();
                    else OnLeftUntracked?.Invoke();
                }
            }
            else
            {
                UpdateRightUIState(isTracked, isWarning);
                if (_isInitialized && isTracked != wasTracked)
                {
                    if (isTracked) OnRightTracked?.Invoke();
                    else OnRightUntracked?.Invoke();
                }
            }

            wasTracked = isTracked;
            wasWarning = isWarning;
        }
    }

    private void UpdateLeftUIState(bool isTracked, bool isWarning)
    {
        if (leftTrackedUI != null) leftTrackedUI.SetActive(isTracked && !isWarning);
        if (leftUntrackedUI != null) leftUntrackedUI.SetActive(!isTracked && !isWarning);
        if (leftWarningUI != null) leftWarningUI.SetActive(isWarning);
    }

    private void UpdateRightUIState(bool isTracked, bool isWarning)
    {
        if (rightTrackedUI != null) rightTrackedUI.SetActive(isTracked && !isWarning);
        if (rightUntrackedUI != null) rightUntrackedUI.SetActive(!isTracked && !isWarning);
        if (rightWarningUI != null) rightWarningUI.SetActive(isWarning);
    }

    private bool EvaluateState_BitwiseDebounce(string handTag, XRController device, ref ControllerState state)
    {
        bool isLeft = handTag == "[L]";
        int rawDebounceState = isLeft ? GetRawLeftTrackingState() : GetRawRightTrackingState();
        bool isRawTracked = require6DoF ? (rawDebounceState & 3) == 3 : rawDebounceState != 0;
        uint prevHistory = state.bitHistory;

        state.bitHistory = (state.bitHistory << 1) | (isRawTracked ? 1u : 0u);

        uint toleranceMask = frameTolerance >= 32 ? uint.MaxValue : (1u << frameTolerance) - 1;
        bool isDebounced = (state.bitHistory & toleranceMask) > 0;
        bool prevDebounced = (prevHistory & toleranceMask) > 0;

        if ((enableDebugLog || enableFileLog) && prevDebounced && !isDebounced && isRawTracked)
        {
            LogDebounceReject(handTag, rawDebounceState, state.bitHistory);
        }

        if (!isDebounced)
        {
            state.lastRejectReason = (enableDebugLog || enableFileLog)
                ? $"Debounce Reject (0x{state.bitHistory:X8})"
                : "Debounce Reject";
        }

        return isDebounced;
    }

    /// <summary>
    /// 基于线速度 (linearVelocity) 和角速度 (angularVelocity) 阈值的极简定位丢失与抽动检测算法
    /// </summary>
    private bool EvaluateState_KinematicDrift(string handTag, XRController device, ref ControllerState state)
    {
        bool isLeft = handTag == "[L]";

        if (device == null)
        {
            state.lastRejectReason = "Device Disconnected";
            return false;
        }

        int rawState = isLeft ? GetRawLeftTrackingState() : GetRawRightTrackingState();
        bool isRawTracked = require6DoF ? (rawState & 3) == 3 : rawState != 0;

        if (!isRawTracked)
        {
            float prevLostTimer = state.lostTimer;
            state.lostTimer += Time.deltaTime;

            if (state.lostTimer > lostToleranceSeconds)
            {
                state.lastRejectReason = (enableDebugLog || enableFileLog)
                    ? $"Drift Timeout ({state.lostTimer:F2}s > {lostToleranceSeconds:F2}s)"
                    : "Drift Timeout";
                if ((enableDebugLog || enableFileLog) && prevLostTimer <= lostToleranceSeconds)
                {
                    LogDriftTimeout(handTag, state.lostTimer);
                }
                return false;
            }
            
            return state.isTrackedUIState;
        }

        bool justRecovered = state.lostTimer > 0f;
        state.lostTimer = 0f;

        // === Position Jump Detection (Δpos / Δt) ===
        Vector3 currentPos = device.devicePosition.ReadValue();
        if (state.hasLastTrackedPos)
        {
            float posDelta = Vector3.Distance(currentPos, state.lastTrackedPos);
            float dtPos = Time.time - state.lastTrackedPosTime;

            if (posDelta > posJumpMinDelta && dtPos > 0f && dtPos < 1.0f)
            {
                float effectiveDt = justRecovered ? Time.deltaTime : dtPos;
                float impliedSpeed = posDelta / Mathf.Max(effectiveDt, 0.001f);

                if (impliedSpeed > posJumpMaxImpliedSpeed)
                {
                    state.lastRejectReason = (enableDebugLog || enableFileLog)
                        ? $"Position Jump (Δ{posDelta * 100f:F1}cm/{dtPos * 1000f:F0}ms = {impliedSpeed:F1}m/s)"
                        : "Position Jump";
                    if ((enableDebugLog || enableFileLog) && (Time.time - state.lastVelJumpLogTime > 0.5f))
                    {
                        state.lastVelJumpLogTime = Time.time;
                        LogPositionJump(handTag, state.lastTrackedPos, currentPos, posDelta, dtPos, impliedSpeed);
                    }
                    state.lastTrackedPos = currentPos;
                    state.lastTrackedPosTime = Time.time;
                    return false;
                }
            }
        }
        state.lastTrackedPos = currentPos;
        state.lastTrackedPosTime = Time.time;
        state.hasLastTrackedPos = true;

        // 使用线速度与角速度直接判定高速抽动或丢失定位
        Vector3 linVel = GetLinearVelocity(isLeft, device);
        Vector3 angVel = GetAngularVelocity(isLeft, device);

        float linSpeed = linVel.magnitude;
        float angSpeed = angVel.magnitude;

        // 线速度低于最小阈值超过容忍时间 → 光学追踪丢失或静止
        if (linSpeed < minLinVelForTracking)
        {
            state.linVelLostTimer += Time.deltaTime;
            if (state.linVelLostTimer > lostToleranceSeconds)
            {
                state.lastRejectReason = (enableDebugLog || enableFileLog)
                    ? $"LinVel Freeze ({linSpeed:F3}m/s < {minLinVelForTracking:F3}m/s for {state.linVelLostTimer:F2}s)"
                    : "LinVel Freeze";
                return false;
            }
        }
        else
        {
            state.linVelLostTimer = 0f;
        }

        if (linSpeed > maxHumanSpeed)
        {
            state.lastRejectReason = (enableDebugLog || enableFileLog)
                ? $"High Linear Speed Jump ({linSpeed:F1}m/s > {maxHumanSpeed:F1}m/s)"
                : "High Linear Speed Jump";
            if ((enableDebugLog || enableFileLog) && (Time.time - state.lastVelJumpLogTime > 0.5f))
            {
                state.lastVelJumpLogTime = Time.time;
                LogVelocityJump(handTag, linVel, angVel, linSpeed, maxHumanSpeed, "Linear");
            }
            return false; 
        }

        // NOTE: No upper-bound check on angularSpeed - fast intentional swings can exceed any reasonable limit
        // and are handled by shake detection, not tracking loss detection.

        return true;
    }

    private bool EvaluateState_Combined(string handTag, XRController device, ref ControllerState state)
    {
        bool isDebounced = EvaluateState_BitwiseDebounce(handTag, device, ref state);
        bool isDriftFree = EvaluateState_KinematicDrift(handTag, device, ref state);
        return isDebounced && isDriftFree;
    }

    #region Logging

    private enum HandLoggedState { Unknown, OK, WARN, LOST }

    private struct HandLogCache
    {
        public bool isInitialized;
        public HandLoggedState lastState;
        public int lastRawState;
        public float lastLogTime;
    }

    private HandLogCache _leftLogCache;
    private HandLogCache _rightLogCache;

    private void DispatchLog(string message, LogType logType = LogType.Log)
    {
        if (enableDebugLog)
        {
            if (logType == LogType.Error) Debug.LogError(message);
            else if (logType == LogType.Warning) Debug.LogWarning(message);
            else Debug.Log(message);
        }
        if (enableFileLog)
        {
            Logger.LogApp(message, logType);
        }
    }

    private void LogHandState(string handTag, XRController device, bool isTracked, bool isWarning, ref ControllerState state, ref HandLogCache cache)
    {
        if (!enableDebugLog && !enableFileLog) return;

        bool isLeft = handTag == "[L]";
        int rawState = isLeft ? GetRawLeftTrackingState() : GetRawRightTrackingState();
        HandLoggedState currentState = !isTracked ? HandLoggedState.LOST : (isWarning ? HandLoggedState.WARN : HandLoggedState.OK);

        bool stateChanged = currentState != cache.lastState;
        bool rawStateChanged = rawState != cache.lastRawState;
        bool timeIntervalPassed = logIntervalSeconds > 0f && (Time.time - cache.lastLogTime >= logIntervalSeconds);

        if (!cache.isInitialized || stateChanged || rawStateChanged || timeIntervalPassed)
        {
            Vector3 linVel = GetLinearVelocity(isLeft, device);
            Vector3 angVel = GetAngularVelocity(isLeft, device);
            Vector3 linAccel = GetLinearAcceleration(isLeft, device);
            Vector3 angAccel = GetAngularAcceleration(isLeft, device);

            string dynamicsStr = $"linVel: {linVel.ToString("F2")}m/s ({linVel.magnitude:F2}), angVel: {angVel.ToString("F2")}rad/s ({angVel.magnitude:F2}), linAccel: {linAccel.ToString("F2")}m/s², angAccel: {angAccel.ToString("F2")}rad/s²";

            if (!cache.isInitialized)
            {
                cache.isInitialized = true;
                cache.lastState = currentState;
                cache.lastRawState = rawState;
                cache.lastLogTime = Time.time;

                if (currentState != HandLoggedState.OK)
                {
                    string cause = GetStateCause(device, isTracked, isWarning, rawState, state.lastRejectReason);
                    DispatchLog($"[XR] {handTag} INIT: {currentState} | raw: {GetRawStateString(rawState)} | {dynamicsStr} | cause: {cause}", LogType.Warning);
                }
                else
                {
                    DispatchLog($"[XR] {handTag} INIT: OK | raw: {GetRawStateString(rawState)} | {dynamicsStr}", LogType.Log);
                }
                return;
            }

            cache.lastLogTime = Time.time;

            if (currentState == HandLoggedState.OK)
            {
                DispatchLog($"[XR] {handTag} STATE: OK | raw: {GetRawStateString(rawState)} | {dynamicsStr}", LogType.Log);
            }
            else
            {
                string cause = GetStateCause(device, isTracked, isWarning, rawState, state.lastRejectReason);
                DispatchLog($"[XR] {handTag} STATE: {currentState} | raw: {GetRawStateString(rawState)} | {dynamicsStr} | cause: {cause}", LogType.Warning);
            }
        }

        cache.lastState = currentState;
        cache.lastRawState = rawState;
    }

    private void LogVelocityJump(string handTag, Vector3 linVel, Vector3 angVel, float speed, float threshold, string type)
    {
        DispatchLog($"[XR] {handTag} VELOCITY JUMP ({type}) | speed: {speed:F1} > {threshold:F1} | linVel: {linVel.ToString("F2")}, angVel: {angVel.ToString("F2")}", LogType.Warning);
    }

    private void LogPositionJump(string handTag, Vector3 from, Vector3 to, float delta, float dt, float impliedSpeed)
    {
        DispatchLog($"[XR] {handTag} POSITION JUMP | Δ{delta * 100f:F1}cm in {dt * 1000f:F0}ms ({impliedSpeed:F1}m/s) | from: {from.ToString("F3")} to: {to.ToString("F3")}", LogType.Warning);
    }

    private void LogDriftTimeout(string handTag, float lostTimer)
    {
        DispatchLog($"[XR] {handTag} DRIFT TIMEOUT | lost: {lostTimer:F2}s > {lostToleranceSeconds:F2}s", LogType.Warning);
    }

    private void LogDebounceReject(string handTag, int rawState, uint bitHistory)
    {
        DispatchLog($"[XR] {handTag} DEBOUNCE REJECT | raw: {GetRawStateString(rawState)} | bitHistory: 0x{bitHistory:X8}", LogType.Warning);
    }

    private string GetStateCause(XRController device, bool isTracked, bool isWarning, int rawState, string rejectReason)
    {
        if (device == null) return "Device Disconnected";
        if (rawState == 0) return "Hardware Tracking Lost";
        if ((rawState & 3) != 3) return "Pos or Rot Incomplete";
        if (!isTracked) return !string.IsNullOrEmpty(rejectReason) ? rejectReason : "Filter Rejected";
        if (isWarning) return "Warning Hold Active";
        return "Unknown";
    }

    private string GetRawStateString(int rawState)
    {
        if (rawState == 0) return "OFF";

        bool hasPos = (rawState & 1) != 0;
        bool hasRot = (rawState & 2) != 0;

        if (hasPos && hasRot) return "Pos+Rot";
        if (hasPos) return "Pos Only";
        if (hasRot) return "Rot Only";
        return "Invalid";
    }

    #endregion
}
