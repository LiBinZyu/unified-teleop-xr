using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace Apex.Dashboard
{
    public enum HeightState
    {
        Unknown,
        Normal,
        TooLow,
        TooHigh
    }

    /// <summary>
    /// 高性能后台检测 HMD 高度，判断是否需要地面校准。
    /// 仅在 App 聚焦且头显有效追踪时以 4Hz 低频采样，0 GC 堆内存分配。
    /// </summary>
    public class XRHmdHeightObserver : MonoBehaviour
    {
        private const string TAG = "[XRHmdHeightObserver] ";

        [Header("State Events")]
        public UnityEvent onHeightNormal;
        public UnityEvent onHeightTooLow;
        public UnityEvent onHeightTooHigh;

        public UnityEvent<float> onCurrentHeightUpdated;

        // 判定参数
        private const float SampleInterval = 0.5f;  // 采样间隔 0.5s (2Hz)
        private const float MinNormalHeight = 1.3f; // 正常成人身高下限（坐/站）
        private const float MaxNormalHeight = 2.0f; // 正常成人身高上限（站）
        private const int WindowSize = 8;            // 8 个样本滑动窗口（2秒历史）
        private const float MajorityRatio = 0.75f;   // 75% 多数表决比例

        // 环形缓冲与中位数计算缓存（0 GC 堆内存分配）
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
        /// 获取瞬时 HMD 高度（Y 轴）。支持 XRNode.Head / CenterEye 及 Camera.main 兜底。
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
        /// 清空采样缓冲（在重置朝向或丢失追踪时调用）
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

            // 0 GC 快速计算真实中位数 (Median)
            System.Array.Sort(_scratch, 0, WindowSize);
            _lastEvaluatedHeight = _scratch[WindowSize / 2];

            // 广播身高结果供外部订阅
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
