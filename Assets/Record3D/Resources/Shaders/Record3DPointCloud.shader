Shader "Record3D/PointCloud"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 3.0
        _MinDepth ("Min Depth", Float) = 0.1
        _MaxDepth ("Max Depth", Float) = 2.8
        _PointStep ("Point Downsample Step", Int) = 2
        _Scale ("Scale", Float) = 1.0
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
        Cull Off
        ZWrite On

        Pass
        {
            Name "PointCloudPass"
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
            int _PointStep;
            float _PointSize;
            float _MinDepth;
            float _MaxDepth;
            float _Scale;
            float _UseDevicePose;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float pointSize : PSIZE;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                GPUFrameMetadata meta = _FrameMetadataBuffer[_CurrentRingIndex];

                uint width = max((uint)meta.resolution.x, 1);
                uint height = max((uint)meta.resolution.y, 1);
                uint step = max((uint)_PointStep, 1);

                uint sampledW = (width + step - 1) / step;
                uint sX = input.vertexID % sampledW;
                uint sY = input.vertexID / sampledW;

                uint ptX = sX * step;
                uint ptY = sY * step;

                if (ptX >= width || ptY >= height)
                {
                    output.positionCS = float4(0, 0, 2.0, 1.0);
                    return output;
                }

                // Sample metric depth directly from live slice
                float depth = _DepthRingArray.Load(int4(ptX, ptY, _CurrentRingIndex, 0));
                if (depth < meta.minDepth || depth >= meta.maxDepth)
                {
                    output.positionCS = float4(0, 0, 2.0, 1.0);
                    return output;
                }

                // Direct pinhole unprojection to camera 3D space
                float3 camPos = float3(
                    (meta.unprojection.x * float(ptX) + meta.unprojection.z) * depth,
                    -(meta.unprojection.y * float(ptY) + meta.unprojection.w) * depth,
                    depth
                ) * _Scale;

                float4x4 pose = _UseDevicePose > 0.5 ? meta.poseMatrix : float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
                float4 anchorPos = mul(pose, float4(camPos, 1.0));
                float4 worldPos = mul(_LocalToWorldMatrix, anchorPos);

                output.positionCS = TransformWorldToHClip(worldPos.xyz);
                output.uv = (float2(ptX, ptY) + 0.5) / meta.resolution.zw;
                output.pointSize = _PointSize;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Direct sample of live video RGB texture - pure performance
                float4 rgb = _RgbRingArray.Sample(sampler_RgbRingArray, float3(input.uv, _CurrentRingIndex));
                return float4(rgb.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
