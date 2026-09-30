using System;
using UnityEngine;

namespace Record3D
{
    /// <summary>
    /// Lightweight Scene Manager & Public Interface for Record3D ingestion and reconstruction.
    /// Attached to the GameObject in the scene (defining the robot/camera reference baseline).
    /// </summary>
    public class Record3DManager : MonoBehaviour
    {
        private static Record3DManager _instance;
        public static Record3DManager Instance => _instance;

        [Header("Connection")]
        [SerializeField] private string _deviceIp = "192.168.0.194";
        [SerializeField] private int _devicePort = 80;
        [SerializeField, Range(1, 60)] private int _ingestFps = 30;
        [SerializeField] private bool _enableAutoProbe = true;
        [SerializeField] private int _maxRgbWidth = 1440;
        [SerializeField] private int _maxHeight = 960;

        [Header("Pose Calibration")]
        [SerializeField] private bool _useDevicePose = true;

        [Header("Rendering")]
        [SerializeField] private RenderingMode _renderingMode = RenderingMode.Mesh;
        [SerializeField] private float _scale = 1.0f;
        [SerializeField] private float _minDepth = 0.3f;
        [SerializeField] private float _maxDepth = 2.8f;
        [SerializeField] private float _depthThreshold = 0.05f;
        [SerializeField] private bool _flipY = true;
        [SerializeField] private bool _enableGui = true;

        [Header("Point Cloud Settings")]
        [SerializeField] private float _pointSize = 3.0f;
        [SerializeField, Range(1, 8)] private int _pointStep = 2;

        [Header("Mesh Settings")]
        [SerializeField, Range(1, 8)] private int _meshLod = 2;
        [SerializeField] private bool _enableDithering = true;
        [SerializeField, Range(0.005f, 0.3f)] private float _ditherRange = 0.15f;
        [SerializeField, Range(0.0f, 1.0f)] private float _ditherIntensity = 1.0f;

        [Header("RoomScan")]
        [SerializeField, Range(1, 16)] private int _roomScanAnchors = 6;
        [SerializeField, Range(1, 8)] private int _roomScanMeshLod = 2;
        [SerializeField, Range(0.01f, 0.30f)] private float _roomScanDepthThreshold = 0.08f;
        [SerializeField] private Color _fresnelLightColor = new Color(0.75f, 0.77f, 0.80f);
        [SerializeField] private Color _fresnelDarkColor = new Color(0.20f, 0.22f, 0.24f);

        private Record3DPipeline _pipeline;
        public Record3DPipeline Pipeline => _pipeline;

        // Events
        public static event Action OnPoseReset;
        public static event Action OnPoseResetEvent { add => OnPoseReset += value; remove => OnPoseReset -= value; }
        public event Action<Texture, Record3DMetadataInfo> OnFrameReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;

        // Public Accessors & Transform Relative Pose
        public bool IsConnected => _pipeline != null && _pipeline.IsConnected;
        public bool IsPipelineRunning => _pipeline != null && _pipeline.IsRunning;
        public Vector3 CameraPosition => _pipeline != null ? _pipeline.LatestMetadata.cameraPosition : Vector3.zero;
        public Quaternion CameraRotation => _pipeline != null ? _pipeline.LatestMetadata.cameraRotation : Quaternion.identity;
        public Matrix4x4 PoseMatrix => _pipeline != null ? _pipeline.LatestMetadata.poseMatrix : Matrix4x4.identity;
        public Vector3 WorldCameraPosition => transform.TransformPoint(CameraPosition);
        public Quaternion WorldCameraRotation => transform.rotation * CameraRotation;
        public float Fps => _pipeline?.Fps ?? 0f;
        public string StatusMessage => _pipeline?.StatusMessage ?? "";
        public Record3DMetadataInfo LatestMetadata => _pipeline?.LatestMetadata ?? default;
        public bool HasInitialPose => _pipeline != null && _pipeline.HasInitialPose;
        public Vector3 InitialCameraPosition => _pipeline?.InitialCameraPosition ?? Vector3.zero;
        public Quaternion InitialCameraRotation => _pipeline?.InitialCameraRotation ?? Quaternion.identity;

        // Subsystems (Direct Access)
        public IRecord3DGpuProcessor GpuProcessor => _pipeline?.GpuProcessor;
        public IRecord3DRenderer Renderer => _pipeline?.Renderer;
        public IRecord3DReceiver Receiver => _pipeline?.Receiver;
        public Record3DCoordinateVisualizer CoordinateVisualizer => _pipeline?.CoordinateVisualizer;
        public Record3DAutoProbe AutoProbe => _pipeline?.AutoProbe;

