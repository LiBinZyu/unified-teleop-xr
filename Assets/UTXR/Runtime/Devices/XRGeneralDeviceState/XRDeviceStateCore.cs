using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UTXR.Core;
using Logger = UTXR.Core.Logger;

namespace UTXR.Devices
{
    /// <summary>
    /// Filtering and evaluation algorithm modes for XR tracking.
    /// </summary>
    public enum FilterMethod
    {
        None,
        BitwiseDebounce,
        KinematicDrift,
        Combined
    }

    /// <summary>
    /// Controller tracking logging state.
    /// </summary>
    public enum HandLoggedState
    {
        Unknown,
        OK,
        WARN,
        LOST
    }

    /// <summary>
    /// Internal tracking evaluation and filtering state cache (value type, zero GC allocations).
    /// </summary>
    public struct ControllerTrackingState
    {
        public bool isTrackedUIState;       // Output state for UI presentation
        public float warningTimer;          // Hold timer for warning UI to prevent flickering
        
        // Bitwise debounce cache
        public uint bitHistory;             
        
        // Kinematic drift cache
        public float lostTimer;             
        public string lastRejectReason;

        // Acceleration swing & combo detection cache
        public float lastSwingTime;         
        public float swingHistory1;         
        public float swingHistory2;

        // Velocity jump logging throttle
        public float lastVelJumpLogTime;    

        // Linear velocity freeze detection
        public float linVelLostTimer;       

        // Position jump detection
        public Vector3 lastTrackedPos;
        public float lastTrackedPosTime;
        public bool hasLastTrackedPos;

        /// <summary>
        /// Resets all tracking state caches.
        /// </summary>
        public void Reset()
        {
            isTrackedUIState = false;
            warningTimer = 0f;
            bitHistory = 0;
            lostTimer = 0f;
            lastRejectReason = null;
            lastSwingTime = 0f;
            swingHistory1 = -999f;
            swingHistory2 = -999f;
            lastVelJumpLogTime = 0f;
            linVelLostTimer = 0f;
            lastTrackedPos = Vector3.zero;
            lastTrackedPosTime = 0f;
            hasLastTrackedPos = false;
        }
    }

    /// <summary>
    /// Cache structure for logging state transitions.
    /// </summary>
    public struct HandLogCache
    {
        public bool isInitialized;
        public HandLoggedState lastState;
        public int lastRawState;
        public float lastLogTime;
    }

    /// <summary>
    /// Core tracking evaluation and filtering algorithms.
    /// Handles bitwise debounce, kinematic drift validation, shake/combo detection, and control reading.
    /// </summary>
    public static class XRDeviceStateCore
    {
        public const float MaxSanitySpeed = 1000f; // Reject sentinel values from XR runtime

        #region Controller Discovery and Control Reading

