using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Management;

namespace UTXR.Devices
{
    /// <summary>
    /// Unity host for explicit Controller/Hand selection. Owns the session and Inspector wiring;
    /// providers own sampling only. Does not change headset settings or initialize XR subsystems.
    /// </summary>
    [DisallowMultipleComponent]
    public class XRGeneralDeviceStateMonitor : MonoBehaviour, IXRDeviceState
    {
        [Header("Event Dispatcher")]
        [Tooltip("Dispatcher responsible for status changes and gesture events")]
        [SerializeField] private XRDeviceEventDispatcher eventDispatcher;

        [Header("Filtering Settings")]
        public FilterMethod filterMethod = FilterMethod.Combined;
        [Tooltip("Require 6DoF (Position + Rotation) tracking state. If false, 3DoF rotation-only is accepted.")]
        private bool require6DoF = true;
        private int frameTolerance = 20;
        private float lostToleranceSeconds = 0.1f;
        private float maxHumanSpeed = 8.0f;          // Upper bound for linear speed sanity check
        private float minLinVelForTracking = 0.001f; // Minimum linear speed threshold
        private float posJumpMinDelta = 0.015f;      // 1.5cm position jump threshold
        private float posJumpMaxImpliedSpeed = 4.0f; // Implied speed threshold (m/s)
        private float warningMinDurationSeconds = 2.0f;
        private float warnCooldownSeconds = 1.0f;

        [Header("Application Input Mode")]
        [Tooltip("Startup selection. During play, use UseControllers/UseHands or SetMode explicitly; editing this field does not rebind a running provider.")]
        [SerializeField] private XRInputMode inputMode = XRInputMode.Controller;
        [FormerlySerializedAs("enableShakeDetection")]
        [SerializeField] private bool enableSwingDetection = true;
        [Tooltip("Optional interaction roots controlled exclusively by this monitor. Empty means state monitoring only.")]
        [SerializeField] private GameObject[] controllerInteractionRoots = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] handInteractionRoots = Array.Empty<GameObject>();

        [Header("Swing Detection")]
        [Tooltip("Position change per second (m/s) required to trigger a swing. Must fall below this threshold before another swing.")]
        private float swingSpeedThreshold = 1.5f;
        [Tooltip("Minimum time (seconds) between swings")]
        private float swingCooldownDuration = 0.15f;
        [Tooltip("Maximum window time (seconds) to complete 3 swings")]
        private float maxComboInterval = 1.0f;

        [Header("Logging Settings")]
        [Tooltip("Print tracking logs to Unity Console")]
        public bool enableDebugLog = true;
        [Tooltip("Write tracking logs to disk file via Logger.LogApp")]
        public bool enableFileLog = false;
        [Tooltip("Periodic continuous logging interval in seconds (0 = log only on state change)")]
        private float logIntervalSeconds = 1.0f;

        private XRDeviceStateSession _session;
        private XRDeviceState _lastLeft, _lastRight;
        private bool _hasLeftState, _hasRightState, _suppressWarning;
        private float _lastWarnTime = float.NegativeInfinity, _lastLogTime;
        private static readonly XRProviderStatus DisabledStatus = new XRProviderStatus(XRProviderStatusCode.Disabled, "Monitor is disabled.");
        private XRProviderStatus _inactiveStatus = DisabledStatus;

        public XRInputMode Mode => _session?.Mode ?? inputMode;
        public XRProviderStatus ProviderStatus => _session?.ProviderStatus ?? _inactiveStatus;
        public XRDeviceEventDispatcher EventDispatcher => eventDispatcher;
        public bool IsLeftTracked => GetState(XRSide.Left).IsTracked;
        public bool IsRightTracked => GetState(XRSide.Right).IsTracked;
        public bool IsLeftWarning => GetState(XRSide.Left).IsWarning;
        public bool IsRightWarning => GetState(XRSide.Right).IsWarning;
        public event Action<XRInputMode> ModeChanged;
        public event Action<XRProviderStatus> ProviderStatusChanged;
        public event Action<XRSide, XRDeviceState> TrackingChanged;

        public UnityEvent OnLeftTracked => eventDispatcher.OnLeftTracked;
        public UnityEvent OnLeftUntracked => eventDispatcher.OnLeftUntracked;
        public UnityEvent OnRightTracked => eventDispatcher.OnRightTracked;
        public UnityEvent OnRightUntracked => eventDispatcher.OnRightUntracked;
        public UnityEvent OnTrackingWarning => eventDispatcher.OnTrackingWarning;
        public UnityEvent OnLeftSwing => eventDispatcher.OnLeftSwing;
        public UnityEvent OnRightSwing => eventDispatcher.OnRightSwing;
        public UnityEvent OnLeftTripleSwing => eventDispatcher.OnLeftTripleSwing;
        public UnityEvent OnRightTripleSwing => eventDispatcher.OnRightTripleSwing;
        public UnityEvent OnAnyShake => eventDispatcher.OnAnyShake;