        // Settings Properties
        public string DeviceIp { get => _deviceIp; set { _deviceIp = value; _pipeline?.UpdateDeviceAddress(); } }
        public int DevicePort { get => _devicePort; set { _devicePort = value; _pipeline?.UpdateDeviceAddress(); } }
        public string DeviceAddress => _devicePort == 80 || _devicePort <= 0 ? _deviceIp : $"{_deviceIp}:{_devicePort}";
        public int IngestFps { get => _ingestFps; set => _ingestFps = Mathf.Clamp(value, 1, 60); }
        public int MaxRgbWidth => _maxRgbWidth;
        public int MaxHeight => _maxHeight;
        public bool EnableAutoProbe { get => _enableAutoProbe; set { _enableAutoProbe = value; _pipeline?.AutoProbe?.SetEnable(value); } }
        public bool enablegui { get => _enableGui; set { _enableGui = value; if (_pipeline?.CoordinateVisualizer != null) _pipeline.CoordinateVisualizer.ShowGuiPanel = value; } }
        public bool enableGui { get => enablegui; set => enablegui = value; }
        public bool UseDevicePose { get => _useDevicePose; set => _useDevicePose = value; }
        public RenderingMode RenderingMode => _renderingMode;
        public RenderingMode Mode { get => _renderingMode; set => SwitchRenderingMode(value); }
        public float Scale { get => _scale; set => _scale = value; }
        public float MinDepth { get => _minDepth; set => _minDepth = value; }
        public float MaxDepth { get => _maxDepth; set => _maxDepth = value; }
        public float DepthThreshold { get => _depthThreshold; set => _depthThreshold = value; }
        public bool FlipY { get => _flipY; set => _flipY = value; }
        public float PointSize { get => _pointSize; set => _pointSize = value; }
        public int PointStep { get => _pointStep; set => _pointStep = Mathf.Clamp(value, 1, 8); }
        public int MeshLod { get => _meshLod; set => _meshLod = Mathf.Clamp(value, 1, 8); }
        public bool EnableDithering { get => _enableDithering; set => _enableDithering = value; }
        public float DitherRange { get => _ditherRange; set => _ditherRange = value; }
        public float DitherIntensity { get => _ditherIntensity; set => _ditherIntensity = value; }
        public int RoomScanAnchors { get => _roomScanAnchors; set => _roomScanAnchors = Mathf.Clamp(value, 1, 16); }
        public int RoomScanMeshLod { get => _roomScanMeshLod; set => _roomScanMeshLod = Mathf.Clamp(value, 1, 8); }
        public float RoomScanDepthThreshold { get => _roomScanDepthThreshold; set => _roomScanDepthThreshold = Mathf.Max(0.01f, value); }
        public Color FresnelLightColor { get => _fresnelLightColor; set => _fresnelLightColor = value; }
        public Color FresnelDarkColor { get => _fresnelDarkColor; set => _fresnelDarkColor = value; }

        // Core Public Control API
        public static void ResetCurrentPose() => _instance?.ResetPose();
        public void ResetPose() => _pipeline?.ResetPose();
        public void ResetIPhonePose() => ResetPose();
        public void RecenterOrigin() => ResetPose();
        public void Connect() => _pipeline?.Connect();
        public void Disconnect() => _pipeline?.Disconnect();
        public void Reconnect() => _pipeline?.Reconnect();
        public void StartPipeline() => _pipeline?.StartPipeline();
        public void StopPipeline() => _pipeline?.StopPipeline();
        public void TogglePipeline() => _pipeline?.TogglePipeline();
        public void SwitchRenderingMode(RenderingMode mode) { _renderingMode = mode; _pipeline?.SwitchRenderingMode(mode); }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            _pipeline = new Record3DPipeline(this);
            _pipeline.OnFrameReceived += (tex, meta) => OnFrameReceived?.Invoke(tex, meta);
            _pipeline.OnConnected += () => OnConnected?.Invoke();
            _pipeline.OnDisconnected += () => OnDisconnected?.Invoke();
            _pipeline.OnPoseReset += () => OnPoseReset?.Invoke();
            _pipeline.Initialize();
        }

        private void Start() { if (Application.isPlaying) StartPipeline(); }
        private void Update() => _pipeline?.Tick();
        public void PrepareForShutdown() => _pipeline?.PrepareForShutdown();
        public static void PrepareForEditorPlayModeExit() => _instance?.PrepareForShutdown();
        private void OnApplicationQuit() => PrepareForShutdown();
        private void OnDestroy() { _pipeline?.Dispose(); if (_instance == this) _instance = null; }

#if UNITY_EDITOR
        public static event Action<Record3DManager> OnDrawDebugGUI;
        private void OnGUI() { if (_enableGui && Application.isPlaying) OnDrawDebugGUI?.Invoke(this); }
#endif
    }
}
