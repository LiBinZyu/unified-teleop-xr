using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UTXR.Prefs;

namespace Record3D
{
    /// <summary>
    /// Feature Bridge for Record3D:
    /// Encapsulates Record3D settings and UI controls, binding them directly to PlayerPrefs with zero coupling.
    /// </summary>
    public class Record3DUIBridge : MonoBehaviour
    {
        private const string PREF_KEY_ENABLED = "Record3D_Enabled";
        private const string PREF_KEY_TARGET_IP = "Record3D_TargetIp";
        private const string PREF_KEY_INTERFACE_ID = "Record3D_InterfaceId";
        private const string PREF_KEY_INGEST_FPS = "Record3D_IngestFps";
        private const string PREF_KEY_MESH_LOD = "Record3D_MeshLod";
        private const string PREF_KEY_POINT_STEP = "Record3D_PointStep";
        private const string PREF_KEY_POINT_SIZE = "Record3D_PointSize";
        private const string PREF_KEY_DITHER_RANGE = "Record3D_DitherRange";
        private const string PREF_KEY_RENDER_MODE = "Record3D_RenderMode";

        [Header("Target Manager (Optional, defaults to Instance)")]
        [SerializeField] private Record3DManager _manager;

        [Header("Network Configuration")]
        [Tooltip("TMP_InputField for iOS device IP (Target IP)")]
        [SerializeField] private TMP_InputField _targetIpInputField;
        [Tooltip("TMP_Dropdown for selecting local network interface")]
        [SerializeField] private TMP_Dropdown _interfaceDropdown;

        [Header("Pipeline Overall Switch (Toggle)")]
        [SerializeField] private Toggle _record3dToggle;

        [Header("Sliders")]
        [SerializeField] private Slider _ingestFpsSlider;
        [SerializeField] private Slider _meshLodSlider;
        [SerializeField] private Slider _pointStepSlider;
        [SerializeField] private Slider _pointSizeSlider;
        [SerializeField] private Slider _ditherRangeSlider;

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

            // 1. Network Target IP and Interface Binding
            string defaultIp = mgr != null ? mgr.DeviceIp : "192.168.0.194";
            string currentTargetIp = PlayerPrefsManager.GetString(PREF_KEY_TARGET_IP, defaultIp);
            string currentInterfaceId = Network.InterfaceInfo.NetworkInterfacePrefBridge.ResolvePreferredInterfaceId(
                PREF_KEY_INTERFACE_ID, currentTargetIp, mgr != null ? mgr.InterfaceId : "");

            void RefreshInterfaceDropdown()
            {
                if (_interfaceDropdown == null) return;
                Network.InterfaceInfo.NetworkInterfacePrefBridge.BindDropdown(
                    _interfaceDropdown, PREF_KEY_INTERFACE_ID, currentInterfaceId, (selectedIface, localIp) =>
                    {
                        currentInterfaceId = selectedIface.Id;
                        TargetManager?.SetTargetNetwork(currentTargetIp, currentInterfaceId);
                    });
            }

            if (_targetIpInputField != null)
            {
                PlayerPrefsManager.Bind(_targetIpInputField, PREF_KEY_TARGET_IP, currentTargetIp, newIp =>
                {
                    if (string.IsNullOrWhiteSpace(newIp)) return;
                    currentTargetIp = newIp.Trim();
                    PlayerPrefsManager.SetString(PREF_KEY_TARGET_IP, currentTargetIp);

                    // Auto-calculate matching interface by subnet
                    if (Network.InterfaceInfo.NetworkInfoService.FindInterfaceBySubnet(currentTargetIp, out var matchedIface, out _))
                    {
                        currentInterfaceId = matchedIface.Id;
                        PlayerPrefsManager.SetString(PREF_KEY_INTERFACE_ID, currentInterfaceId);
                    }

                    RefreshInterfaceDropdown();
                    TargetManager?.SetTargetNetwork(currentTargetIp, currentInterfaceId);
                });
            }

            RefreshInterfaceDropdown();
            TargetManager?.SetTargetNetwork(currentTargetIp, currentInterfaceId);

            // 2. Overall Pipeline Switch
            PlayerPrefsManager.Bind(_record3dToggle, PREF_KEY_ENABLED, true, enabled =>
            {
                if (TargetManager != null)
                {
                    if (enabled) TargetManager.StartPipeline();
                    else TargetManager.StopPipeline();
                }
            });

            // 3. Ingest FPS (1 - 60)
            PlayerPrefsManager.Bind(_ingestFpsSlider, PREF_KEY_INGEST_FPS, mgr != null ? mgr.IngestFps : 30, fps =>
            {
                if (TargetManager != null) TargetManager.IngestFps = fps;
            });

            // 3. Mesh LOD (1 - 8)
            PlayerPrefsManager.Bind(_meshLodSlider, PREF_KEY_MESH_LOD, mgr != null ? mgr.MeshLod : 2, lod =>
            {
                if (TargetManager != null) TargetManager.MeshLod = lod;
            });

            // 4. Point Step (1 - 8)
            PlayerPrefsManager.Bind(_pointStepSlider, PREF_KEY_POINT_STEP, mgr != null ? mgr.PointStep : 2, step =>
            {
                if (TargetManager != null) TargetManager.PointStep = step;
            });

            // 5. Point Size (0.5 - 15.0)
            PlayerPrefsManager.Bind(_pointSizeSlider, PREF_KEY_POINT_SIZE, mgr != null ? mgr.PointSize : 3.0f, size =>
            {
                if (TargetManager != null) TargetManager.PointSize = size;
            });

            // 6. Dither Range (0.005 - 0.30)
            PlayerPrefsManager.Bind(_ditherRangeSlider, PREF_KEY_DITHER_RANGE, mgr != null ? mgr.DitherRange : 0.15f, dither =>
            {
                if (TargetManager != null) TargetManager.DitherRange = dither;
            });

            // 7. Rendering Mode Dropdown
            PlayerPrefsManager.Bind(_renderingModeDropdown, PREF_KEY_RENDER_MODE, mgr != null ? mgr.RenderingMode : RenderingMode.Mesh, mode =>
            {
                TargetManager?.SwitchRenderingMode(mode);
            });

            // Buttons
            if (_startPipelineButton != null)
            {
                _startPipelineButton.onClick.AddListener(() =>
                {
                    PlayerPrefsManager.SetBool(PREF_KEY_ENABLED, true);
                    TargetManager?.StartPipeline();
                });
            }

            if (_stopPipelineButton != null)
            {
                _stopPipelineButton.onClick.AddListener(() =>
                {
                    PlayerPrefsManager.SetBool(PREF_KEY_ENABLED, false);
                    TargetManager?.StopPipeline();
                });
            }

            if (_resetPoseButton != null)
            {
                _resetPoseButton.onClick.AddListener(() => TargetManager?.ResetPose());
            }
        }
    }
}
