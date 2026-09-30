using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace Apex.Utils
{
    // Native PICO Data Structures for Controller Hardware IMU Sensor Data
    [StructLayout(LayoutKind.Sequential)]
    public struct PxrVector3f
    {
        public float x;
        public float y;
        public float z;
        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxrVector4f
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxrPosef
    {
        public PxrVector4f orientation;
        public PxrVector3f position;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxrSensorState
    {
        public int status;
        public PxrPosef pose;
        private PxrPosef globalPose;
        public PxrVector3f angularVelocity;
        public PxrVector3f linearVelocity;
        public PxrVector3f angularAcceleration; // 物理硬件原生角加速度
        public PxrVector3f linearAcceleration;  // 物理硬件原生线加速度
        public UInt64 poseTimeStampNs;
        private int viewNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PxrControllerTracking
    {
        public PxrSensorState localControllerPose;
        public PxrSensorState globalControllerPose;
    }

    /// <summary>
    /// Dual Native Plugin Wrapper: Supports both PICO Android Native (PxrPlatform) and Editor LivePreview (PxrLivePreview).
    /// </summary>
    public static class PICOControllerNativePlugin
    {
        private const string PXR_PLATFORM_DLL = "PxrPlatform";
        private const string PXR_LIVE_PREVIEW_DLL = "PxrLivePreview";

        private static bool _editorTested = false;
        private static bool _editorEntrySupported = false;

        private const int MAX_FAIL_COUNT = 3;
        private static int _failureCount = 0;
        private static bool _isCircuitBroken = false;

        public static bool IsCircuitBroken => _isCircuitBroken;

        public static void ResetCircuitBreaker()
        {
            _failureCount = 0;
            _isCircuitBroken = false;
            _editorTested = false;
            _editorEntrySupported = false;
        }

        private static void RecordFailure(string reason)
        {
            _failureCount++;
            if (_failureCount >= MAX_FAIL_COUNT)
            {
                _isCircuitBroken = true;
                Debug.LogWarning($"[PICOControllerNativePlugin] Native tracking interface failed {_failureCount} times ({reason}). Circuit broken; future frames will bypass Native directly.");
            }
        }

        [DllImport(PXR_PLATFORM_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pxr_GetControllerTrackingState")]
        private static extern int Pxr_GetControllerTrackingState_Android(uint deviceID, double predictTime, ref PxrControllerTracking tracking);

        [DllImport(PXR_LIVE_PREVIEW_DLL, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Pxr_GetControllerTrackingState")]
        private static extern int Pxr_GetControllerTrackingState_LivePreview(uint deviceID, double predictTime, ref PxrControllerTracking tracking);

        public static int GetControllerTrackingState(uint deviceID, double predictTime, ref PxrControllerTracking tracking)
        {
            if (_isCircuitBroken) return -102;

#if UNITY_EDITOR
            if (!_editorTested)
            {
                _editorTested = true;
                try
                {
                    int res = Pxr_GetControllerTrackingState_LivePreview(deviceID, predictTime, ref tracking);
                    _editorEntrySupported = (res == 0);
                    if (!_editorEntrySupported)
                    {
                        RecordFailure("LivePreview returned " + res);
                    }
                    return res;
                }
                catch (Exception ex)
                {
                    _editorEntrySupported = false;
                    RecordFailure("LivePreview entry exception: " + ex.Message);
                    return -100;
                }
            }

            if (!_editorEntrySupported) return -100;

            try
            {
                int res = Pxr_GetControllerTrackingState_LivePreview(deviceID, predictTime, ref tracking);
                if (res != 0) RecordFailure("LivePreview error " + res);
                else _failureCount = 0;
                return res;
            }
            catch (Exception ex)
            {
                RecordFailure("LivePreview call exception: " + ex.Message);
                return -100;
            }
#else
            try
            {
                int res = Pxr_GetControllerTrackingState_Android(deviceID, predictTime, ref tracking);
                if (res != 0)
                {
                    RecordFailure("Android native returned " + res);
                }
                else
                {
                    _failureCount = 0;
                }
                return res;
            }
            catch (Exception ex)
            {
                RecordFailure("Android native exception: " + ex.Message);
                return -101;
            }
#endif
        }
    }

    /// <summary>
    /// Bridge component that directly queries PICO Native C Runtime (PxrPlatform / PxrLivePreview)
    /// and injects acceleration directly into user-configured binding paths WITHOUT creating or simulating any new devices.
    /// Features Zero-GC Control Caching for maximum runtime performance.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class XRAccelerationBridge : MonoBehaviour
    {
        [Header("Left Hand Binding Paths")]
        [Tooltip("Binding path for Left Hand Linear Acceleration")]
        public string leftLinearAccelPath = "<PICO4UltraController>{LeftHand}/deviceAcceleration";

        [Tooltip("Binding path for Left Hand Angular Acceleration")]
        public string leftAngularAccelPath = "<PICO4UltraController>{LeftHand}/deviceAngularAcceleration";

        [Header("Right Hand Binding Paths")]
        [Tooltip("Binding path for Right Hand Linear Acceleration")]
        public string rightLinearAccelPath = "<PICO4UltraController>{RightHand}/deviceAcceleration";

        [Tooltip("Binding path for Right Hand Angular Acceleration")]
        public string rightAngularAccelPath = "<PICO4UltraController>{RightHand}/deviceAngularAcceleration";

        [Header("Debug & Logging Settings")]
        [Tooltip("Print Vector3 acceleration logs to Unity Console")]
        public bool enableDebugLog = true;

        [Tooltip("Write Vector3 acceleration logs to disk file via Logger.LogApp")]
        public bool enableFileLog = false;

        [Tooltip("Log interval in seconds (default 1.0s)")]
        public float logIntervalSeconds = 1.0f;

        private float _lastLogTime = 0f;

        private Vector3 _lastLeftVel;
        private Vector3 _lastLeftAngVel;
        private Vector3 _lastRightVel;
        private Vector3 _lastRightAngVel;

        private Vector3 _leftLinearAccel;
        private Vector3 _leftAngularAccel;
        private Vector3 _rightLinearAccel;
        private Vector3 _rightAngularAccel;

        public Vector3 LeftLinearAccel  => _leftLinearAccel;
        public Vector3 LeftAngularAccel => _leftAngularAccel;
        public Vector3 RightLinearAccel  => _rightLinearAccel;
        public Vector3 RightAngularAccel => _rightAngularAccel;

        // Cached Control references for Zero-GC update loop
        private Vector3Control _leftLinearControl;
        private Vector3Control _leftAngularControl;
        private Vector3Control _rightLinearControl;
        private Vector3Control _rightAngularControl;

        private void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            RefreshControlCache();
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.ConfigurationChanged)
            {
                RefreshControlCache();
            }
        }

        public void RefreshControlCache()
        {
            _leftLinearControl = GetFirstVector3Control(leftLinearAccelPath);
            _leftAngularControl = GetFirstVector3Control(leftAngularAccelPath);
            _rightLinearControl = GetFirstVector3Control(rightLinearAccelPath);
            _rightAngularControl = GetFirstVector3Control(rightAngularAccelPath);
        }

        private Vector3Control GetFirstVector3Control(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var controls = InputSystem.FindControls(path);
                for (int i = 0; i < controls.Count; i++)
                {
                    if (controls[i] is Vector3Control v3Control) return v3Control;
                }
            }
            catch { }
            return null;
        }

        private void Update()
        {
            UpdateHandHardwareAcceleration(0, XRNode.LeftHand, _leftLinearControl, _leftAngularControl, ref _lastLeftVel, ref _lastLeftAngVel, out _leftLinearAccel, out _leftAngularAccel);
            UpdateHandHardwareAcceleration(1, XRNode.RightHand, _rightLinearControl, _rightAngularControl, ref _lastRightVel, ref _lastRightAngVel, out _rightLinearAccel, out _rightAngularAccel);

            if ((enableDebugLog || enableFileLog) && (Time.time - _lastLogTime >= logIntervalSeconds))
            {
                _lastLogTime = Time.time;
                DispatchLogs();
            }
        }

        private void UpdateHandHardwareAcceleration(uint controllerID, XRNode node, Vector3Control linearCtrl, Vector3Control angularCtrl, ref Vector3 lastVel, ref Vector3 lastAngVel, out Vector3 rawAccel, out Vector3 rawAngAccel)
        {
            rawAccel = Vector3.zero;
            rawAngAccel = Vector3.zero;

            int result = -1;
            PxrControllerTracking tracking = default;

            // 失败熔断检查：若已经熔断（例如驱动不支持），直接走 XRNode 备用回退，后续帧不再尝试穿透 Native
            if (!PICOControllerNativePlugin.IsCircuitBroken)
            {
                result = PICOControllerNativePlugin.GetControllerTrackingState(controllerID, 0.0, ref tracking);
            }

            if (result == 0)
            {
                // PICO 物理硬件 IMU 原生线加速度与角加速度
                rawAccel = tracking.localControllerPose.linearAcceleration.ToVector3();
                rawAngAccel = tracking.localControllerPose.angularAcceleration.ToVector3();
            }
            else
            {
                // LivePreview PC 串流或 Native 熔断/不支持时的备用回退
                UnityEngine.XR.InputDevice xrDevice = InputDevices.GetDeviceAtXRNode(node);
                if (xrDevice.isValid)
                {
                    if (xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceVelocity, out Vector3 curVel))
                    {
                        rawAccel = (curVel - lastVel) / Time.deltaTime;
                        lastVel = curVel;
                    }
                    if (xrDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceAngularVelocity, out Vector3 curAngVel))
                    {
                        rawAngAccel = (curAngVel - lastAngVel) / Time.deltaTime;
                        lastAngVel = curAngVel;
                    }
                }
            }

            if (linearCtrl != null) InputSystem.QueueDeltaStateEvent(linearCtrl, rawAccel);
            if (angularCtrl != null) InputSystem.QueueDeltaStateEvent(angularCtrl, rawAngAccel);
        }

        private void DispatchLogs()
        {
            if (!enableDebugLog && !enableFileLog) return;

            string msg = $"[XRAccelerationBridge] " +
                         $"  Left  -> LinearAccel: {_leftLinearAccel.ToString("F3")} m/s², AngularAccel: {_leftAngularAccel.ToString("F3")} rad/s²;" +
                         $"  Right -> LinearAccel: {_rightLinearAccel.ToString("F3")} m/s², AngularAccel: {_rightAngularAccel.ToString("F3")} rad/s²";

            if (enableDebugLog) Debug.Log(msg);
            if (enableFileLog) Logger.LogApp(msg);
        }
    }
}
