using System;
using UnityEngine;

namespace Record3D
{
    public interface IRecord3DRenderer : IDisposable
    {
        RenderingMode Mode { get; set; }
        float Scale { get; set; }
        float MinDepth { get; set; }
        float MaxDepth { get; set; }
        float DepthThreshold { get; set; }
        bool UseDevicePose { get; set; }

        // PointCloud Mode parameters
        float PointSize { get; set; }
        int PointStep { get; set; }

        // Mesh Mode parameters
        int MeshLod { get; set; }
        bool EnableDithering { get; set; }
        float DitherRange { get; set; }
        float DitherIntensity { get; set; }

        // RoomScan Mode parameters
        int RoomScanAnchors { get; set; }
        int RoomScanMeshLod { get; set; }
        float RoomScanDepthThreshold { get; set; }
        Color FresnelLightColor { get; set; }
        Color FresnelDarkColor { get; set; }

        int LastVertexCount { get; }
        void Render(IRecord3DGpuProcessor processor, Transform anchorTransform);
    }

    /// <summary>
    /// Decoupled orchestrator for rendering PointCloud, Solid Mesh, and RoomScan.
    /// Each mode uses its own dedicated shader, material, and draw invocation.
    /// </summary>
    public class Record3DRenderer : IRecord3DRenderer
    {
        private RenderingMode _mode = RenderingMode.Points;
        private float _scale = 1.0f;
        private float _minDepth = 0.1f;
        private float _maxDepth = 2.8f;
        private float _depthThreshold = 0.05f;
        private bool _useDevicePose = true;

        // Point Cloud parameters
        private float _pointSize = 3.0f;
        private int _pointStep = 2;

        // Solid Mesh parameters
        private int _meshLod = 2;
        private bool _enableDithering = true;
        private float _ditherRange = 0.05f;
        private float _ditherIntensity = 1.0f;

        // RoomScan parameters
        private int _roomScanAnchors = 6;
        private int _roomScanMeshLod = 2;
        private float _roomScanDepthThreshold = 0.08f;
        private Color _fresnelLightColor = new Color(0.75f, 0.77f, 0.80f);
        private Color _fresnelDarkColor = new Color(0.20f, 0.22f, 0.24f);

        private int _lastVertexCount = 0;

        private Material _pointCloudMaterial;
        private Material _meshMaterial;
        private Material _roomScanMaterial;

        public RenderingMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public float Scale
        {
            get => _scale;
            set => _scale = value;
        }

        public float MinDepth
        {
            get => _minDepth;
            set => _minDepth = value;
        }

        public float MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value;
        }

        public float DepthThreshold
        {
            get => _depthThreshold;
            set => _depthThreshold = value;
        }

        public bool UseDevicePose
        {
            get => _useDevicePose;
            set => _useDevicePose = value;
        }

        // PointCloud
        public float PointSize
        {
            get => _pointSize;
            set => _pointSize = value;
        }

        public int PointStep
        {
            get => _pointStep;
            set => _pointStep = Mathf.Max(1, value);
        }

        // Solid Mesh
        public int MeshLod
        {
            get => _meshLod;
            set => _meshLod = Mathf.Max(1, value);
        }

        public bool EnableDithering
        {
            get => _enableDithering;
            set => _enableDithering = value;
        }

        public float DitherRange
        {
            get => _ditherRange;
            set => _ditherRange = Mathf.Max(0.001f, value);
        }

        public float DitherIntensity
        {
            get => _ditherIntensity;
            set => _ditherIntensity = Mathf.Clamp01(value);
        }

        // RoomScan
        public int RoomScanAnchors
        {
            get => _roomScanAnchors;
            set => _roomScanAnchors = Mathf.Max(1, value);
        }

        public int RoomScanMeshLod
        {
            get => _roomScanMeshLod;
            set => _roomScanMeshLod = Mathf.Max(1, value);
        }

        public float RoomScanDepthThreshold
        {
            get => _roomScanDepthThreshold;
            set => _roomScanDepthThreshold = Mathf.Max(0.01f, value);
        }

        public Color FresnelLightColor
        {
            get => _fresnelLightColor;
            set => _fresnelLightColor = value;
        }

        public Color FresnelDarkColor
        {
            get => _fresnelDarkColor;
            set => _fresnelDarkColor = value;
        }

        public int LastVertexCount => _lastVertexCount;

        public Record3DRenderer()
        {
            InitMaterials();
        }