        /// <summary>
        /// Checks whether the specified layout corresponds to hand tracking rather than physical controllers.
        /// </summary>
        public static bool IsHandTrackingLayout(string layoutName)
        {
            if (string.IsNullOrEmpty(layoutName)) return false;
            return layoutName.IndexOf("handinteraction", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layoutName.IndexOf("handtracking", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layoutName.IndexOf("metaquesthand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layoutName.IndexOf("oculushand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layoutName.IndexOf("picohand", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   layoutName.IndexOf("customhand", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Scans connected InputSystem devices to find left and right XRControllers.
        /// </summary>
        public static void FindControllers(out XRController leftDevice, out XRController rightDevice)
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

        /// <summary>
        /// Safely reads a Vector3 control value from an XR controller, discarding invalid sentinel values.
        /// </summary>
        public static Vector3 ReadDeviceControl(XRController device, params string[] candidates)
        {
            if (device == null) return Vector3.zero;
            for (int i = 0; i < candidates.Length; i++)
            {
                var ctrl = device.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>(candidates[i]);
                if (ctrl == null) continue;
                Vector3 val = ctrl.ReadValue();
                if (val.sqrMagnitude < MaxSanitySpeed * MaxSanitySpeed) return val;
            }
            return Vector3.zero;
        }

        public static Vector3 GetLinearVelocity(XRController device)
        {
            return ReadDeviceControl(device, "deviceVelocity", "devicePose/velocity");
        }

        public static Vector3 GetAngularVelocity(XRController device)
        {
            return ReadDeviceControl(device, "deviceAngularVelocity", "devicePose/angularVelocity");
        }

        public static Vector3 GetLinearAcceleration(XRController device, XRAccelerationDeviceBridge bridge, bool isLeft)
        {
            var targetBridge = bridge ?? XRAccelerationDeviceBridge.Instance;
            if (targetBridge != null)
                return isLeft ? targetBridge.LeftLinearAccel : targetBridge.RightLinearAccel;
            return ReadDeviceControl(device, "deviceAcceleration", "devicelinearacceleration", "linearAcceleration", "devicePose/acceleration");
        }

        public static Vector3 GetAngularAcceleration(XRController device, XRAccelerationDeviceBridge bridge, bool isLeft)
        {
            var targetBridge = bridge ?? XRAccelerationDeviceBridge.Instance;
            if (targetBridge != null)
                return isLeft ? targetBridge.LeftAngularAccel : targetBridge.RightAngularAccel;
            return ReadDeviceControl(device, "deviceAngularAcceleration", "deviceangularacceleration", "angularAcceleration", "devicePose/angularAcceleration");
        }

        public static int GetRawTrackingState(InputActionProperty trackingStateAction, XRController device)
        {
            if (trackingStateAction.action != null && trackingStateAction.action.enabled)
            {
                try { return trackingStateAction.action.ReadValue<int>(); } catch { }
            }

            if (device != null && device.added)
            {
                return device.trackingState.ReadValue();
            }
            return 0;
        }

        #endregion

        #region State Evaluation & Filtering Algorithms

        /// <summary>
        /// Evaluates current tracking state for a controller combining hardware state and selected filter.
        /// </summary>
        public static bool EvaluateControllerState(
            string handTag,
            XRController device,
            InputActionProperty isTrackedAction,
            InputActionProperty trackingStateAction,
            FilterMethod filterMethod,
            bool require6DoF,
            int frameTolerance,
            float lostToleranceSeconds,
            float maxHumanSpeed,
            float minLinVelForTracking,
            float posJumpMinDelta,
            float posJumpMaxImpliedSpeed,
            bool enableLog,
            ref ControllerTrackingState state)
        {
            if (device == null || !device.added)
            {
                state.hasLastTrackedPos = false;
                state.lastRejectReason = "Device Disconnected";
                return false;
            }

            // 1. Primary gate: use IsTracked action if assigned
            bool hasIsTrackedAction = isTrackedAction.action != null && isTrackedAction.action.enabled;
            bool isTrackedByAction = false;
            if (hasIsTrackedAction)
            {
                try { isTrackedByAction = isTrackedAction.action.ReadValue<float>() >= 0.5f; }
                catch { hasIsTrackedAction = false; }
            }

            // 2. Hardware trackingState from XRController device
            int rawDeviceState = device.trackingState.ReadValue();
            bool isTrackedByDevice = require6DoF ? (rawDeviceState & 3) == 3 : rawDeviceState != 0;

            bool isRawTracked = hasIsTrackedAction ? (isTrackedByAction && isTrackedByDevice) : isTrackedByDevice;

            if (!isRawTracked)
            {
                state.lastRejectReason = "Hardware Tracking Lost";
                return false;
            }

            switch (filterMethod)
            {
                case FilterMethod.BitwiseDebounce:
                    return EvaluateState_BitwiseDebounce(handTag, trackingStateAction, device, require6DoF, frameTolerance, enableLog, ref state);
                case FilterMethod.KinematicDrift:
                    return EvaluateState_KinematicDrift(handTag, trackingStateAction, device, require6DoF, lostToleranceSeconds, maxHumanSpeed, minLinVelForTracking, posJumpMinDelta, posJumpMaxImpliedSpeed, enableLog, ref state);
                case FilterMethod.Combined:
                    bool isDebounced = EvaluateState_BitwiseDebounce(handTag, trackingStateAction, device, require6DoF, frameTolerance, enableLog, ref state);
                    bool isDriftFree = EvaluateState_KinematicDrift(handTag, trackingStateAction, device, require6DoF, lostToleranceSeconds, maxHumanSpeed, minLinVelForTracking, posJumpMinDelta, posJumpMaxImpliedSpeed, enableLog, ref state);
                    return isDebounced && isDriftFree;
                case FilterMethod.None:
                default:
                    return true;
            }
        }

        /// <summary>
        /// Bitwise history mask debounce filter across consecutive frames.
        /// </summary>
        public static bool EvaluateState_BitwiseDebounce(
            string handTag,
            InputActionProperty trackingStateAction,
            XRController device,
            bool require6DoF,
            int frameTolerance,
            bool enableLog,
            ref ControllerTrackingState state)
        {
            int rawDebounceState = GetRawTrackingState(trackingStateAction, device);
            bool isRawTracked = require6DoF ? (rawDebounceState & 3) == 3 : rawDebounceState != 0;
            uint prevHistory = state.bitHistory;

            state.bitHistory = (state.bitHistory << 1) | (isRawTracked ? 1u : 0u);

            uint toleranceMask = frameTolerance >= 32 ? uint.MaxValue : (1u << frameTolerance) - 1;
            bool isDebounced = (state.bitHistory & toleranceMask) > 0;
            bool prevDebounced = (prevHistory & toleranceMask) > 0;

            if (enableLog && prevDebounced && !isDebounced && isRawTracked)
            {
                LogDebounceReject(handTag, rawDebounceState, state.bitHistory);
            }

            if (!isDebounced)
            {
                state.lastRejectReason = enableLog ? $"Debounce Reject (0x{state.bitHistory:X8})" : "Debounce Reject";
            }

            return isDebounced;
        }

        /// <summary>
        /// Kinematic drift, velocity freeze, and position jump rejection filter.
        /// </summary>
        public static bool EvaluateState_KinematicDrift(
            string handTag,
            InputActionProperty trackingStateAction,
            XRController device,
            bool require6DoF,
            float lostToleranceSeconds,
            float maxHumanSpeed,
            float minLinVelForTracking,
            float posJumpMinDelta,
            float posJumpMaxImpliedSpeed,
            bool enableLog,
            ref ControllerTrackingState state)
        {
            if (device == null)
            {
                state.lastRejectReason = "Device Disconnected";
                return false;
            }

            int rawState = GetRawTrackingState(trackingStateAction, device);
            bool isRawTracked = require6DoF ? (rawState & 3) == 3 : rawState != 0;

            if (!isRawTracked)
            {
                float prevLostTimer = state.lostTimer;
                state.lostTimer += Time.deltaTime;

                if (state.lostTimer > lostToleranceSeconds)
                {
                    state.lastRejectReason = enableLog
                        ? $"Drift Timeout ({state.lostTimer:F2}s > {lostToleranceSeconds:F2}s)"
                        : "Drift Timeout";
                    if (enableLog && prevLostTimer <= lostToleranceSeconds)
                    {
                        LogDriftTimeout(handTag, state.lostTimer, lostToleranceSeconds);
                    }
                    return false;
                }
                
                return state.isTrackedUIState;
            }

            bool justRecovered = state.lostTimer > 0f;
            state.lostTimer = 0f;

            // === Position Jump Detection (deltaPos / deltaT) ===
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
                        state.lastRejectReason = enableLog
                            ? $"Position Jump (delta {posDelta * 100f:F1}cm/{dtPos * 1000f:F0}ms = {impliedSpeed:F1}m/s)"
                            : "Position Jump";
                        if (enableLog && (Time.time - state.lastVelJumpLogTime > 0.5f))
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

            // === Linear & Angular Velocity Sanity Checks ===
            Vector3 linVel = GetLinearVelocity(device);
            Vector3 angVel = GetAngularVelocity(device);

            float linSpeed = linVel.magnitude;

            // Linear velocity freeze check
            if (linSpeed < minLinVelForTracking)
            {
                state.linVelLostTimer += Time.deltaTime;
                if (state.linVelLostTimer > lostToleranceSeconds)
                {
                    state.lastRejectReason = enableLog
                        ? $"LinVel Freeze ({linSpeed:F3}m/s < {minLinVelForTracking:F3}m/s for {state.linVelLostTimer:F2}s)"
                        : "LinVel Freeze";
                    return false;
                }
            }
            else
            {
                state.linVelLostTimer = 0f;
            }

            // High linear speed threshold check
            if (linSpeed > maxHumanSpeed)
            {
                state.lastRejectReason = enableLog
                    ? $"High Linear Speed Jump ({linSpeed:F1}m/s > {maxHumanSpeed:F1}m/s)"
                    : "High Linear Speed Jump";
                if (enableLog && (Time.time - state.lastVelJumpLogTime > 0.5f))
                {
                    state.lastVelJumpLogTime = Time.time;
                    LogVelocityJump(handTag, linVel, angVel, linSpeed, maxHumanSpeed, "Linear");
                }
                return false; 
            }

            return true;
        }

        /// <summary>
        /// Updates the warning hold timer and determines if the warning state is currently active.
        /// </summary>
        public static bool ProcessWarningTimer(
            XRController device,
            int rawState,
            bool isTracked,
            float warningMinDurationSeconds,
            float deltaTime,
            ref ControllerTrackingState state)
        {
            if (device != null)
            {
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
                state.warningTimer -= deltaTime;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Detects single swings and 3-swing combos based on linear acceleration.
        /// </summary>
        public static void DetectShakeWithAcceleration(
            Vector3 linAccel,
            float shakeAccelThreshold,
            float swingCooldownDuration,
            float maxComboInterval,
            ref ControllerTrackingState state,
            out bool isSingleSwing,
            out bool isTripleSwing,
            out bool isAnyShake)
        {
            isSingleSwing = false;
            isTripleSwing = false;
            isAnyShake = false;

            float accelMag = linAccel.magnitude;
            float currentTime = Time.time;

            if (accelMag >= shakeAccelThreshold && (currentTime - state.lastSwingTime > swingCooldownDuration))
            {
                state.lastSwingTime = currentTime;
                isSingleSwing = true;

                if (currentTime - state.swingHistory2 <= maxComboInterval)
                {
                    // 3-swing combo completed; reset history
                    state.swingHistory1 = -999f;
                    state.swingHistory2 = -999f;

                    isTripleSwing = true;
                    isAnyShake = true;
                }
                else
                {
                    state.swingHistory2 = state.swingHistory1;
                    state.swingHistory1 = currentTime;
                }
            }
        }

        #endregion

        #region Diagnostics & Logging

        public static void DispatchLog(string message, LogType logType, bool enableDebugLog, bool enableFileLog)
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

        public static void LogHandState(
            string handTag,
            XRController device,
            int rawState,
            bool isTracked,
            bool isWarning,
            float logIntervalSeconds,
            bool enableDebugLog,
            bool enableFileLog,
            Vector3 linVel,
            Vector3 angVel,
            Vector3 linAccel,
            Vector3 angAccel,
            ref ControllerTrackingState state,
            ref HandLogCache cache)
        {
            if (!enableDebugLog && !enableFileLog) return;

            HandLoggedState currentState = !isTracked ? HandLoggedState.LOST : (isWarning ? HandLoggedState.WARN : HandLoggedState.OK);

            bool stateChanged = currentState != cache.lastState;
            bool rawStateChanged = rawState != cache.lastRawState;
            bool timeIntervalPassed = logIntervalSeconds > 0f && (Time.time - cache.lastLogTime >= logIntervalSeconds);

            if (!cache.isInitialized || stateChanged || rawStateChanged || timeIntervalPassed)
            {
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
                        DispatchLog($"[XR] {handTag} INIT: {currentState} | raw: {GetRawStateString(rawState)} | {dynamicsStr} | cause: {cause}", LogType.Warning, enableDebugLog, enableFileLog);
                    }
                    else
                    {
                        DispatchLog($"[XR] {handTag} INIT: OK | raw: {GetRawStateString(rawState)} | {dynamicsStr}", LogType.Log, enableDebugLog, enableFileLog);
                    }
                    return;
                }

                cache.lastLogTime = Time.time;

                if (currentState == HandLoggedState.OK)
                {
                    DispatchLog($"[XR] {handTag} STATE: OK | raw: {GetRawStateString(rawState)} | {dynamicsStr}", LogType.Log, enableDebugLog, enableFileLog);
                }
                else
                {
                    string cause = GetStateCause(device, isTracked, isWarning, rawState, state.lastRejectReason);
                    DispatchLog($"[XR] {handTag} STATE: {currentState} | raw: {GetRawStateString(rawState)} | {dynamicsStr} | cause: {cause}", LogType.Warning, enableDebugLog, enableFileLog);
                }
            }

            cache.lastState = currentState;
            cache.lastRawState = rawState;
        }

        public static void LogVelocityJump(string handTag, Vector3 linVel, Vector3 angVel, float speed, float threshold, string type)
        {
            DispatchLog($"[XR] {handTag} VELOCITY JUMP ({type}) | speed: {speed:F1} > {threshold:F1} | linVel: {linVel.ToString("F2")}, angVel: {angVel.ToString("F2")}", LogType.Warning, true, true);
        }

        public static void LogPositionJump(string handTag, Vector3 from, Vector3 to, float delta, float dt, float impliedSpeed)
        {
            DispatchLog($"[XR] {handTag} POSITION JUMP | delta {delta * 100f:F1}cm in {dt * 1000f:F0}ms ({impliedSpeed:F1}m/s) | from: {from.ToString("F3")} to: {to.ToString("F3")}", LogType.Warning, true, true);
        }

        public static void LogDriftTimeout(string handTag, float lostTimer, float lostToleranceSeconds)
        {
            DispatchLog($"[XR] {handTag} DRIFT TIMEOUT | lost: {lostTimer:F2}s > {lostToleranceSeconds:F2}s", LogType.Warning, true, true);
        }

        public static void LogDebounceReject(string handTag, int rawState, uint bitHistory)
        {
            DispatchLog($"[XR] {handTag} DEBOUNCE REJECT | raw: {GetRawStateString(rawState)} | bitHistory: 0x{bitHistory:X8}", LogType.Warning, true, true);
        }

        public static string GetStateCause(XRController device, bool isTracked, bool isWarning, int rawState, string rejectReason)
        {
            if (device == null) return "Device Disconnected";
            if (rawState == 0) return "Hardware Tracking Lost";
            if ((rawState & 3) != 3) return "Pos or Rot Incomplete";
            if (!isTracked) return !string.IsNullOrEmpty(rejectReason) ? rejectReason : "Filter Rejected";
            if (isWarning) return "Warning Hold Active";
            return "Unknown";
        }

        public static string GetRawStateString(int rawState)
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
}
