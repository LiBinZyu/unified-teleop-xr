Shader "Record3D/Mesh"
{
    Properties
    {
        _DitheringOn ("Enable Stylized Dithering", Float) = 1.0
        _DitherIntensity ("Dithering Intensity", Range(0.0, 1.0)) = 1.0
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
            Name "MeshPass"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct GPUMeshVertex
            {
                float4 position; // xyz = position, w = alpha
                float4 uv;       // xy = uv, zw = 0
            };

            StructuredBuffer<GPUMeshVertex> _MeshVertexBuffer;

            Texture2DArray<float4> _RgbRingArray;
            SamplerState sampler_RgbRingArray;

            float4x4 _LocalToWorldMatrix;
            int _CurrentRingIndex;
            float _DitheringOn;
            float _DitherIntensity;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 4x4 Bayer Dither Matrix for mobile screen-door edge fade
            static const float kDither4x4[16] = {
                0.0625, 0.5625, 0.1875, 0.6875,
                0.8125, 0.3125, 0.9375, 0.4375,
                0.2500, 0.7500, 0.1250, 0.6250,
                1.0000, 0.5000, 0.8750, 0.3750
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Fetch precomputed stable mesh vertex from GPU buffer (0 depth samples!)
                GPUMeshVertex v = _MeshVertexBuffer[input.vertexID];

                float alpha = v.position.w;

                // Degenerate / discarded triangle: early return outside frustum
                if (alpha < -0.5)
                {
                    output.positionCS = float4(0, 0, 2.0, 1.0);
                    return output;
                }

                // Single MVP transform for XR 72/90Hz refresh rate
                float4 worldPos = mul(_LocalToWorldMatrix, float4(v.position.xyz, 1.0));
                output.positionCS = TransformWorldToHClip(worldPos.xyz);
                output.uv = v.uv.xy;
                output.color = float4(1.0, 1.0, 1.0, saturate(alpha));

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Smooth Bayer dithering clip along edges
                if (_DitheringOn > 0.5)
                {
                    uint2 pixelPos = uint2(input.positionCS.xy) % 4;
                    float ditherLimit = kDither4x4[pixelPos.y * 4 + pixelPos.x];
                    float threshold = lerp(0.01, ditherLimit, saturate(_DitherIntensity));
                    clip(input.color.a - threshold);
                }
                else
                {
                    if (input.color.a <= 0.05)
                        discard;
                }

                // Sample live RGB texture
                float4 rgb = _RgbRingArray.Sample(sampler_RgbRingArray, float3(input.uv, _CurrentRingIndex));
                return float4(rgb.rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