        private void InitMaterials()
        {
            Shader pointShader = Resources.Load<Shader>("Shaders/Record3DPointCloud") ?? Shader.Find("Record3D/PointCloud");
            if (pointShader != null)
            {
                _pointCloudMaterial = new Material(pointShader) { name = "Record3D_PointCloud_Mat" };
                Record3DLogger.GPU("Initialized Record3D PointCloud material successfully.", Record3DLogLevel.Info);
            }
            else
            {
                string err = "[Record3DRenderer] Shader 'Record3D/PointCloud' not found in Resources/Shaders or Project!";
                Debug.LogError(err);
                Record3DLogger.GPU(err, Record3DLogLevel.Error);
            }

            Shader meshShader = Resources.Load<Shader>("Shaders/Record3DMesh") ?? Shader.Find("Record3D/Mesh");
            if (meshShader != null)
            {
                _meshMaterial = new Material(meshShader) { name = "Record3D_Mesh_Mat" };
                Record3DLogger.GPU("Initialized Record3D Mesh material successfully.", Record3DLogLevel.Info);
            }
            else
            {
                string err = "[Record3DRenderer] Shader 'Record3D/Mesh' not found in Resources/Shaders or Project!";
                Debug.LogError(err);
                Record3DLogger.GPU(err, Record3DLogLevel.Error);
            }

            Shader roomScanShader = Resources.Load<Shader>("Shaders/Record3DRoomScan") ?? Shader.Find("Record3D/RoomScan");
            if (roomScanShader != null)
            {
                _roomScanMaterial = new Material(roomScanShader) { name = "Record3D_RoomScan_Mat" };
                Record3DLogger.GPU("Initialized Record3D RoomScan material successfully.", Record3DLogLevel.Info);
            }
            else
            {
                string err = "[Record3DRenderer] Shader 'Record3D/RoomScan' not found in Resources/Shaders or Project!";
                Debug.LogError(err);
                Record3DLogger.GPU(err, Record3DLogLevel.Error);
            }
        }

        public void Render(IRecord3DGpuProcessor processor, Transform anchorTransform)
        {
            if (processor == null || processor.ProcessedFrameCount == 0)
                return;

            int w = processor.CurrentRgbWidth;
            int h = processor.CurrentRgbHeight;
            if (w <= 1 || h <= 1)
                return;

            Matrix4x4 localToWorld = (anchorTransform != null) ? anchorTransform.localToWorldMatrix : Matrix4x4.identity;
            float maxW = processor.RgbRingArray != null ? processor.RgbRingArray.width : 1440f;
            float maxH = processor.RgbRingArray != null ? processor.RgbRingArray.height : 960f;

            switch (_mode)
            {
                case RenderingMode.Points:
                    RenderPointCloud(processor, localToWorld, w, h, maxW, maxH);
                    break;
                case RenderingMode.Mesh:
                    RenderSolidMesh(processor, localToWorld, w, h, maxW, maxH);
                    break;
                case RenderingMode.RoomScan:
                    RenderRoomScan(processor, localToWorld, w, h, maxW, maxH);
                    break;
            }
        }

        // =========================================================================
        // Mode 1: Point Cloud (Dedicated Pipeline)
        // =========================================================================
        private void RenderPointCloud(IRecord3DGpuProcessor processor, Matrix4x4 localToWorld, int w, int h, float maxW, float maxH)
        {
            if (_pointCloudMaterial == null)
            {
                InitMaterials();
                if (_pointCloudMaterial == null) return;
            }

            int step = Mathf.Max(1, _pointStep);
            int sampledW = (w + step - 1) / step;
            int sampledH = (h + step - 1) / step;
            int vertexCount = sampledW * sampledH;

            _pointCloudMaterial.SetTexture("_RgbRingArray", processor.RgbRingArray);
            _pointCloudMaterial.SetTexture("_DepthRingArray", processor.DepthRingArray);
            _pointCloudMaterial.SetBuffer("_FrameMetadataBuffer", processor.FrameMetadataBuffer);
            _pointCloudMaterial.SetVector("_Resolution", new Vector4(w, h, maxW, maxH));
            _pointCloudMaterial.SetVector("_Unprojection", processor.CurrentUnprojection);
            _pointCloudMaterial.SetMatrix("_LocalToWorldMatrix", localToWorld);
            _pointCloudMaterial.SetInt("_CurrentRingIndex", processor.CurrentRingIndex);
            _pointCloudMaterial.SetFloat("_Scale", _scale);
            _pointCloudMaterial.SetFloat("_MinDepth", _minDepth);
            _pointCloudMaterial.SetFloat("_MaxDepth", _maxDepth);
            _pointCloudMaterial.SetFloat("_PointSize", _pointSize);
            _pointCloudMaterial.SetFloat("_UseDevicePose", _useDevicePose ? 1.0f : 0.0f);
            _pointCloudMaterial.SetInt("_PointStep", step);

            _lastVertexCount = vertexCount;

            var renderParams = new RenderParams(_pointCloudMaterial)
            {
                worldBounds = new Bounds(localToWorld.GetColumn(3), Vector3.one * 50f)
            };
            Graphics.RenderPrimitives(renderParams, MeshTopology.Points, vertexCount, 1);
        }

