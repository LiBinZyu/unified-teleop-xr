Shader "Record3D/RoomScan"
{
    Properties
    {
        _MinDepth ("Min Depth", Float) = 0.1
        _MaxDepth ("Max Depth", Float) = 2.8
        _DepthThreshold ("Depth Edge Threshold", Float) = 0.08
        _Scale ("Scale", Float) = 1.0
        _MeshLod ("Mesh LOD Step", Int) = 2
        _RoomScanThreshold ("Deduplication Threshold", Float) = 0.08
        _FresnelLightColor ("Fresnel Light Color", Color) = (0.75, 0.77, 0.80, 1.0)
        _FresnelDarkColor ("Fresnel Dark Color", Color) = (0.20, 0.22, 0.24, 1.0)
    }
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
        }
        LOD 100
        Cull Back
        ZWrite On

        Pass
        {
            Name "RoomScanPass"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct GPUFrameMetadata
            {
                float4x4 poseMatrix;
                float4x4 invPoseMatrix;
                float4 unprojection;
                float4 cameraPos;
                float4 resolution;
                float minDepth;
                float maxDepth;
                uint frameIndex;
                float pad;
            };

            StructuredBuffer<GPUFrameMetadata> _FrameMetadataBuffer;

            Texture2DArray<float4> _RgbRingArray;
            SamplerState sampler_RgbRingArray;
            Texture2DArray<float> _DepthRingArray;

            float4 _Resolution;     // x: rgbWidth, y: rgbHeight, z: maxW, w: maxH
            float4 _Unprojection;   // (ifx, ify, itx, ity)
            float4x4 _LocalToWorldMatrix;
            int _CurrentRingIndex;
            int _RingSize;
            int _MeshLod;
            float _MinDepth;
            float _MaxDepth;
            float _DepthThreshold;
            float _Scale;
            float _UseDevicePose;
            float _RoomScanThreshold;
            float4 _FresnelLightColor;
            float4 _FresnelDarkColor;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;         // Live UV coordinates
                float3 worldPos : TEXCOORD1;
                float3 anchorPos : TEXCOORD2;
                float isLiveFrame : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Instance 0 = Live Delta Frame (_CurrentRingIndex)
                // Instance 1..K = Persistent Keyframe Anchors in GPU Ring Buffer
                int sliceIndex = _CurrentRingIndex;
                if (input.instanceID > 0)
                {
                    sliceIndex = (_CurrentRingIndex - (int)input.instanceID + _RingSize) % _RingSize;
                }

                GPUFrameMetadata meta = _FrameMetadataBuffer[sliceIndex];

                uint width = max((uint)meta.resolution.x, 1);
                uint height = max((uint)meta.resolution.y, 1);
                uint lod = max((uint)_MeshLod, 1);

                uint qWidth = max(1u, (width - 1) / lod);
                uint qHeight = max(1u, (height - 1) / lod);

                uint quadIndex = input.vertexID / 6;
                uint vInQuad = input.vertexID % 6;

                uint qx = quadIndex % qWidth;
                uint qy = quadIndex / qWidth;

                if (qy >= qHeight)
                {
                    output.positionCS = float4(0, 0, 2.0, 1.0);
                    return output;
                }

                uint px = qx * lod;
                uint py = qy * lod;

                // Triangle 0: tl, bl, tr. Triangle 1: bl, br, tr
                static const uint2 kOffsets[6] = {
                    uint2(0, 0), uint2(0, 1), uint2(1, 0),
                    uint2(0, 1), uint2(1, 1), uint2(1, 0)
                };

                uint2 pt = min(uint2(px, py) + kOffsets[vInQuad] * lod, uint2(width - 1, height - 1));

                float depth = _DepthRingArray.Load(int4(pt.x, pt.y, sliceIndex, 0));
                if (depth < meta.minDepth || depth >= meta.maxDepth)
                {
                    output.positionCS = float4(0, 0, 2.0, 1.0);
                    return output;
                }

                // Quad stretch cut - rejects non-continuous boundary stretch
                uint2 c00 = uint2(px, py);
                uint2 c10 = min(uint2(px + lod, py), uint2(width - 1, height - 1));
                uint2 c01 = min(uint2(px, py + lod), uint2(width - 1, height - 1));
                uint2 c11 = min(uint2(px + lod, py + lod), uint2(width - 1, height - 1));

                float d00 = _DepthRingArray.Load(int4(c00.x, c00.y, sliceIndex, 0));
                float d10 = _DepthRingArray.Load(int4(c10.x, c10.y, sliceIndex, 0));
                float d01 = _DepthRingArray.Load(int4(c01.x, c01.y, sliceIndex, 0));
                float d11 = _DepthRingArray.Load(int4(c11.x, c11.y, sliceIndex, 0));

                float hardCut = _DepthThreshold * (1.8 + float(lod) * 0.25);
                if (d00 < meta.minDepth || d10 < meta.minDepth || d01 < meta.minDepth || d11 < meta.minDepth ||
                    abs(d00 - d10) > hardCut || abs(d00 - d01) > hardCut || abs(d10 - d11) > hardCut)
                {
                    output.positionCS = float4(0, 0, 2.0, 1.0);
                    return output;
                }

                // Unproject point to camera 3D space of this slice
                float3 camPos = float3(
                    (meta.unprojection.x * float(pt.x) + meta.unprojection.z) * depth,
                    -(meta.unprojection.y * float(pt.y) + meta.unprojection.w) * depth,
                    depth
                ) * _Scale;

                // Transform to persistent anchor space using this slice's camera pose
                float4x4 pose = _UseDevicePose > 0.5 ? meta.poseMatrix : float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
                float4 anchorPos = mul(pose, float4(camPos, 1.0));
                float4 worldPos = mul(_LocalToWorldMatrix, anchorPos);

                output.positionCS = TransformWorldToHClip(worldPos.xyz);
                output.worldPos = worldPos.xyz;
                output.anchorPos = anchorPos.xyz;
                output.isLiveFrame = (input.instanceID == 0) ? 1.0 : 0.0;
                output.uv = (float2(pt) + 0.5) / meta.resolution.zw;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                GPUFrameMetadata currMeta = _FrameMetadataBuffer[_CurrentRingIndex];

                // 1. Delta Update (Live Frame): Render with direct live video texture
                if (input.isLiveFrame > 0.5)
                {
                    float4 liveRgb = _RgbRingArray.Sample(sampler_RgbRingArray, float3(input.uv, _CurrentRingIndex));
                    return float4(liveRgb.rgb, 1.0);
                }

                // 2. Full Fusion (Persistent Keyframe Anchors):
                // Project onto current camera frame to check visibility and occlusions
                float4x4 currInv = _UseDevicePose > 0.5 ? currMeta.invPoseMatrix : float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
                float3 pCurr = mul(currInv, float4(input.anchorPos, 1.0)).xyz;

                if (pCurr.z > 0.05)
                {
                    float nx = pCurr.x / pCurr.z;
                    float ny = pCurr.y / pCurr.z;
                    float pxC = (nx - currMeta.unprojection.z) / currMeta.unprojection.x;
                    float pyC = (-ny - currMeta.unprojection.w) / currMeta.unprojection.y;

                    if (pxC >= 0.0 && pxC < currMeta.resolution.x &&
                        pyC >= 0.0 && pyC < currMeta.resolution.y)
                    {
                        int ix = (int)pxC;
                        int iy = (int)pyC;
                        float dCurr = _DepthRingArray.Load(int4(ix, iy, _CurrentRingIndex, 0));
                        if (dCurr >= currMeta.minDepth && dCurr <= currMeta.maxDepth)
                        {
                            // If live camera is actively observing this surface:
                            // Discard the historical keyframe pixel so live frame renders it with 100% fidelity!
                            // Completely eliminates duplicate surfaces, trailing, and ghosting blur.
                            if (pCurr.z >= (dCurr - _RoomScanThreshold))
                            {
                                discard;
                            }
                        }
                    }
                }

                // 3. Persistent Room Geometry (Outside current camera view):
                // Render historical scanned room in architectural Fresnel clay
                float3 currPhoneLocal = _UseDevicePose > 0.5 ? currMeta.cameraPos.xyz : float3(0, 0, 0);
                float3 iphoneWorldPos = mul(_LocalToWorldMatrix, float4(currPhoneLocal, 1.0)).xyz;
                float3 V = normalize(iphoneWorldPos - input.worldPos);

                float3 dX = ddx(input.worldPos);
                float3 dY = ddy(input.worldPos);
                float3 N = normalize(cross(dX, dY));
                if (dot(N, V) < 0.0) N = -N;

                float NdotV = saturate(dot(N, V));
                float fresnel = pow(1.0 - NdotV, 2.0);
                float3 clayColor = lerp(_FresnelLightColor.rgb, _FresnelDarkColor.rgb, fresnel);

                return float4(clayColor, 1.0);
            }
            ENDHLSL
        }
    }
}
