Shader "Custom/StereoVideoWithMask_Optimized_Final"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Video Stream (SBS)", 2D) = "black" {}

        _UvScale ("Aspect & FOV Fix (X: Width, Y: Height)", Vector) = (1, 1, 0, 0)
        _GlobalOffset ("Global UV Offset", Vector) = (0, 0, 0, 0)
        
        _LeftEyeOffset ("Left Eye Micro Offset", Vector) = (0, 0, 0, 0)
        _RightEyeOffset ("Right Eye Micro Offset", Vector) = (0, 0, 0, 0)
        
        _GlobalScale ("Global Content Scale", Range(0.05, 0.5)) = 0.2
        _Aspect ("Screen Aspect Ratio (W/H)", Float) = 1.77778
        _MaskRadius ("Mask Corner Radius", Range(0.0, 0.5)) = 0.025
    
        _MaskFeather ("Mask Softness/Feather", Range(0.001, 0.3)) = 0.02
        _MaskFadeOffset ("Mask Fade Offset", Range(-0.1, 0.1)) = -0.02

        _BlendFactor ("Nebula <-> Video Blend", Range(0.0, 1.0)) = 0.5
        _NebulaBrightness ("Nebula Brightness", Range(0.0, 5.0)) = 1.0
        _NebulaHueShift ("Nebula Hue Shift", Range(0.0, 1.0)) = 0.12
        _NebulaSpeed ("Nebula Speed", Range(0.0, 10.0)) = 1.0
        _NebulaDensity ("Nebula Density (UV Scale)", Range(0.1, 10.0)) = 1.0
        _GlobalAlpha ("Global Alpha Multiplier", Range(0, 1)) = 1.0

        _Cutoff ("Alpha Cutoff Threshold", Range(0.0, 1.0)) = 0.005
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0 
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 2
    }
    SubShader
    {
        Tags { 
            "RenderType"="TransparentCutout" 
            "Queue"="AlphaTest"
            "RenderPipeline"="UniversalPipeline" 
        }
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite [_ZWrite] 
        Cull [_CullMode]

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            
            CBUFFER_START(UnityPerMaterial)
                float4 _UvScale;
                float4 _GlobalOffset;
                float4 _LeftEyeOffset;
                float4 _RightEyeOffset;
                half _GlobalAlpha;
                float _GlobalScale;
                half _Cutoff;

                float _Aspect;
                float _MaskRadius;
                float _MaskFeather;
                float _MaskFadeOffset;
                half _BlendFactor;
                half _NebulaBrightness;
                half _NebulaHueShift;
                half _NebulaSpeed;
                half _NebulaDensity;
            CBUFFER_END

            // ==========================================
            // 1. Procedural Mask 
            // ==========================================
            half CalculateProceduralMask(float2 centeredUV, float totalScale, float aspect)
            {
                float2 half_size = 0.5 * totalScale * float2(aspect, 1.0);
                float safe_radius = min(_MaskRadius, min(half_size.x, half_size.y));
                float2 d_vec = abs(centeredUV) - half_size + safe_radius;
                
                float d = min(max(d_vec.x, d_vec.y), 0.0) + length(max(d_vec, 0.0)) - safe_radius;
                return (half)(1.0 - smoothstep(-_MaskFeather + _MaskFadeOffset, _MaskFeather + _MaskFadeOffset, d));
            }

            // ==========================================
            // 2. Nebula Generation (修复亮度丢失与迭代截断)
            // ==========================================
            half4 GenerateNebula(float2 uv, half brightness, half hue_shift, half speed, half density)
            {
                float2 p_uv = uv * 2.0 - 1.0;
                float timeVar = _Time.y * speed;
                float t = timeVar * 0.005;

                float si, co;
                sincos(t + 1.0, si, co);
                p_uv = mul(p_uv, float2x2(co, si, -si, co));
                
                p_uv *= density;

                float timeScaled = (timeVar + 29.0) * 60.0;
                float s = 0.0, v = 0.0;
                float3 col = float3(0.0, 0.0, 0.0);
                float3 init = float3(0.25, 0.25 + sin(timeScaled * 0.001) * 0.4, floor(timeScaled) * 0.0008);

                // 优化：循环从 50x10 降为 25x7 (移动端可接受的甜点范围)
                for (int r = 0; r < 12; r++) 
                {
                    float3 p = init + s * float3(p_uv, 0.143);
                    p.z = fmod(p.z, 2.0); // 浮点环境安全
                    
                    for (int i = 0; i < 5; i++) 
                        p = abs(p * 2.04) / dot(p, p) - 0.75;
                    
                    // 核心修复：单次累加权重翻倍，弥补循环次数减少带来的亮度损失
                    v += length(p * p) * smoothstep(0.0, 0.5, 0.9 - s) * 0.01; 
                    col += float3(v * 0.8, 1.1 - s * 0.5, 0.7 + v * 0.5) * v * 0.026; 
                    s += 0.08; // 步长翻倍，保证星云的空间纵深不变
                }

                col *= (float)brightness;
                
                float hue_rad = (float)hue_shift * 6.2831853;
                float3 k = float3(0.5773503, 0.5773503, 0.5773503); 
                
                float cosAngle, sinAngle;
                sincos(hue_rad, sinAngle, cosAngle);
                
                float3 final_col = col * cosAngle + cross(k, col) * sinAngle + k * dot(k, col) * (1.0 - cosAngle);
                final_col = max(final_col, 0.0);
                
                float alpha = max(final_col.r, max(final_col.g, final_col.b));
                // 稍微放宽一点 alpha 的阈值，防止边缘变硬
                alpha = saturate(smoothstep(0.02, 1.0, alpha));

                return half4((half3)final_col, (half)alpha);
            }

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int eyeIndex = unity_StereoEyeIndex;
                float2 centeredUV = input.uv - 0.5;

                // 1. Mask
                half proceduralMask = CalculateProceduralMask(centeredUV, _GlobalScale, _Aspect);

                // 2. UV Transforms
                centeredUV /= _GlobalScale;
                centeredUV = centeredUV * _UvScale.xy + _GlobalOffset.xy;
                
                centeredUV += (eyeIndex == 0) ? _LeftEyeOffset.xy : _RightEyeOffset.xy;
                
                float2 logicUV = centeredUV + 0.5;
                
                half edgeMask = (half)(step(0.0, logicUV.x) * step(logicUV.x, 1.0) * step(0.0, logicUV.y) * step(logicUV.y, 1.0));

                // 3a. Nebula
                half4 nebulaColor = half4(0,0,0,0);
                UNITY_BRANCH 
                if (_BlendFactor < 0.995) 
                {
                    nebulaColor = GenerateNebula(logicUV, _NebulaBrightness, _NebulaHueShift, _NebulaSpeed, _NebulaDensity);
                }

                // 3b. Video
                float2 videoUV = logicUV;
                videoUV.x = videoUV.x * 0.5 + (eyeIndex == 0 ? 0.0 : 0.5);
                half4 videoColor = tex2D(_MainTex, videoUV);

                // 3c. Core Blend
                half3 blendedRGB = lerp(nebulaColor.rgb, videoColor.rgb, _BlendFactor);
                half blendedAlpha = lerp(nebulaColor.a, videoColor.a, _BlendFactor);

                // 4. Final Comp
                half finalAlpha = blendedAlpha * proceduralMask * edgeMask * _GlobalAlpha;
                clip(finalAlpha - _Cutoff);
                
                return half4(blendedRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}