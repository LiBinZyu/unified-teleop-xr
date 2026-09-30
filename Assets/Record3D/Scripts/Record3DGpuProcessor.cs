using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Record3D
{
    public class Record3DGpuProcessor : IRecord3DGpuProcessor
    {
        private int _maxRgbWidth = 1440;
        private int _maxHeight = 960;
        private int _ringSize = 8;

        private RenderTexture _rgbRingArray;
        private RenderTexture _depthRingArray;
        private GraphicsBuffer _frameMetadataBuffer;
        private GPUFrameMetadata[] _metadataArray;
        private GraphicsFence _sourceReadFence;
        private bool _hasPendingSourceRead;

        private ComputeShader _computeShader;
        private int _kSplitAndDecode = -1;
        private int _kFilterAndConfidence = -1;
        private int _kTemporalFusion = -1;
        private int _kGenerateMesh = -1;

        private GraphicsBuffer _meshVertexBuffer;
        private int _meshVertexCount = 0;
        private Record3DMetadataInfo _lastMetadata;

        private int _writeRingIndex = 0;
        private uint _processedFrameCount = 0;
        private int _currentRgbWidth = 720;
        private int _currentRgbHeight = 960;
        private Vector4 _currentUnprojection = Vector4.zero;
        private Matrix4x4 _currentPoseMatrix = Matrix4x4.identity;

        public RenderTexture RgbRingArray => _rgbRingArray;
        public RenderTexture DepthRingArray => _depthRingArray;
        public GraphicsBuffer FrameMetadataBuffer => _frameMetadataBuffer;
        public int CurrentRingIndex => _writeRingIndex;
        public int CurrentRgbWidth => _currentRgbWidth;
        public int CurrentRgbHeight => _currentRgbHeight;
        public Vector4 CurrentUnprojection => _currentUnprojection;
        public Matrix4x4 CurrentPoseMatrix => _currentPoseMatrix;
        public uint ProcessedFrameCount => _processedFrameCount;
        public int RingSize => _ringSize;
        public int CommittedKeyframeCount => _committedKeyframeCount;
        public bool FlipY { get; set; } = true;
        public bool EnableTemporalFusion { get; set; } = false;

        public GraphicsBuffer MeshVertexBuffer => _meshVertexBuffer;
        public int MeshVertexCount => _meshVertexCount;
        public int MeshLod { get; set; } = 2;
        public float Scale { get; set; } = 1.0f;
        public float MinDepth { get; set; } = 0.1f;
        public float MaxDepth { get; set; } = 3.0f;
        public float DepthThreshold { get; set; } = 0.05f;
        public float DitherRange { get; set; } = 0.04f;

        private RenderingMode _mode = RenderingMode.Points;
        public RenderingMode Mode
        {
            get => _mode;
            set
            {
                if (_mode != value)
                {
                    _mode = value;
                    if (_mode == RenderingMode.Mesh && _processedFrameCount > 0 && _depthRingArray != null)
                    {
                        DispatchGenerateMesh(_writeRingIndex, _lastMetadata);
                    }
                }
            }
        }

        private int _anchors = 6;
        public int Anchors
        {
            get => _anchors;
            set => _anchors = Mathf.Clamp(value, 1, _ringSize);
        }

        private int _committedKeyframeCount = 0;

        public void ClearKeyframes()
        {
            _writeRingIndex = 0;
            _committedKeyframeCount = 0;
        }

        public void Initialize(int maxRgbWidth = 1440, int maxRgbHeight = 960, int ringSize = 16)
        {
            _maxRgbWidth = maxRgbWidth;
            _maxHeight = maxRgbHeight;
            _ringSize = Mathf.Max(ringSize, 2);

            _computeShader = Resources.Load<ComputeShader>("Record3DProcessor");
            if (_computeShader == null)
            {
                Record3DLogger.GPU("Failed to load 'Record3DProcessor.compute' from Resources!", Record3DLogLevel.Error);
                return;
            }

            _kSplitAndDecode = _computeShader.FindKernel("K_SplitAndDecode");
            _kFilterAndConfidence = _computeShader.FindKernel("K_FilterAndConfidence");
            _kTemporalFusion = _computeShader.FindKernel("K_TemporalFusion");
            _kGenerateMesh = _computeShader.FindKernel("K_GenerateMesh");

            CreateFixedGpuBuffers();
            Record3DLogger.GPU($"GPU Processor initialized: Fixed Max Buffer {_maxRgbWidth}x{_maxHeight} (Depth R32F), Ring Size = {_ringSize}");
        }

        private void CreateFixedGpuBuffers()
        {
            // Release existing if any
            DisposeBuffers();

            // 1. Fixed RGB Texture2DArray [N x H x W]
            var rgbDesc = new RenderTextureDescriptor(_maxRgbWidth, _maxHeight, GraphicsFormat.R8G8B8A8_UNorm, 0)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = _ringSize,
                enableRandomWrite = true,
                msaaSamples = 1,
                sRGB = false
            };
            _rgbRingArray = new RenderTexture(rgbDesc)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "Record3D_RGB_RingArray"
            };
            _rgbRingArray.Create();

            // 2. Fixed Depth Texture2DArray [N x H x W] (metric float meters, R32F format matching RWTexture2DArray<float> r32f)
            var depthDesc = new RenderTextureDescriptor(_maxRgbWidth, _maxHeight, GraphicsFormat.R32_SFloat, 0)
            {
                dimension = TextureDimension.Tex2DArray,
                volumeDepth = _ringSize,
                enableRandomWrite = true,
                msaaSamples = 1
            };
            _depthRingArray = new RenderTexture(depthDesc)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Record3D_Depth_RingArray"
            };
            _depthRingArray.Create();

            // 3. FrameMetadata GraphicsBuffer [N]
            int structSize = Marshal.SizeOf<GPUFrameMetadata>();
            _frameMetadataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, _ringSize, structSize);
            _metadataArray = new GPUFrameMetadata[_ringSize];
        }

        public void ProcessFrame(Texture sourceVideoTex, Record3DMetadataInfo metadata)
        {
            if (sourceVideoTex == null || _computeShader == null)
                return;

            // 1. RoomScan sliding window buffer:
            // PointCloud & SolidMesh modes: strictly 0 history, live frame only (slice 0).
            // RoomScan mode: FIFO sliding window ring buffer of the latest _anchors ingested frames.
            // Older frames naturally expire and are overwritten.
            if (Mode == RenderingMode.RoomScan)
            {
                int bufferSize = Mathf.Clamp(_anchors, 1, _ringSize);
                _writeRingIndex = (_writeRingIndex + 1) % bufferSize;
                _committedKeyframeCount = Mathf.Min(_committedKeyframeCount + 1, bufferSize);
            }
            else
            {
                _writeRingIndex = 0;
                _committedKeyframeCount = 0;
            }

            int writeSlice = _writeRingIndex;
            int prevSlice = (writeSlice - 1 + _ringSize) % _ringSize;

            // 2. Dynamic resolution of incoming SBS video
            int sourceW = sourceVideoTex.width;
            int sourceH = sourceVideoTex.height;
            int rgbW = sourceW / 2;
            int rgbH = sourceH;

            // Clamp safely to fixed max dimensions
            rgbW = Mathf.Clamp(rgbW, 1, _maxRgbWidth);
            rgbH = Mathf.Clamp(rgbH, 1, _maxHeight);

            if (_processedFrameCount == 0 || _currentRgbWidth != rgbW || _currentRgbHeight != rgbH)
            {
                Record3DLogger.GPU($"Resolution: {rgbW}x{rgbH} (SBS Source: {sourceW}x{sourceH}). Active UV Rect: ({rgbW}/{(float)_maxRgbWidth:F2}, {rgbH}/{(float)_maxHeight:F2})");
            }

            _currentRgbWidth = rgbW;
            _currentRgbHeight = rgbH;
            _currentUnprojection = metadata.unprojection;
            _currentPoseMatrix = metadata.poseMatrix;

            // 3. Update GPU FrameMetadata buffer (192 bytes per slice, column-major matches HLSL)
            Matrix4x4 pose = metadata.poseMatrix;
            Matrix4x4 invPose = pose.inverse;

            _metadataArray[writeSlice] = new GPUFrameMetadata
            {
                poseMatrix = pose,
                invPoseMatrix = invPose,
                unprojection = metadata.unprojection,
                cameraPos = new Vector4(metadata.cameraPosition.x, metadata.cameraPosition.y, metadata.cameraPosition.z, 1.0f),
                resolution = new Vector4(rgbW, rgbH, _maxRgbWidth, _maxHeight),
                minDepth = metadata.minDepth > 0.0f ? metadata.minDepth : 0.1f,
                maxDepth = metadata.maxDepth > 0.0f ? metadata.maxDepth : 2.8f,
                frameIndex = _processedFrameCount,
                pad = 0f
            };
            _frameMetadataBuffer.SetData(_metadataArray, writeSlice, writeSlice, 1);

            // 4. Compute Shader Pass: SBS Split & Depth Decode (100% faithful to Record3D official JS)
            int groupsX = Mathf.CeilToInt(rgbW / 8.0f);
            int groupsY = Mathf.CeilToInt(rgbH / 8.0f);

            _computeShader.SetTexture(_kSplitAndDecode, "_SourceVideoTex", sourceVideoTex);
            _computeShader.SetTexture(_kSplitAndDecode, "_RgbRingArray", _rgbRingArray);
            _computeShader.SetTexture(_kSplitAndDecode, "_DepthRingArray", _depthRingArray);
            _computeShader.SetInt("_SourceWidth", sourceW);
            _computeShader.SetInt("_SourceHeight", sourceH);
            _computeShader.SetInt("_RgbWidth", rgbW);
            _computeShader.SetInt("_RgbHeight", rgbH);
            _computeShader.SetInt("_SliceIndex", writeSlice);
            _computeShader.SetFloat("_MinDepth", metadata.minDepth > 0.0f ? metadata.minDepth : 0.1f);
            _computeShader.SetFloat("_MaxDepth", metadata.maxDepth > 0.0f ? metadata.maxDepth : 2.8f);
            _computeShader.SetInt("_FlipY", FlipY ? 1 : 0);

            _computeShader.Dispatch(_kSplitAndDecode, groupsX, groupsY, 1);

            // The WebRTC package owns sourceVideoTex and replaces/destroys it when the sender
            // changes resolution. Fence the asynchronous compute read so the receiver can wait
            // exactly at that transition without coupling normal WebRTC and ingest rates.
            try
            {
                _sourceReadFence = Graphics.CreateGraphicsFence(
                    GraphicsFenceType.AsyncQueueSynchronisation,
                    SynchronisationStageFlags.ComputeProcessing);
                _hasPendingSourceRead = true;
            }
            catch (Exception ex)
            {
                _hasPendingSourceRead = false;
                Record3DLogger.GPU($"Could not create source-read fence: {ex.Message}", Record3DLogLevel.Warning);
            }

            // Optional multi-frame temporal fusion (if explicitly enabled)
            if (EnableTemporalFusion && _processedFrameCount > 0)
            {
                _computeShader.SetTexture(_kTemporalFusion, "_DepthRingArray", _depthRingArray);
                _computeShader.SetInt("_RgbWidth", rgbW);
                _computeShader.SetInt("_RgbHeight", rgbH);
                _computeShader.SetInt("_SliceIndex", writeSlice);
                _computeShader.SetInt("_PrevSliceIndex", prevSlice);
                _computeShader.SetFloat("_DepthThreshold", metadata.depthThreshold > 0.0f ? metadata.depthThreshold : 0.05f);

                _computeShader.Dispatch(_kTemporalFusion, groupsX, groupsY, 1);
            }

            _lastMetadata = metadata;

            // 5. Generate Stable Mesh once per incoming depth frame (only for Mesh mode)
            if (_mode == RenderingMode.Mesh)
            {
                DispatchGenerateMesh(writeSlice, metadata);
            }

            _processedFrameCount++;
        }

        public void WaitForPendingSourceRead()
        {
            if (!_hasPendingSourceRead) return;

            try
            {
                // This readback is only used when changing resolution or shutting down.
                // It is queued after the compute dispatch and WaitForCompletion really blocks
                // the CPU. WaitOnAsyncGraphicsFence only synchronizes GPU queues.
                if (SystemInfo.supportsAsyncGPUReadback && _rgbRingArray != null)
                {
                    var completion = AsyncGPUReadback.Request(_rgbRingArray, 0, 0, 1, 0, 1, 0, 1);
                    completion.WaitForCompletion();
                    if (completion.hasError)
                        Record3DLogger.GPU("GPU source-read synchronization failed.", Record3DLogLevel.Error);
                }
                else if (!_sourceReadFence.passed)
                {
                    Record3DLogger.GPU("Cannot synchronize GPU source read on this graphics device.", Record3DLogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                Record3DLogger.GPU($"Could not wait for GPU source read: {ex.Message}", Record3DLogLevel.Error);
            }
            finally
            {
                _hasPendingSourceRead = false;
            }
        }

        private void DispatchGenerateMesh(int slice, Record3DMetadataInfo metadata)
        {
            if (_kGenerateMesh < 0 || _depthRingArray == null || _computeShader == null) return;

            int lod = Mathf.Max(MeshLod, 1);
            int qWidth = Mathf.Max(1, (_currentRgbWidth - 1) / lod);
            int qHeight = Mathf.Max(1, (_currentRgbHeight - 1) / lod);
            int totalVertices = qWidth * qHeight * 6;

            EnsureMeshVertexBuffer(totalVertices);
            _meshVertexCount = totalVertices;

            _computeShader.SetBuffer(_kGenerateMesh, "_MeshVertexBuffer", _meshVertexBuffer);
            _computeShader.SetTexture(_kGenerateMesh, "_DepthRingArray", _depthRingArray);
            _computeShader.SetInt("_SliceIndex", slice);
            _computeShader.SetInt("_RgbWidth", _currentRgbWidth);
            _computeShader.SetInt("_RgbHeight", _currentRgbHeight);
            _computeShader.SetInt("_MaxRgbWidth", _maxRgbWidth);
            _computeShader.SetInt("_MaxHeight", _maxHeight);
            _computeShader.SetInt("_MeshLod", lod);
            _computeShader.SetFloat("_MinDepth", MinDepth > 0f ? MinDepth : (metadata.minDepth > 0f ? metadata.minDepth : 0.1f));
            _computeShader.SetFloat("_MaxDepth", MaxDepth > 0f ? MaxDepth : (metadata.maxDepth > 0f ? metadata.maxDepth : 3.0f));
            _computeShader.SetFloat("_DepthThreshold", DepthThreshold > 0f ? DepthThreshold : (metadata.depthThreshold > 0f ? metadata.depthThreshold : 0.05f));
            _computeShader.SetFloat("_DitherRange", DitherRange);
            _computeShader.SetFloat("_Scale", Scale);
            _computeShader.SetVector("_Unprojection", metadata.unprojection);

            int meshGroupsX = Mathf.CeilToInt(qWidth / 8.0f);
            int meshGroupsY = Mathf.CeilToInt(qHeight / 8.0f);
            _computeShader.Dispatch(_kGenerateMesh, meshGroupsX, meshGroupsY, 1);
        }

        private void EnsureMeshVertexBuffer(int count)
        {
            if (_meshVertexBuffer == null || _meshVertexBuffer.count != count)
            {
                if (_meshVertexBuffer != null)
                {
                    _meshVertexBuffer.Dispose();
                    _meshVertexBuffer = null;
                }
                int structSize = Marshal.SizeOf<GPUMeshVertex>();
                _meshVertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, structSize);
            }
        }

        private void DisposeBuffers()
        {
            if (_rgbRingArray != null)
            {
                _rgbRingArray.Release();
                UnityEngine.Object.DestroyImmediate(_rgbRingArray);
                _rgbRingArray = null;
            }

            if (_depthRingArray != null)
            {
                _depthRingArray.Release();
                UnityEngine.Object.DestroyImmediate(_depthRingArray);
                _depthRingArray = null;
            }

            if (_frameMetadataBuffer != null)
            {
                _frameMetadataBuffer.Dispose();
                _frameMetadataBuffer = null;
            }

            if (_meshVertexBuffer != null)
            {
                _meshVertexBuffer.Dispose();
                _meshVertexBuffer = null;
            }
        }

        public void Dispose()
        {
            DisposeBuffers();
        }
    }
}
