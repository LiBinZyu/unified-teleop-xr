using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Record3D
{
    /// <summary>
    /// Simple UI Bridge connecting Unity UI Sliders, TMP_Dropdown, and Buttons to Record3DManager.
    /// </summary>
    public class Record3DUIBridge : MonoBehaviour
    {
        [Header("Target Manager (Optional, defaults to Instance)")]
        [SerializeField] private Record3DManager _manager;

        [Header("Sliders")]
        [SerializeField] private Slider _ingestFpsSlider;
        [SerializeField] private Slider _meshLodSlider;
        [SerializeField] private Slider _ditherRangeSlider;
        [SerializeField] private Slider _pointSizeSlider;
        [SerializeField] private Slider _pointStepSlider;

        [Header("Dropdown")]
        [SerializeField] private TMP_Dropdown _renderingModeDropdown;

        [Header("Buttons")]
        [SerializeField] private Button _startPipelineButton;
        [SerializeField] private Button _stopPipelineButton;
        [SerializeField] private Button _resetPoseButton;

        private Record3DManager TargetManager => _manager != null ? _manager : Record3DManager.Instance;

        private void Start()
        {
            var mgr = TargetManager;
            if (mgr != null)
            {
                // Assign proper ranges, whole numbers mode, and initial values matching Record3DManager
                SetupSlider(_ingestFpsSlider, 1f, 60f, true, mgr.IngestFps);
                SetupSlider(_meshLodSlider, 1f, 8f, true, mgr.MeshLod);
                SetupSlider(_pointStepSlider, 1f, 8f, true, mgr.PointStep);
                SetupSlider(_pointSizeSlider, 0.5f, 15.0f, false, mgr.PointSize);
                SetupSlider(_ditherRangeSlider, 0.005f, 0.30f, false, mgr.DitherRange);

                if (_renderingModeDropdown != null)
                {
                    _renderingModeDropdown.ClearOptions();
                    _renderingModeDropdown.AddOptions(new List<string>(Enum.GetNames(typeof(RenderingMode))));
                    _renderingModeDropdown.value = (int)mgr.RenderingMode;
                }
            }

            // Sliders value listeners
            if (_ingestFpsSlider != null)
                _ingestFpsSlider.onValueChanged.AddListener(val => { if (TargetManager != null) TargetManager.IngestFps = Mathf.RoundToInt(val); });

            if (_meshLodSlider != null)
                _meshLodSlider.onValueChanged.AddListener(val => { if (TargetManager != null) TargetManager.MeshLod = Mathf.RoundToInt(val); });

            if (_ditherRangeSlider != null)
                _ditherRangeSlider.onValueChanged.AddListener(val => { if (TargetManager != null) TargetManager.DitherRange = val; });

            if (_pointSizeSlider != null)
                _pointSizeSlider.onValueChanged.AddListener(val => { if (TargetManager != null) TargetManager.PointSize = val; });

            if (_pointStepSlider != null)
                _pointStepSlider.onValueChanged.AddListener(val => { if (TargetManager != null) TargetManager.PointStep = Mathf.RoundToInt(val); });

            // Dropdown listener
            if (_renderingModeDropdown != null)
                _renderingModeDropdown.onValueChanged.AddListener(index => { if (TargetManager != null) TargetManager.SwitchRenderingMode((RenderingMode)index); });

            // Buttons listeners
            if (_startPipelineButton != null)
                _startPipelineButton.onClick.AddListener(() => TargetManager?.StartPipeline());

            if (_stopPipelineButton != null)
                _stopPipelineButton.onClick.AddListener(() => TargetManager?.StopPipeline());

            if (_resetPoseButton != null)
                _resetPoseButton.onClick.AddListener(() => TargetManager?.ResetPose());
        }

        private void SetupSlider(Slider slider, float min, float max, bool wholeNumbers, float currentValue)
        {
            if (slider == null) return;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = currentValue;
        }
    }
}
