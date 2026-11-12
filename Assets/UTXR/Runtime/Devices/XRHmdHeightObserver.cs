using UTXR.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace UTXR.Devices
{
    public enum HeightState
    {
        Unknown,
        Normal,
        TooLow,
        TooHigh
    }

    /// <summary>
    /// Background observer that samples HMD height to determine whether floor calibration is needed.
    /// Runs at 2Hz only when the application is focused and tracking is active, with zero GC allocations.
    /// </summary>
    public class XRHmdHeightObserver : MonoBehaviour
    {
        private const string TAG = "[XRHmdHeightObserver] ";

        [Header("State Events")]
        public UnityEvent onHeightNormal;
        public UnityEvent onHeightTooLow;
        public UnityEvent onHeightTooHigh;

        public UnityEvent<float> onCurrentHeightUpdated;

        // Evaluation parameters
        private const float SampleInterval = 0.5f;  // Sampling interval 0.5s (2Hz)
        private const float MinNormalHeight = 1.3f; // Lower bound for normal adult height (sit/stand)
        private const float MaxNormalHeight = 2.0f; // Upper bound for normal adult height (stand)
        private const int WindowSize = 8;            // 8-sample sliding window (2 seconds history)
        private const float MajorityRatio = 0.75f;   // 75% majority vote ratio

        // Ring buffer and scratch buffer for median calculation (0 GC allocations)
        private readonly float[] _samples = new float[WindowSize];
        private readonly float[] _scratch = new float[WindowSize];
        private int _sampleCount = 0;
        private int _sampleIndex = 0;
        private float _nextSampleTime = 0f;
        private HeightState _currentState = HeightState.Unknown;
        private float _lastEvaluatedHeight = 0f;
        private XRInputSubsystem _inputSubsystem;

        public float LastEvaluatedHeight => _lastEvaluatedHeight;
        public HeightState CurrentHeightState => _currentState;

        private void OnEnable()
        {
            var subsystems = new System.Collections.Generic.List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            if (subsystems.Count > 0)
            {
                _inputSubsystem = subsystems[0];
                _inputSubsystem.trackingOriginUpdated += ResetHeightBuffer;
            }
        }

        private void OnDisable()
        {
            if (_inputSubsystem != null)
            {
                _inputSubsystem.trackingOriginUpdated -= ResetHeightBuffer;
                _inputSubsystem = null;
            }
        }

        private void Update()
        {
            if (!Application.isFocused || Time.unscaledTime < _nextSampleTime) return;
            _nextSampleTime = Time.unscaledTime + SampleInterval;

            if (!TryGetCurrentHmdHeight(out float height))
            {
                if (_sampleCount > 0) ResetHeightBuffer();
                return;
            }

            _samples[_sampleIndex] = height;
            _sampleIndex = (_sampleIndex + 1) % WindowSize;
            if (_sampleCount < WindowSize) _sampleCount++;

            if (_sampleCount >= WindowSize) EvaluateHeight();
        }

        /// <summary>
        /// Reads instantaneous HMD height (Y axis). Supports XRNode.Head / CenterEye and falls back to Camera.main.
        /// </summary>
        public bool TryGetCurrentHmdHeight(out float height)
        {
            height = 0f;
            InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!hmd.isValid) hmd = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);

            if (hmd.isValid && hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            {
                if (hmd.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos) ||
                    hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out pos))
                {
                    height = pos.y;
                    return true;
                }
            }

            if (Camera.main != null)
            {
                height = Camera.main.transform.position.y;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Resets the sampling buffer (called upon recenter or tracking loss).
        /// </summary>
        public void ResetHeightBuffer(XRInputSubsystem _ = null)
        {
            _sampleCount = 0;
            _sampleIndex = 0;
            _currentState = HeightState.Unknown;
        }

        private void EvaluateHeight()
        {
            int lowCount = 0;
            int highCount = 0;
            int normalCount = 0;

            for (int i = 0; i < WindowSize; i++)
            {
                float h = _samples[i];
                _scratch[i] = h;

                if (h < MinNormalHeight) lowCount++;
                else if (h > MaxNormalHeight) highCount++;
                else normalCount++;
            }

            // Fast median calculation with zero GC
            System.Array.Sort(_scratch, 0, WindowSize);
            _lastEvaluatedHeight = _scratch[WindowSize / 2];

            onCurrentHeightUpdated?.Invoke(_lastEvaluatedHeight);

            int threshold = Mathf.CeilToInt(WindowSize * MajorityRatio);

            if (lowCount >= threshold && _currentState != HeightState.TooLow)
            {
                _currentState = HeightState.TooLow;
                Debug.LogWarning($"{TAG}HMD Height Too Low: {_lastEvaluatedHeight:F2}m (< {MinNormalHeight:F2}m)");
                onHeightTooLow?.Invoke();
            }
            else if (highCount >= threshold && _currentState != HeightState.TooHigh)
            {
                _currentState = HeightState.TooHigh;
                Debug.LogWarning($"{TAG}HMD Height Too High: {_lastEvaluatedHeight:F2}m (> {MaxNormalHeight:F2}m)");
                onHeightTooHigh?.Invoke();
            }
            else if (normalCount >= threshold && _currentState != HeightState.Normal)
            {
                _currentState = HeightState.Normal;
                Debug.Log($"{TAG}HMD Height Normal: {_lastEvaluatedHeight:F2}m");
                onHeightNormal?.Invoke();
            }
        }
    }
}