        // =========================================================================
        // Mode 2: Solid Mesh (Dedicated Pipeline)
        // Precomputed stable mesh buffer is generated once per depth frame on GPU.
        // At 72/90/120Hz XR refresh rate, only MVP transform and live RGB sampling are done.
        // =========================================================================
        private void RenderSolidMesh(IRecord3DGpuProcessor processor, Matrix4x4 localToWorld, int w, int h, float maxW, float maxH)
        {
            if (_meshMaterial == null)
            {
                InitMaterials();
                if (_meshMaterial == null) return;
            }

            if (processor.MeshVertexBuffer == null || processor.MeshVertexCount <= 0) return;

            int vertexCount = processor.MeshVertexCount;

            // Pre-multiply object-to-world matrix once on CPU instead of per-vertex in VS
            Matrix4x4 meshToWorld = _useDevicePose ? localToWorld * processor.CurrentPoseMatrix : localToWorld;

            _meshMaterial.SetBuffer("_MeshVertexBuffer", processor.MeshVertexBuffer);
            _meshMaterial.SetTexture("_RgbRingArray", processor.RgbRingArray);
            _meshMaterial.SetMatrix("_LocalToWorldMatrix", meshToWorld);
            _meshMaterial.SetInt("_CurrentRingIndex", processor.CurrentRingIndex);
            _meshMaterial.SetFloat("_DitheringOn", _enableDithering ? 1.0f : 0.0f);
            _meshMaterial.SetFloat("_DitherIntensity", _ditherIntensity);

            _lastVertexCount = vertexCount;

            var renderParams = new RenderParams(_meshMaterial)
            {
                worldBounds = new Bounds(meshToWorld.GetColumn(3), Vector3.one * 50f)
            };
            Graphics.RenderPrimitives(renderParams, MeshTopology.Triangles, vertexCount, 1);
        }

        // =========================================================================
        // Mode 3: RoomScan (Dedicated Pipeline: Delta Update + Persistent Full Fusion)
        // =========================================================================
        private void RenderRoomScan(IRecord3DGpuProcessor processor, Matrix4x4 localToWorld, int w, int h, float maxW, float maxH)
        {
            if (_roomScanMaterial == null)
            {
                InitMaterials();
                if (_roomScanMaterial == null) return;
            }

            int lod = Mathf.Max(1, _roomScanMeshLod);
            int qWidth = Mathf.Max(1, (w - 1) / lod);
            int qHeight = Mathf.Max(1, (h - 1) / lod);
            int vertexCount = qWidth * qHeight * 6;

            // Sliding window: draw the latest frames up to _roomScanAnchors
            int totalAvailable = processor.CommittedKeyframeCount;
            int maxAnchors = Mathf.Max(1, _roomScanAnchors);
            int instanceCount = Mathf.Clamp(totalAvailable, 1, maxAnchors);

            _roomScanMaterial.SetTexture("_RgbRingArray", processor.RgbRingArray);
            _roomScanMaterial.SetTexture("_DepthRingArray", processor.DepthRingArray);
            _roomScanMaterial.SetBuffer("_FrameMetadataBuffer", processor.FrameMetadataBuffer);
            _roomScanMaterial.SetVector("_Resolution", new Vector4(w, h, maxW, maxH));
            _roomScanMaterial.SetVector("_Unprojection", processor.CurrentUnprojection);
            _roomScanMaterial.SetMatrix("_LocalToWorldMatrix", localToWorld);
            _roomScanMaterial.SetInt("_CurrentRingIndex", processor.CurrentRingIndex);
            _roomScanMaterial.SetInt("_RingSize", maxAnchors);
            _roomScanMaterial.SetFloat("_Scale", _scale);
            _roomScanMaterial.SetFloat("_MinDepth", _minDepth);
            _roomScanMaterial.SetFloat("_MaxDepth", _maxDepth);
            _roomScanMaterial.SetFloat("_DepthThreshold", _roomScanDepthThreshold);
            _roomScanMaterial.SetFloat("_UseDevicePose", _useDevicePose ? 1.0f : 0.0f);
            _roomScanMaterial.SetFloat("_RoomScanThreshold", _roomScanDepthThreshold);
            _roomScanMaterial.SetColor("_FresnelLightColor", _fresnelLightColor);
            _roomScanMaterial.SetColor("_FresnelDarkColor", _fresnelDarkColor);
            _roomScanMaterial.SetInt("_MeshLod", lod);

            _lastVertexCount = vertexCount * instanceCount;

            var renderParams = new RenderParams(_roomScanMaterial)
            {
                worldBounds = new Bounds(localToWorld.GetColumn(3), Vector3.one * 50f)
            };
            Graphics.RenderPrimitives(renderParams, MeshTopology.Triangles, vertexCount, instanceCount);
        }

        public void Dispose()
        {
            if (_pointCloudMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_pointCloudMaterial);
                _pointCloudMaterial = null;
            }

            if (_meshMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_meshMaterial);
                _meshMaterial = null;
            }

            if (_roomScanMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_roomScanMaterial);
                _roomScanMaterial = null;
            }
        }
    }
}
