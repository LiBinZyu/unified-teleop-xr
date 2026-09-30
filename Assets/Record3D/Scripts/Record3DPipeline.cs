using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Record3D
{
    /// <summary>
    /// Core execution engine for Record3D ingestion, GPU processing, pose anchoring, and rendering.
    /// Decoupled from Unity scene lifecycle and UI, handling the heavy operational logic.
    /// </summary>
    public class Record3DPipeline : IDisposable
    {
        private readonly Record3DManager _manager;

        // Subsystems
        private IRecord3DGpuProcessor _gpuProcessor;
        private IRecord3DReceiver _webrtcReceiver;
        private IRecord3DRenderer _renderer;
        private Record3DCoordinateVisualizer _coordinateVisualizer;
        private Record3DAutoProbe _autoProbe;

        // State flags
        private bool _isInitialized = false;
        private bool _isPipelineRunning = false;
        private bool _isShuttingDown = false;
        private volatile bool _pendingAutoConnect = false;
        private bool _userManuallyDisconnected = false;
        private bool _wasConnectedLastFrame = false;

        // Pose Calibration / Zero-Start Anchoring
        private bool _hasInitialPose = false;
        private Vector3 _initialCameraPosition = Vector3.zero;
        private Quaternion _initialCameraRotation = Quaternion.identity;
        private Record3DMetadataInfo _latestMetadata;

        // Diagnostics
        private float _fps = 0f;
        private float _fpsTimer = 0f;
        private int _fpsCounter = 0;
        private string _statusMessage = "Ready";

        // Snapshot & Ingestion
        private double _lastIngestTime = 0.0;
        private RenderTexture _ingestSnapshot;

        // Events
        public event Action<Texture, Record3DMetadataInfo> OnFrameReceived;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action OnPoseReset;

        // Properties
        public bool IsRunning => _isPipelineRunning;
        public bool IsConnected => _webrtcReceiver != null && _webrtcReceiver.IsConnected;
        public float Fps => _fps;
        public string StatusMessage => _statusMessage;
        public Record3DMetadataInfo LatestMetadata => _latestMetadata;

        public bool HasInitialPose => _hasInitialPose;
        public Vector3 InitialCameraPosition => _initialCameraPosition;
        public Quaternion InitialCameraRotation => _initialCameraRotation;

        public IRecord3DGpuProcessor GpuProcessor => _gpuProcessor;
        public IRecord3DReceiver Receiver => _webrtcReceiver;
        public IRecord3DRenderer Renderer => _renderer;
        public Record3DCoordinateVisualizer CoordinateVisualizer => _coordinateVisualizer;
        public Record3DAutoProbe AutoProbe => _autoProbe;

        public Record3DPipeline(Record3DManager manager)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            // 1. GPU Ring Buffer Processor
            _gpuProcessor = new Record3DGpuProcessor();
            _gpuProcessor.Initialize(_manager.MaxRgbWidth, _manager.MaxHeight, 16);
            _gpuProcessor.FlipY = _manager.FlipY;
            _gpuProcessor.Anchors = _manager.RoomScanAnchors;
            _gpuProcessor.Mode = _manager.RenderingMode;

            // 2. Reconstruction Renderer
            _renderer = new Record3DRenderer
            {
                Mode = _manager.RenderingMode,
                PointSize = _manager.PointSize,
                PointStep = _manager.PointStep,
                Scale = _manager.Scale,
                MinDepth = _manager.MinDepth,
                MaxDepth = _manager.MaxDepth,
                DepthThreshold = _manager.DepthThreshold,
                UseDevicePose = _manager.UseDevicePose,
                MeshLod = _manager.MeshLod,
                EnableDithering = _manager.EnableDithering,
                DitherRange = _manager.DitherRange,
                DitherIntensity = _manager.DitherIntensity,
                RoomScanAnchors = _manager.RoomScanAnchors,
                RoomScanMeshLod = _manager.RoomScanMeshLod,
                RoomScanDepthThreshold = _manager.RoomScanDepthThreshold,
                FresnelLightColor = _manager.FresnelLightColor,
                FresnelDarkColor = _manager.FresnelDarkColor
            };

            // 3. Coordinate System Visualizer
            _coordinateVisualizer = new Record3DCoordinateVisualizer(_manager.transform, _manager);
            _coordinateVisualizer.ShowGuiPanel = _manager.enablegui;

            // 4. WebRTC Ingestion & Auto-Discovery
            if (Application.isPlaying)
            {
                _webrtcReceiver = new Record3DWebRTCReceiver(_manager, _manager.DeviceAddress);
                _webrtcReceiver.OnFrameReceived += HandleFrameReceived;

                _autoProbe = new Record3DAutoProbe(
                    () => _manager.DeviceIp,
                    () => _manager.DevicePort,
                    () => _userManuallyDisconnected || (_webrtcReceiver != null && (_webrtcReceiver.IsConnected || _webrtcReceiver.IsConnecting)),
                    () =>
                    {
                        if (_isPipelineRunning && !_userManuallyDisconnected && _webrtcReceiver != null && !_webrtcReceiver.IsConnected && !_webrtcReceiver.IsConnecting)
                        {
                            _pendingAutoConnect = true;
                        }
                    },
                    isOnline =>
                    {
                        if (_webrtcReceiver == null || !_webrtcReceiver.IsConnected)
                        {
                            _statusMessage = isOnline
                                ? $"Record3D Online ({_manager.DeviceAddress}) - Connecting..."
                                : $"AutoProbe: Searching {_manager.DeviceAddress}...";
                        }
                    }
                );
                _autoProbe.SetEnable(_manager.EnableAutoProbe);
            }

            _isInitialized = true;
        }

        public void StartPipeline()
        {
            Initialize();
            ResetPose();
            _isPipelineRunning = true;
            _userManuallyDisconnected = false;
            _pendingAutoConnect = false;

            if (_webrtcReceiver != null && !_webrtcReceiver.IsConnected && !_webrtcReceiver.IsConnecting)
            {
                _webrtcReceiver.DeviceAddress = _manager.DeviceAddress;
                _statusMessage = $"Connecting to {_manager.DeviceAddress}...";
                _webrtcReceiver.StartReceiving();
            }
            else if (_manager.EnableAutoProbe)
            {
                _statusMessage = $"AutoProbe: Searching {_manager.DeviceAddress}...";
                _autoProbe?.SetEnable(true);
            }
            else
            {
                _statusMessage = $"Ready ({_manager.DeviceAddress})";
            }

            Record3DLogger.Info(Record3DLogCategory.General, "Record3D Pipeline STARTED.");
        }

        public void StopPipeline()
        {
            _isPipelineRunning = false;
            ResetPose();
            _autoProbe?.SetEnable(false);
            _webrtcReceiver?.StopReceiving();
            _statusMessage = "Pipeline Stopped";
            Record3DLogger.Info(Record3DLogCategory.General, "Record3D Pipeline STOPPED.");
        }

        public void TogglePipeline()
        {
            if (_isPipelineRunning) StopPipeline();
            else StartPipeline();
        }

        public void Connect()
        {
            _userManuallyDisconnected = false;
            _pendingAutoConnect = false;
            ResetPose();
            if (!_isPipelineRunning) StartPipeline();
            _statusMessage = $"Connecting to {_manager.DeviceAddress}...";
            if (_webrtcReceiver != null)
            {
                _webrtcReceiver.DeviceAddress = _manager.DeviceAddress;
                _webrtcReceiver.StartReceiving();
            }
        }

        public void Disconnect()
        {
            _userManuallyDisconnected = true;
            _pendingAutoConnect = false;
            ResetPose();
            _webrtcReceiver?.StopReceiving();
            _statusMessage = "Disconnected";
        }

        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        public void ResetPose()
        {
            _hasInitialPose = false;
            _initialCameraPosition = Vector3.zero;
            _initialCameraRotation = Quaternion.identity;

            _latestMetadata.cameraPosition = Vector3.zero;
            _latestMetadata.cameraRotation = Quaternion.identity;
            _latestMetadata.poseMatrix = Matrix4x4.identity;

            _coordinateVisualizer?.UpdatePose(Vector3.zero, Quaternion.identity, _manager.UseDevicePose);
            OnPoseReset?.Invoke();
            Record3DLogger.Info(Record3DLogCategory.General, "[Record3D] iPhone pose RESET: Pulled back to initial anchor pose.");
        }

        public void SwitchRenderingMode(RenderingMode mode)
        {
            if (_renderer != null) _renderer.Mode = mode;
            if (_gpuProcessor != null)
            {
                _gpuProcessor.Mode = mode;
                if (mode != RenderingMode.RoomScan) _gpuProcessor.ClearKeyframes();
            }
        }

        public void UpdateDeviceAddress()
        {
            if (_webrtcReceiver != null)
            {
                _webrtcReceiver.DeviceAddress = _manager.DeviceAddress;
            }
        }

        public void Tick()
        {
            _fpsCounter++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsCounter / _fpsTimer;
                _fpsCounter = 0;
                _fpsTimer = 0f;
            }

            if (!_isPipelineRunning || !Application.isPlaying)
                return;

            // Connection state transitions
            bool connected = IsConnected;
            if (connected && !_wasConnectedLastFrame) OnConnected?.Invoke();
            else if (!connected && _wasConnectedLastFrame) OnDisconnected?.Invoke();
            _wasConnectedLastFrame = connected;

            // Auto-connect dispatch
            if (_pendingAutoConnect)
            {
                _pendingAutoConnect = false;
                if (!_userManuallyDisconnected && _webrtcReceiver != null && !_webrtcReceiver.IsConnected && !_webrtcReceiver.IsConnecting)
                {
                    Connect();
                }
            }

            // Pump WebRTC network packets
            _webrtcReceiver?.Update();

            // Synchronize parameters & submit GPU render command
            if (_renderer != null && _gpuProcessor != null)
            {
                _gpuProcessor.Mode = _manager.RenderingMode;
                _renderer.Mode = _manager.RenderingMode;
                _renderer.PointSize = _manager.PointSize;
                _renderer.PointStep = _manager.PointStep;
                _renderer.Scale = _manager.Scale;
                _renderer.MinDepth = _manager.MinDepth;
                _renderer.MaxDepth = _manager.MaxDepth;
                _renderer.DepthThreshold = _manager.DepthThreshold;
                _renderer.UseDevicePose = _manager.UseDevicePose;
                _renderer.MeshLod = _manager.MeshLod;
                _renderer.EnableDithering = _manager.EnableDithering;
                _renderer.DitherRange = _manager.DitherRange;
                _renderer.DitherIntensity = _manager.DitherIntensity;
                _renderer.RoomScanAnchors = _manager.RoomScanAnchors;
                _renderer.RoomScanMeshLod = _manager.RoomScanMeshLod;
                _renderer.RoomScanDepthThreshold = _manager.RoomScanDepthThreshold;
                _renderer.FresnelLightColor = _manager.FresnelLightColor;
                _renderer.FresnelDarkColor = _manager.FresnelDarkColor;

                _gpuProcessor.FlipY = _manager.FlipY;
                _gpuProcessor.Anchors = _manager.RoomScanAnchors;
                _gpuProcessor.MeshLod = _manager.MeshLod;
                _gpuProcessor.Scale = _manager.Scale;
                _gpuProcessor.MinDepth = _manager.MinDepth;
                _gpuProcessor.MaxDepth = _manager.MaxDepth;
                _gpuProcessor.DepthThreshold = _manager.DepthThreshold;
                _gpuProcessor.DitherRange = _manager.DitherRange;

                _renderer.Render(_gpuProcessor, _manager.transform);
            }

            // Update coordinate visualizer
            _coordinateVisualizer?.UpdatePose(_latestMetadata.cameraPosition, _latestMetadata.cameraRotation, _manager.UseDevicePose);
        }

        private void HandleFrameReceived(Texture sourceTex, Record3DMetadataInfo metadata)
        {
            if (_isShuttingDown || _gpuProcessor == null || !_isPipelineRunning || sourceTex == null) return;

            // Ingestion throttling
            double now = Time.realtimeSinceStartupAsDouble;
            double minInterval = 1.0 / Mathf.Clamp(_manager.IngestFps, 1, 120);
            if (now - _lastIngestTime < minInterval) return;
            _lastIngestTime = now;

            // Pose Calibration / Zero-Start Anchoring to Transform
            if (_manager.UseDevicePose)
            {
                if (!_hasInitialPose && metadata.timestamp > 0)
                {
                    _initialCameraPosition = metadata.cameraPosition;
                    _initialCameraRotation = metadata.cameraRotation;
                    _hasInitialPose = true;
                    Record3DLogger.Info(Record3DLogCategory.General,
                        $"[Record3D] Initial iPhone reference locked: Pos={_initialCameraPosition:F3}, Rot={_initialCameraRotation.eulerAngles:F1}°");
                }

                if (_hasInitialPose)
                {
                    Quaternion relRot = Quaternion.Inverse(_initialCameraRotation) * metadata.cameraRotation;
                    Vector3 relPos = Quaternion.Inverse(_initialCameraRotation) * (metadata.cameraPosition - _initialCameraPosition);

                    metadata.cameraPosition = relPos;
                    metadata.cameraRotation = relRot;
                    metadata.poseMatrix = Matrix4x4.TRS(relPos, relRot, Vector3.one);
                }
            }
            else
            {
                metadata.cameraPosition = Vector3.zero;
                metadata.cameraRotation = Quaternion.identity;
                metadata.poseMatrix = Matrix4x4.identity;
            }

            _latestMetadata = metadata;
            metadata.minDepth = _manager.MinDepth;
            metadata.maxDepth = _manager.MaxDepth;
            metadata.depthThreshold = _manager.DepthThreshold;

            Texture ingestSnapshot = SnapshotVideoFrame(sourceTex);
            if (ingestSnapshot == null) return;

            _gpuProcessor.ProcessFrame(ingestSnapshot, metadata);
            OnFrameReceived?.Invoke(ingestSnapshot, metadata);

            _statusMessage = $"WebRTC Live ({_gpuProcessor.CurrentRgbWidth}x{_gpuProcessor.CurrentRgbHeight}) @ {_manager.IngestFps}fps";
        }

        private Texture SnapshotVideoFrame(Texture sourceTex)
        {
            if (sourceTex == null || sourceTex.width <= 0 || sourceTex.height <= 0) return null;

            if (_ingestSnapshot == null ||
                _ingestSnapshot.width != sourceTex.width ||
                _ingestSnapshot.height != sourceTex.height ||
                _ingestSnapshot.graphicsFormat != sourceTex.graphicsFormat)
            {
                _gpuProcessor?.WaitForPendingSourceRead();
                ReleaseIngestSnapshot();

                var descriptor = new RenderTextureDescriptor(
                    sourceTex.width, sourceTex.height, sourceTex.graphicsFormat, 0)
                {
                    msaaSamples = 1,
                    useMipMap = false,
                    autoGenerateMips = false,
                    enableRandomWrite = false,
                    depthBufferBits = 0
                };

                _ingestSnapshot = new RenderTexture(descriptor)
                {
                    name = $"Record3D_IngestSnapshot_{sourceTex.width}x{sourceTex.height}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _ingestSnapshot.Create();
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Record3DLogger.GPU("GPU readback synchronization unavailable; ingest skipped.", Record3DLogLevel.Error);
                return null;
            }

            Graphics.CopyTexture(sourceTex, _ingestSnapshot);

            var copyCompletion = AsyncGPUReadback.Request(_ingestSnapshot, 0, 0, 1, 0, 1, 0, 1);
            copyCompletion.WaitForCompletion();
            if (copyCompletion.hasError) return null;

            return _ingestSnapshot;
        }

        private void ReleaseIngestSnapshot()
        {
            if (_ingestSnapshot == null) return;
            _ingestSnapshot.Release();
            if (Application.isPlaying) UnityEngine.Object.Destroy(_ingestSnapshot);
            else UnityEngine.Object.DestroyImmediate(_ingestSnapshot);
            _ingestSnapshot = null;
        }

        public void PrepareForShutdown()
        {
            if (_isShuttingDown) return;
            _isShuttingDown = true;
            _isPipelineRunning = false;
            _pendingAutoConnect = false;

            _autoProbe?.SetEnable(false);

            if (_webrtcReceiver != null)
            {
                _webrtcReceiver.OnFrameReceived -= HandleFrameReceived;
                _gpuProcessor?.WaitForPendingSourceRead();
                _webrtcReceiver.Dispose();
                _webrtcReceiver = null;
            }
        }

        public void Dispose()
        {
            PrepareForShutdown();

            _autoProbe?.Dispose();
            _autoProbe = null;

            _coordinateVisualizer?.Dispose();
            _coordinateVisualizer = null;

            _gpuProcessor?.WaitForPendingSourceRead();
            ReleaseIngestSnapshot();

            _gpuProcessor?.Dispose();
            _gpuProcessor = null;

            _renderer?.Dispose();
            _renderer = null;

            _isInitialized = false;
        }
    }
}
