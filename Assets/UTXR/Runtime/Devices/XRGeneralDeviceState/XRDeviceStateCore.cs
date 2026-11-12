using System;
using UnityEngine;
using UnityEngine.XR;

namespace UTXR.Devices
{
    /// <summary>Per-side tracking and pose-based gestures, shared by every provider.</summary>
    public sealed class XRDeviceStateCore
    {
        // Reject runtime sentinel values independently of the optional human-motion filters.
        private const float RuntimeLinearSpeedLimit = 1000f; // m/s
        private const float RuntimeAngularSpeedLimit = 1000f; // rad/s
        private readonly XRDeviceStateSettings _settings;
        private uint _trackingHistory;
        private float _warningRemaining, _velocityFreezeSeconds;
        private bool _hasPosition;
        private Vector3 _lastPosition;
        private bool _hasGesturePosition, _swingArmed = true;
        private Vector3 _lastGesturePosition;
        private float _sinceSwing = float.PositiveInfinity;
        private int _swingCount;
        private float _comboElapsed;

        public XRDeviceStateCore(XRDeviceStateSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Validate();
            _settings = settings.Copy();
        }

        public XRDeviceState Process(XRDeviceSample sample, float deltaTime)
        {
            if (!IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            bool hasPosition = (sample.TrackingState & InputTrackingState.Position) != 0;
            bool hasRotation = (sample.TrackingState & InputTrackingState.Rotation) != 0;
            bool tracked = sample.IsTracked && (_settings.Require6DoF ? hasPosition && hasRotation : hasPosition || hasRotation);
            if (!sample.Status.IsReady)
            {
                Reset();
                return new XRDeviceState(sample, false);
            }
            if (!tracked)
            {
                _trackingHistory = 0;
                _hasPosition = false;
                _velocityFreezeSeconds = 0f;
                ResetGestures();
                // A connected source with invalid hardware tracking must warn even if its pose bits remain set.
                return new XRDeviceState(sample, false, UpdateWarning(true, deltaTime));
            }
            if ((hasPosition && !IsFinite(sample.Pose.position)) || (hasRotation && !IsFinite(sample.Pose.rotation)) ||
                (sample.LinearVelocity.HasValue && !IsFinite(sample.LinearVelocity.Value)) ||
                (sample.AngularVelocity.HasValue && !IsFinite(sample.AngularVelocity.Value)))
                return Fail(sample, XRProviderStatusCode.InvalidData, "Selected source returned a non-finite measurement.");
            if ((sample.LinearVelocity.HasValue && sample.LinearVelocity.Value.sqrMagnitude >= RuntimeLinearSpeedLimit * RuntimeLinearSpeedLimit) ||
                (sample.AngularVelocity.HasValue && sample.AngularVelocity.Value.sqrMagnitude >= RuntimeAngularSpeedLimit * RuntimeAngularSpeedLimit))
                return Fail(sample, XRProviderStatusCode.InvalidData, "Runtime velocity exceeded the sanity limit (1000 m/s or 1000 rad/s).");
            if (_settings.UsesKinematicFilter && !sample.LinearVelocity.HasValue)
                return Fail(sample, XRProviderStatusCode.InvalidConfiguration,
                    "Kinematic filtering requires runtime linear velocity. Supply it or explicitly select another filter.");

            _trackingHistory = (_trackingHistory << 1) | 1u;
            bool accepted = true;
            if (_settings.Filter == FilterMethod.BitwiseDebounce || _settings.Filter == FilterMethod.Combined)
            {
                uint mask = _settings.FrameTolerance == 32 ? uint.MaxValue : (1u << _settings.FrameTolerance) - 1;
                // Legacy debounce accepts any valid sample in the window, not a full window of valid samples.
                accepted = (_trackingHistory & mask) != 0;
            }
            if (_settings.UsesKinematicFilter)
            {
                bool positionJump = false;
                if (hasPosition && _hasPosition && deltaTime > 0f && deltaTime < 1f)
                {
                    float distance = Vector3.Distance(sample.Pose.position, _lastPosition);
                    positionJump = distance > _settings.PositionJumpMinDelta &&
                        distance / Mathf.Max(deltaTime, .001f) > _settings.PositionJumpMaxSpeed;
                }
                if (positionJump) accepted = false;
                else
                {
                    float speed = sample.LinearVelocity.Value.magnitude;
                    _velocityFreezeSeconds = speed < _settings.MinLinearVelocity ? _velocityFreezeSeconds + deltaTime : 0f;
                    if (speed > _settings.MaxHumanSpeed || _velocityFreezeSeconds > _settings.LostToleranceSeconds) accepted = false;
                }
            }
            _lastPosition = sample.Pose.position;
            _hasPosition = hasPosition;
            bool warning = UpdateWarning(!hasPosition || !hasRotation || !accepted, deltaTime);

            XRGesture gestures = EvaluateGestures(accepted && hasPosition, sample.Pose.position, deltaTime);
            return new XRDeviceState(sample, accepted, warning, gestures);
        }

        private bool UpdateWarning(bool rejected, float deltaTime)
        {
            if (rejected) _warningRemaining = _settings.WarningHoldSeconds;
            if (_warningRemaining <= 0f) return false;
            _warningRemaining = Mathf.Max(0f, _warningRemaining - deltaTime);
            return true;
        }

        private XRGesture EvaluateGestures(bool tracked, Vector3 position, float deltaTime)
        {
            if (!tracked || !_settings.EnableSwingDetection)
            {
                ResetGestures();
                return XRGesture.None;
            }
            _sinceSwing += deltaTime;
            _comboElapsed += deltaTime;
            if (_comboElapsed > _settings.TripleSwingWindowSeconds) _swingCount = 0;
            bool canMeasure = _hasGesturePosition && deltaTime > 0f;
            float speed = canMeasure ? Vector3.Distance(position, _lastGesturePosition) / deltaTime : 0f;
            _lastGesturePosition = position;
            _hasGesturePosition = true;
            if (!canMeasure) return XRGesture.None;
            if (speed < _settings.SwingSpeedThreshold)
            {
                _swingArmed = true;
                return XRGesture.None;
            }
            if (!_swingArmed) return XRGesture.None;
            _swingArmed = false;
            if (_sinceSwing < _settings.SwingCooldownSeconds) return XRGesture.None;

            XRGesture gesture = XRGesture.Swing;
            if (_swingCount == 0) _comboElapsed = 0f;
            if (++_swingCount == 3)
            {
                gesture |= XRGesture.TripleSwing;
                _swingCount = 0;
            }
            _sinceSwing = 0f;
            return gesture;
        }

        public void Reset()
        {
            _trackingHistory = 0;
            _warningRemaining = _velocityFreezeSeconds = 0f;
            _hasPosition = false;
            ResetGestures();
        }

        private void ResetGestures()
        {
            _hasGesturePosition = false;
            _swingArmed = true;
            _sinceSwing = float.PositiveInfinity;
            _swingCount = 0;
            _comboElapsed = 0f;
        }

        private XRDeviceState Fail(XRDeviceSample raw, XRProviderStatusCode code, string message)
        {
            Reset();
            return new XRDeviceState(raw, false, status: new XRProviderStatus(code, message));
        }

        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        public static bool IsFinite(Quaternion value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
