using UTXR.Core;
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.LowLevel;
using InputCommonUsages = UnityEngine.InputSystem.CommonUsages;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace UTXR.Devices
{
    /// <summary>
    /// Memory state structure for XR Acceleration device (24 bytes).
    /// Holds instantaneous 3D linear and angular acceleration vectors.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XRAccelerationState : IInputStateTypeInfo
    {
        public FourCC format => new FourCC('X', 'A', 'C', 'L');

        [InputControl(name = "deviceAcceleration", layout = "Vector3", displayName = "Linear Acceleration")]
        public Vector3 deviceAcceleration;

        [InputControl(name = "deviceAngularAcceleration", layout = "Vector3", displayName = "Angular Acceleration")]
        public Vector3 deviceAngularAcceleration;
    }

    /// <summary>
    /// Custom virtual InputDevice registered into Unity Input System.
    /// Enables any hardware provider (Pico, Quest, HTC Vive, data gloves) to inject acceleration
    /// so that InputActions and CycloneDDS can sample it seamlessly.
    /// </summary>
    [InputControlLayout(stateType = typeof(XRAccelerationState), displayName = "XR Acceleration Device")]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public class XRAccelerationDevice : UnityEngine.InputSystem.InputDevice
    {
        public Vector3Control deviceAcceleration { get; private set; }
        public Vector3Control deviceAngularAcceleration { get; private set; }

        static XRAccelerationDevice()
        {
            RegisterLayout();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterLayout()
        {
            InputSystem.RegisterLayout<XRAccelerationDevice>("XRAccelerationDevice");
        }

        protected override void FinishSetup()
        {
            base.FinishSetup();
            deviceAcceleration = GetChildControl<Vector3Control>("deviceAcceleration");
            deviceAngularAcceleration = GetChildControl<Vector3Control>("deviceAngularAcceleration");
        }
    }

    /// <summary>
    /// Generic bridge component managing virtual XRAccelerationDevices in Unity Input System.
    /// Provides high-performance, Zero-GC queuing of hardware/native acceleration data,
    /// with built-in numerical-derivative fallback for non-native XR devices (Quest, Vive, Index, etc.).
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class XRAccelerationDeviceBridge : MonoBehaviour
    {
        private static XRAccelerationDeviceBridge _instance;
        public static XRAccelerationDeviceBridge Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<XRAccelerationDeviceBridge>();
                    if (_instance == null)
                    {
                        var go = new GameObject("[XRAccelerationDeviceBridge]");
                        _instance = go.AddComponent<XRAccelerationDeviceBridge>();
                        if (Application.isPlaying)
                        {
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return _instance;
            }
        }

        [Header("Generic XR Fallback")]
        [Tooltip("If no hardware-specific native provider (like Pico) pushes acceleration in a frame, automatically compute numerical acceleration from XRNode velocity (supports Quest, Vive, etc.).")]
        public bool enableGenericFallback = true;

        [Header("Diagnostics")]
        [Tooltip("Enable periodic debug logging")]
        public bool enableDebugLog = false;
        public float logIntervalSeconds = 1.0f;
        private float _lastLogTime = 0f;

        private XRAccelerationDevice _leftDevice;
        private XRAccelerationDevice _rightDevice;

        private XRAccelerationState _leftState;
        private XRAccelerationState _rightState;

        private bool _leftUpdatedThisFrame = false;
        private bool _rightUpdatedThisFrame = false;

        private Vector3 _lastLeftVel;
        private Vector3 _lastLeftAngVel;
        private Vector3 _lastRightVel;
        private Vector3 _lastRightAngVel;

        public Vector3 LeftLinearAccel => _leftState.deviceAcceleration;
        public Vector3 LeftAngularAccel => _leftState.deviceAngularAcceleration;
        public Vector3 RightLinearAccel => _rightState.deviceAcceleration;
        public Vector3 RightAngularAccel => _rightState.deviceAngularAcceleration;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            var _ = Instance;
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = this;
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            EnsureDevicesCreated();
        }

        private void OnEnable()
        {
            EnsureDevicesCreated();
        }

        private void OnDisable()
        {
            RemoveDevices();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            RemoveDevices();
        }

        public void EnsureDevicesCreated()
        {
            if (_leftDevice == null || !_leftDevice.added)
            {
                _leftDevice = InputSystem.AddDevice<XRAccelerationDevice>("XRAcceleration_Left");
                InputSystem.SetDeviceUsage(_leftDevice, InputCommonUsages.LeftHand);
            }

            if (_rightDevice == null || !_rightDevice.added)
            {
                _rightDevice = InputSystem.AddDevice<XRAccelerationDevice>("XRAcceleration_Right");
                InputSystem.SetDeviceUsage(_rightDevice, InputCommonUsages.RightHand);
            }
        }

        private void RemoveDevices()
        {
            if (_leftDevice != null && _leftDevice.added)
            {
                InputSystem.RemoveDevice(_leftDevice);
                _leftDevice = null;
            }

            if (_rightDevice != null && _rightDevice.added)
            {
                InputSystem.RemoveDevice(_rightDevice);
                _rightDevice = null;
            }
        }

        /// <summary>
        /// External provider API: push acceleration for either left or right hand.
        /// Zero GC, non-blocking, immediate queue state event.
        /// </summary>
        public static void UpdateAcceleration(bool isLeft, in Vector3 linearAccel, in Vector3 angularAccel)
        {
            Instance.SetAcceleration(isLeft, in linearAccel, in angularAccel);
        }

        public void SetAcceleration(bool isLeft, in Vector3 linearAccel, in Vector3 angularAccel)
        {
            EnsureDevicesCreated();

            if (isLeft)
            {
                _leftState.deviceAcceleration = linearAccel;
                _leftState.deviceAngularAcceleration = angularAccel;
                _leftUpdatedThisFrame = true;

                if (_leftDevice != null && _leftDevice.added)
                {
                    InputSystem.QueueStateEvent(_leftDevice, _leftState);
                }
            }
            else
            {
                _rightState.deviceAcceleration = linearAccel;
                _rightState.deviceAngularAcceleration = angularAccel;
                _rightUpdatedThisFrame = true;

                if (_rightDevice != null && _rightDevice.added)
                {
                    InputSystem.QueueStateEvent(_rightDevice, _rightState);
                }
            }
        }

        private void Update()
        {
            if (enableGenericFallback)
            {
                float dt = Time.deltaTime;
                if (dt > 0.0001f)
                {
                    if (!_leftUpdatedThisFrame)
                    {
                        ComputeFallbackAcceleration(XRNode.LeftHand, dt, ref _lastLeftVel, ref _lastLeftAngVel, out Vector3 lin, out Vector3 ang);
                        _leftState.deviceAcceleration = lin;
                        _leftState.deviceAngularAcceleration = ang;
                        if (_leftDevice != null && _leftDevice.added)
                        {
                            InputSystem.QueueStateEvent(_leftDevice, _leftState);
                        }
                    }

                    if (!_rightUpdatedThisFrame)
                    {
                        ComputeFallbackAcceleration(XRNode.RightHand, dt, ref _lastRightVel, ref _lastRightAngVel, out Vector3 lin, out Vector3 ang);
                        _rightState.deviceAcceleration = lin;
                        _rightState.deviceAngularAcceleration = ang;
                        if (_rightDevice != null && _rightDevice.added)
                        {
                            InputSystem.QueueStateEvent(_rightDevice, _rightState);
                        }
                    }
                }
            }

            _leftUpdatedThisFrame = false;
            _rightUpdatedThisFrame = false;

            if (enableDebugLog && Time.time - _lastLogTime >= logIntervalSeconds)
            {
                _lastLogTime = Time.time;
                Debug.Log($"[XRAccelerationDeviceBridge] Left: Linear={_leftState.deviceAcceleration:F2}, Angular={_leftState.deviceAngularAcceleration:F2} | Right: Linear={_rightState.deviceAcceleration:F2}, Angular={_rightState.deviceAngularAcceleration:F2}");
            }
        }

        private void ComputeFallbackAcceleration(XRNode node, float dt, ref Vector3 lastVel, ref Vector3 lastAngVel, out Vector3 linAccel, out Vector3 angAccel)
        {
            linAccel = Vector3.zero;
            angAccel = Vector3.zero;

            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            if (device.TryGetFeatureValue(XRCommonUsages.deviceVelocity, out Vector3 curVel))
            {
                linAccel = (curVel - lastVel) / dt;
                lastVel = curVel;
            }

            if (device.TryGetFeatureValue(XRCommonUsages.deviceAngularVelocity, out Vector3 curAngVel))
            {
                angAccel = (curAngVel - lastAngVel) / dt;
                lastAngVel = curAngVel;
            }
        }
    }
}

