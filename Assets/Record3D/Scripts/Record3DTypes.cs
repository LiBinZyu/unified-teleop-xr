using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Record3D
{
    public enum RenderingMode
    {
        Points,   // Pure raw point cloud, zero processing, maximum mobile performance
        Mesh,     // Single-frame mesh with stylized edge/hole dithering
        RoomScan  // RoomScan multi-frame mesh reconstruction (current texture projection + Fresnel clay)
    }

    /// <summary>Video codecs advertised by the Record3D sender. No automatic fallback.</summary>
    public enum Record3DVideoCodec
    {
        VP8 = 0,
        H264ConstrainedBaseline = 1,
        H264High = 2
    }

    /// <summary>Optional repair codecs; only the exact selected mode is accepted.</summary>
    public enum Record3DVideoRepair
    {
        None = 0,
        RTX = 1,
        RTX_RED_ULPFEC = 2
    }

    /// <summary>
    /// Blittable struct passed to GPU StructuredBuffer (192 bytes, 16-byte aligned)
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUFrameMetadata
    {
        public Matrix4x4 poseMatrix;       // 64 bytes
        public Matrix4x4 invPoseMatrix;    // 64 bytes
        public Vector4 unprojection;       // 16 bytes: (ifx, ify, itx, ity)
        public Vector4 cameraPos;          // 16 bytes: (x, y, z, 1)
        public Vector4 resolution;         // 16 bytes: (rgbW, rgbH, maxW, maxH)
        public float minDepth;             // 4 bytes
        public float maxDepth;             // 4 bytes
        public uint frameIndex;            // 4 bytes
        public float pad;                  // 4 bytes
    }

    /// <summary>
    /// Precomputed stable mesh vertex stored in GPU StructuredBuffer.
    /// Calculated once per depth frame, drawn directly at 72/90Hz without depth sampling.
    /// Strictly 16-byte aligned (32 bytes total) for 100% compatibility across D3D11 and OpenGL ES std430.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUMeshVertex
    {
        public Vector4 position; // 16 bytes: xyz = 3D position, w = alpha
        public Vector4 uv;       // 16 bytes: xy = texture UV, zw = 0
    }

    /// <summary>
    /// JSON payload sent by Record3D over WebRTC DataChannel ("WebRTCData")
    /// </summary>
    [Serializable]
    public class Record3DDataChannelPayload
    {
        public long timestamp;
        public int originalDepthHeight;
        public int originalDepthWidth;
        public int originalRgbHeight;
        public int originalRgbWidth;
        public float[] pose;
        public float[] intrinsicMatrixRgb;
    }

    /// <summary>
    /// JSON payload returned by /getOffer endpoint
    /// </summary>
    [Serializable]
    public class Record3DOfferPayload
    {
        public string type;
        public string sdp;
    }

    /// <summary>
    /// JSON payload returned by /metadata endpoint
    /// </summary>
    [Serializable]
    public class Record3DDeviceMetadata
    {
        public int[] originalSize;
        public float[] K;
    }

    /// <summary>
    /// CPU representation of per-frame metadata, held cleanly without memcpy
    /// </summary>
    public struct Record3DMetadataInfo
    {
        public long timestamp;
        public uint frameIndex;
        public int ringIndex;
        public int currentRgbWidth;
        public int currentRgbHeight;
        public Vector3 cameraPosition;
        public Quaternion cameraRotation;
        public Vector4 unprojection; // (ifx, ify, itx, ity)
        public Matrix4x4 poseMatrix;
        public float minDepth;
        public float maxDepth;
        public float depthThreshold;
    }

    public interface IRecord3DGpuProcessor : IDisposable
    {
        void Initialize(int maxRgbWidth, int maxRgbHeight, int ringSize);
        void ProcessFrame(Texture sourceVideoTex, Record3DMetadataInfo metadata);
        void WaitForPendingSourceRead();
        RenderTexture RgbRingArray { get; }
        RenderTexture DepthRingArray { get; }
        GraphicsBuffer FrameMetadataBuffer { get; }
        int CurrentRingIndex { get; }
        int CurrentRgbWidth { get; }
        int CurrentRgbHeight { get; }
        Vector4 CurrentUnprojection { get; }
        Matrix4x4 CurrentPoseMatrix { get; }
        uint ProcessedFrameCount { get; }
        int RingSize { get; }
        int CommittedKeyframeCount { get; }
        int Anchors { get; set; }
        bool FlipY { get; set; }
        RenderingMode Mode { get; set; }
        GraphicsBuffer MeshVertexBuffer { get; }
        int MeshVertexCount { get; }
        int MeshLod { get; set; }
        float Scale { get; set; }
        float MinDepth { get; set; }
        float MaxDepth { get; set; }
        float DepthThreshold { get; set; }
        float DitherRange { get; set; }
        void ClearKeyframes();
    }

    public interface IRecord3DReceiver : IDisposable
    {
        event Action<Texture, Record3DMetadataInfo> OnFrameReceived;
        event Action OnVideoTextureChanging;
        bool IsConnected { get; }
        bool IsConnecting { get; }
        string DeviceAddress { get; set; }
        void StartReceiving();
        void StopReceiving();
        void Update();
    }
}
