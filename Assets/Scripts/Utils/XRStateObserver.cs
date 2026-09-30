using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace Apex.Dashboard
{
    /// <summary>
    /// XRStateObserver observes and broadcasts HMD (Headset) and global XR system states.
    /// The event names directly correspond to the underlying Unity APIs they listen to.
    /// </summary>
    public class XRStateObserver : MonoBehaviour
    {
        private const string TAG = "[XRStateObserver] ";

        [Header("XRInputSubsystem Events")]
        [Tooltip("Triggered when XRInputSubsystem.trackingOriginUpdated occurs (recentered by long pressing Pico/Quest Home button).")]
        public UnityEvent onTrackingOriginUpdated;

        [Header("CommonUsages.userPresence Events")]
        [Tooltip("Triggered when CommonUsages.userPresence on the HMD becomes true.")]
        public UnityEvent onUserPresenceTrue;
        [Tooltip("Triggered when CommonUsages.userPresence on the HMD becomes false.")]
        public UnityEvent onUserPresenceFalse;

        [Header("CommonUsages.isTracked (HMD) Events")]
        [Tooltip("Triggered when CommonUsages.isTracked on the HMD becomes true.")]
        public UnityEvent onHmdIsTrackedTrue;
        [Tooltip("Triggered when CommonUsages.isTracked on the HMD becomes false.")]
        public UnityEvent onHmdIsTrackedFalse;

        [Header("OnApplicationFocus Events")]
        [Tooltip("Triggered when OnApplicationFocus(true) is called.")]
        public UnityEvent onApplicationFocusTrue;
        [Tooltip("Triggered when OnApplicationFocus(false) is called (system overlay/menu opened).")]
        public UnityEvent onApplicationFocusFalse;

        [Header("OnApplicationQuit Event")]
        [Tooltip("Triggered when OnApplicationQuit is called.")]
        public UnityEvent onApplicationQuit;

        [Header("Configuration")]
        [Tooltip("Ignore tracking origin updates for this many seconds after startup/scene load to avoid false triggers during initialization.")]
        public float ignoreTrackingOriginDelayAfterStartup = 5.0f;

        private float _startupTime;
        private XRInputSubsystem _inputSubsystem;

        // Internal HMD Tracking State
        private InputDevice _hmdDevice;
        private bool _hasHmd = false;
        private bool? _lastUserPresence = null;
        private bool? _lastHmdTracked = null;
        private bool _lastAppFocus = true;

        private void Start()
        {
            _startupTime = Time.time;
            _lastAppFocus = Application.isFocused;
            
            string startMsg = $"{TAG}Observer started. Recenter ignore period: {ignoreTrackingOriginDelayAfterStartup}s.";
            Logger.LogApp(startMsg);
            Debug.Log(startMsg);

            InitializeConnectedDevices();

            // Keep OpenXR in LocalFloor space permanently
            try
            {
                UnityEngine.XR.OpenXR.OpenXRSettings.SetAllowRecentering(true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"{TAG}Failed to set OpenXR SetAllowRecentering: {ex.Message}");
            }
        }

        private void OnEnable()
        {
            InputDevices.deviceConnected += OnDeviceConnected;
            InputDevices.deviceDisconnected += OnDeviceDisconnected;
        }

        private void OnDisable()
        {
            if (_inputSubsystem != null)
            {
                _inputSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                _inputSubsystem = null;
            }

            InputDevices.deviceConnected -= OnDeviceConnected;
            InputDevices.deviceDisconnected -= OnDeviceDisconnected;
        }

        private void Update()
        {
            CheckSubsystem();
            MonitorHmdState();
        }

        private void CheckSubsystem()
        {
            if (_inputSubsystem == null)
            {
                var subsystems = new List<XRInputSubsystem>();
                SubsystemManager.GetSubsystems(subsystems);
                if (subsystems.Count > 0)
                {
                    _inputSubsystem = subsystems[0];
                    _inputSubsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
                    _inputSubsystem.trackingOriginUpdated += OnTrackingOriginUpdated;

                    string regMsg = $"{TAG}Acquired XRInputSubsystem: {_inputSubsystem.SubsystemDescriptor.id}";
                    Logger.LogApp(regMsg);
                    Debug.Log(regMsg);
                }
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus != _lastAppFocus)
            {
                _lastAppFocus = hasFocus;
                string msg = $"{TAG}OnApplicationFocus: {hasFocus}";
                Logger.LogApp(msg);
                Debug.Log(msg);

                if (hasFocus)
                    onApplicationFocusTrue?.Invoke();
                else
                    onApplicationFocusFalse?.Invoke();
            }
        }

        private void OnApplicationQuit()
        {
            string msg = $"{TAG}OnApplicationQuit";
            onApplicationQuit?.Invoke();
        }

        private void InitializeConnectedDevices()
        {
            var allDevices = new List<InputDevice>();
            InputDevices.GetDevices(allDevices);
            foreach (var device in allDevices)
            {
                OnDeviceConnected(device);
            }
        }

        private void OnDeviceConnected(InputDevice device)
        {
            if ((device.characteristics & InputDeviceCharacteristics.HeadMounted) != 0)
            {
                _hmdDevice = device;
                _hasHmd = true;
                string msg = $"{TAG}HMD Connected (characteristics match HeadMounted): {device.name}";
                Logger.LogApp(msg);
                Debug.Log(msg);
            }
        }

        private void OnDeviceDisconnected(InputDevice device)
        {
            if ((device.characteristics & InputDeviceCharacteristics.HeadMounted) != 0)
            {
                _hasHmd = false;
                _lastUserPresence = null;
                _lastHmdTracked = null;
                string msg = $"{TAG}HMD Disconnected: {device.name}";
                Logger.LogApp(msg);
                Debug.Log(msg);
            }
        }

        private void MonitorHmdState()
        {
            if (!_hasHmd || !_hmdDevice.isValid) return;

            // 1. Monitor CommonUsages.userPresence (Wearing / Taken off)
            if (_hmdDevice.TryGetFeatureValue(CommonUsages.userPresence, out bool userPresence))
            {
                if (_lastUserPresence == null || userPresence != _lastUserPresence.Value)
                {
                    _lastUserPresence = userPresence;
                    string msg = $"{TAG}CommonUsages.userPresence: {userPresence}";
                    Logger.LogApp(msg);
                    Debug.Log(msg);

                    if (userPresence)
                        onUserPresenceTrue?.Invoke();
                    else
                        onUserPresenceFalse?.Invoke();
                }
            }

            // 2. Monitor CommonUsages.isTracked (6DoF Acquired / Lost)
            if (_hmdDevice.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked))
            {
                if (_lastHmdTracked == null || isTracked != _lastHmdTracked.Value)
                {
                    _lastHmdTracked = isTracked;
                    string msg = $"{TAG}HMD CommonUsages.isTracked: {isTracked}";
                    Logger.LogApp(msg);
                    Debug.Log(msg);

                    if (isTracked)
                        onHmdIsTrackedTrue?.Invoke();
                    else
                        onHmdIsTrackedFalse?.Invoke();
                }
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem)
        {
            float elapsed = Time.time - _startupTime;
            if (elapsed < ignoreTrackingOriginDelayAfterStartup)
            {
                string ignoreMsg = $"{TAG}Ignored trackingOriginUpdated during startup period. Elapsed: {elapsed:F2}s / Required: {ignoreTrackingOriginDelayAfterStartup:F2}s";
                Logger.LogApp(ignoreMsg);
                Debug.Log(ignoreMsg);
                return;
            }

            string triggerMsg = $"{TAG}trackingOriginUpdated event detected! Triggering event. Elapsed: {elapsed:F2}s";
            Logger.LogApp(triggerMsg);
            Debug.Log(triggerMsg);
            onTrackingOriginUpdated?.Invoke();
        }
    }
}