        private void OnEnable()
        {
            if (eventDispatcher == null)
            {
                _inactiveStatus = new XRProviderStatus(XRProviderStatusCode.InvalidConfiguration,
                    "Assign Event Dispatcher explicitly. No component will be searched for or created.");
                Debug.LogError($"[XRDeviceState] {_inactiveStatus}", this);
                enabled = false;
                return;
            }
            try
            {
                ValidateInteractionRoots();
                _session = new XRDeviceStateSession(CreateProvider, inputMode, CreateSettings());
            }
            catch (ArgumentException exception)
            {
                _inactiveStatus = new XRProviderStatus(XRProviderStatusCode.InvalidConfiguration, exception.Message);
                Debug.LogException(exception, this);
                enabled = false;
                return;
            }
            _hasLeftState = _hasRightState = false;
            _suppressWarning = true;
            _session.ModeChanged += HandleModeChanged;
            _session.ProviderStatusChanged += HandleProviderStatusChanged;
            _session.TrackingChanged += HandleStateChanged;
            _session.RefreshProvider();
            ApplyInteractionMode(inputMode);
        }

        private void OnDisable()
        {
            if (_session == null) return;
            _suppressWarning = true;
            inputMode = _session.Mode;
            try { _session.Dispose(); }
            finally
            {
                _session.ModeChanged -= HandleModeChanged;
                _session.ProviderStatusChanged -= HandleProviderStatusChanged;
                _session.TrackingChanged -= HandleStateChanged;
                _session = null;
                _inactiveStatus = DisabledStatus;
                SetRootsActive(controllerInteractionRoots, false);
                SetRootsActive(handInteractionRoots, false);
            }
        }

        private void Update()
        {
            if (_session == null) return;
            _session.Sample(Time.unscaledDeltaTime);
            DispatchGestures(XRSide.Left);
            DispatchGestures(XRSide.Right);
            _suppressWarning = false;
            if ((enableDebugLog || enableFileLog) && logIntervalSeconds > 0f &&
                Time.unscaledTime - _lastLogTime >= logIntervalSeconds)
            {
                _lastLogTime = Time.unscaledTime;
                LogState(XRSide.Left, GetState(XRSide.Left));
                LogState(XRSide.Right, GetState(XRSide.Right));
            }
        }

        public XRProviderStatus SetMode(XRInputMode mode)
        {
            RequireSession();
            ValidateInteractionRoots();
            return _session.SetMode(mode);
        }
        [ContextMenu("Input Mode/Use Controllers")]
        public void UseControllers() => SetMode(XRInputMode.Controller);
        [ContextMenu("Input Mode/Use Hands")]
        public void UseHands() => SetMode(XRInputMode.Hand);
        public XRProviderStatus RefreshProvider()
        {
            RequireSession();
            ValidateInteractionRoots();
            _suppressWarning = true;
            return _session.RefreshProvider();
        }
        [ContextMenu("Input Mode/Refresh Selected Provider")]
        public void RefreshSelectedProvider() => RefreshProvider();
        public XRDeviceState GetState(XRSide side)
        {
            if (side != XRSide.Left && side != XRSide.Right) throw new ArgumentOutOfRangeException(nameof(side));
            return _session != null ? _session.GetState(side) :
                new XRDeviceState(new XRDeviceSample(_inactiveStatus, InputTrackingState.None, false), false);
        }
        public int GetRawLeftTrackingState() => (int)GetState(XRSide.Left).RawTrackingState;
        public int GetRawRightTrackingState() => (int)GetState(XRSide.Right).RawTrackingState;
        public int GetLeftTrackingState() => IsLeftTracked ? GetRawLeftTrackingState() : 0;
        public int GetRightTrackingState() => IsRightTracked ? GetRawRightTrackingState() : 0;

        private void RequireSession()
        {
            if (_session == null) throw new InvalidOperationException("Enable and configure the XR device state monitor before changing its provider.");
        }

        private XRDeviceStateSettings CreateSettings() => new XRDeviceStateSettings
        {
            Filter = filterMethod, Require6DoF = require6DoF, FrameTolerance = frameTolerance,
            LostToleranceSeconds = lostToleranceSeconds, MaxHumanSpeed = maxHumanSpeed,
            MinLinearVelocity = minLinVelForTracking, PositionJumpMinDelta = posJumpMinDelta,
            PositionJumpMaxSpeed = posJumpMaxImpliedSpeed, WarningHoldSeconds = warningMinDurationSeconds,
            EnableSwingDetection = enableSwingDetection, SwingSpeedThreshold = swingSpeedThreshold,
            SwingCooldownSeconds = swingCooldownDuration, TripleSwingWindowSeconds = maxComboInterval
        };

        private IXRDeviceStateProvider CreateProvider(XRInputMode mode)
        {
            switch (mode)
            {
                case XRInputMode.Controller:
                    return new XRControllerStateProvider();
                case XRInputMode.Hand:
                    var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                    var loader = manager != null ? manager.activeLoader : null;
                    return new XRHandStateProvider(loader != null ? loader.GetLoadedSubsystem<XRHandSubsystem>() : null);
                default: throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private void HandleModeChanged(XRInputMode mode)
        {
            inputMode = mode;
            _suppressWarning = true;
            _lastWarnTime = float.NegativeInfinity;
            ApplyInteractionMode(mode);
            eventDispatcher.DispatchModeChanged(mode);
            ModeChanged?.Invoke(mode);
        }
        private void HandleProviderStatusChanged(XRProviderStatus status)
        {
            eventDispatcher.DispatchProviderStatus(status);
            ProviderStatusChanged?.Invoke(status);
        }
        private void HandleStateChanged(XRSide side, XRDeviceState state)
        {
            bool isLeft = side == XRSide.Left;
            var previous = isLeft ? _lastLeft : _lastRight;
            bool initialized = isLeft ? _hasLeftState : _hasRightState;
            if (!state.Status.IsReady && state.Status.Code != XRProviderStatusCode.Disabled &&
                (!initialized || !state.Status.Equals(previous.Status)))
                Debug.LogError($"[XRDeviceState] {Mode} {side}: {state.Status}", this);
            if (initialized && previous.IsTracked != state.IsTracked)
                eventDispatcher.DispatchTrackingChanged(isLeft, state.IsTracked);
            eventDispatcher.DispatchTrackingState(isLeft, state.IsTracked, state.IsWarning);
            if (initialized && !_suppressWarning &&
                previous.IsTracked && !previous.IsWarning && (!state.IsTracked || state.IsWarning) &&
                Time.unscaledTime - _lastWarnTime >= warnCooldownSeconds)
            {
                _lastWarnTime = Time.unscaledTime;
                eventDispatcher.DispatchTrackingWarning();
            }
            if (isLeft) { _lastLeft = state; _hasLeftState = true; }
            else { _lastRight = state; _hasRightState = true; }
            if (enableDebugLog || enableFileLog) LogState(side, state);
            TrackingChanged?.Invoke(side, state);
        }
        private void DispatchGestures(XRSide side)
        {
            XRGesture gestures = GetState(side).Gestures;
            if ((gestures & XRGesture.Swing) != 0) eventDispatcher.DispatchSwing(side == XRSide.Left);
            if ((gestures & XRGesture.TripleSwing) != 0) eventDispatcher.DispatchTripleSwing(side == XRSide.Left);
            if (gestures != XRGesture.None) eventDispatcher.DispatchAnyShake();
        }
        private void LogState(XRSide side, XRDeviceState state)
        {
            string linear = state.Raw.LinearVelocity.HasValue ? state.Raw.LinearVelocity.Value.ToString("F3") : "unavailable";
            string angular = state.Raw.AngularVelocity.HasValue ? state.Raw.AngularVelocity.Value.ToString("F3") : "unavailable";
            string message = $"[XRDeviceState] {Mode} {side}: {state.Status}, tracked={state.IsTracked}, rawTracked={state.Raw.IsTracked}, warning={state.IsWarning}, raw={state.RawTrackingState}, linearVelocity={linear} m/s, angularVelocity={angular} rad/s";
            if (enableDebugLog) Debug.Log(message, this);
            if (enableFileLog) UTXR.Core.Logger.LogApp(message);
        }
        private void ValidateInteractionRoots()
        {
            if (controllerInteractionRoots == null || handInteractionRoots == null)
                throw new ArgumentException("Interaction root arrays must be configured explicitly; use empty arrays for state-only monitoring.");
            foreach (var root in controllerInteractionRoots)
            {
                ValidateRoot(root);
                foreach (var handRoot in handInteractionRoots)
                    if (root != null && handRoot != null &&
                        (root.transform.IsChildOf(handRoot.transform) || handRoot.transform.IsChildOf(root.transform)))
                        throw new ArgumentException("Hand and controller interaction roots must not overlap or contain each other.");
            }
            foreach (var root in handInteractionRoots) ValidateRoot(root);
        }
        private void ValidateRoot(GameObject root)
        {
            if (root == null) throw new ArgumentException("Remove null interaction-root entries or assign them explicitly.");
            if (transform.IsChildOf(root.transform))
                throw new ArgumentException("Interaction roots must not contain the monitor; it must stay active in both modes.");
        }
        private void ApplyInteractionMode(XRInputMode mode)
        {
            SetRootsActive(controllerInteractionRoots, false);
            SetRootsActive(handInteractionRoots, false);
            SetRootsActive(mode == XRInputMode.Controller ? controllerInteractionRoots : handInteractionRoots, true);
        }
        private static void SetRootsActive(GameObject[] roots, bool active)
        {
            foreach (var root in roots) if (root != null) root.SetActive(active);
        }
    }
}
