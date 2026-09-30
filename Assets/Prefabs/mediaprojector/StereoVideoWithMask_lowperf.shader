Shader "Custom/StereoVideoWithMask_LowPerformance"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Video Stream (SBS)", 2D) = "black" {}

        _UvScale ("Aspect & FOV Fix (X: Width, Y: Height)", Vector) = (1, 1, 0, 0)
        _GlobalOffset ("Global UV Offset", Vector) = (0, 0, 0, 0)
        
        _TexScale ("Texture Scale", Vector) = (1, 1, 0, 0)
        _TexOffset ("Texture Offset", Vector) = (0, 0, 0, 0)
        [Toggle] _StereoEnabled ("Enable Stereoscopic Rendering (SBS)", Float) = 1
        
        _GlobalScale ("Global Content Scale", Range(0.05, 0.5)) = 0.2
        _Aspect ("Screen Aspect Ratio (W/H)", Float) = 1.77778
        _MaskRadius ("Mask Corner Radius", Range(0.0, 0.5)) = 0.025
    
        _MaskFeather ("Mask Softness/Feather", Range(0.001, 0.3)) = 0.02
        _MaskFadeOffset ("Mask Fade Offset", Range(-0.1, 0.1)) = -0.02

        // ==========================================
        // UI & Loading Properties
        // ==========================================
        _BlendFactor ("Spinner <-> Video Blend", Range(0.0, 1.0)) = 0.0
        
        [HDR] _SpinnerColor ("Spinner Load Color (RGBA)", Color) = (0.2, 0.6, 1.0, 1.0)
        _SpinnerBgColor ("Spinner Background (RGBA)", Color) = (0.0, 0.0, 0.0, 0.8)
        
        _SpinnerSpeed ("Spinner Speed", Float) = 1.0
        _SpinnerRadius ("Spinner Circle Radius", Range(0.0, 0.5)) = 0.12
        _SpinnerDotSize ("Spinner Dot Base Size", Range(0.001, 0.05)) = 0.012
        
        // 专门修复球体 Mesh UV 拉伸的参数 (>1.0 压扁横向拉伸)
        _SpinnerAspect ("Spinner Aspect Correction (Fix Mesh Stretch)", Range(0.1, 3.0)) = 1.0 

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
                float4 _TexScale;
                float4 _TexOffset;
                float _StereoEnabled;
                half _GlobalAlpha;
                float _GlobalScale;
                half _Cutoff;

                float _Aspect;
                float _MaskRadius;
                float _MaskFeather;
                float _MaskFadeOffset;
                
                // Spinner Properties
                half _BlendFactor;
                half4 _SpinnerColor;
                half4 _SpinnerBgColor;
                float _SpinnerSpeed;
                float _SpinnerRadius;
                float _SpinnerDotSize;
                float _SpinnerAspect;
            CBUFFER_END

            // ==========================================
            // 1. Procedural Mask (优化为基于 logicUV 局部空间)
            // ==========================================
            half CalculateProceduralMask(float2 logicUV, float aspect)
            {
                // 将 0~1 的 logicUV 转换到以中心为原点的绝对空间，并修正长宽比保证圆角是正圆
                float2 p = abs(logicUV - 0.5);
                p.x *= aspect; 
                
                float2 half_size = float2(0.5 * aspect, 0.5);
                float safe_radius = min(_MaskRadius, 0.5);
                
                float2 d_vec = p - half_size + safe_radius;
                float d = min(max(d_vec.x, d_vec.y), 0.0) + length(max(d_vec, 0.0)) - safe_radius;
                
                // 此时 d 的单位是视频画面的百分比。
                // Feather = 0.02 意味着永远是视频高度的 2%，完美跟随缩放和平移！
                return (half)(1.0 - smoothstep(-_MaskFeather + _MaskFadeOffset, _MaskFeather + _MaskFadeOffset, d));
            }

            // ==========================================
            // 2. Ultra-Low Power Loading Spinner 
            // ==========================================
            half4 GenerateLoadingSpinner(float2 centeredSpinnerUV, float time)
            {
                float c = 0.0;
                float speed = time * _SpinnerSpeed;

                UNITY_UNROLL
                for (int i = 0; i < 12; i++)
                {
                    float angle = (float)i * 0.52359877; 
                    float cosA, sinA;
                    sincos(angle, sinA, cosA);
                    float2 dotCenter = float2(cosA, sinA) * _SpinnerRadius;

                    float dist = length(centeredSpinnerUV - dotCenter);
                    float phase = frac(-(float)i / 12.0 - speed);
                    float currentDotSize = _SpinnerDotSize * (0.3 + 0.7 * phase);
                    float dotIntensity = smoothstep(currentDotSize, currentDotSize * 0.1, dist);

                    c += dotIntensity * phase;
                }

                half3 finalRGB = lerp(_SpinnerBgColor.rgb, _SpinnerColor.rgb, saturate(c));
                half finalAlpha = saturate(_SpinnerBgColor.a + (c * _SpinnerColor.a));
                return half4(finalRGB, finalAlpha);
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

                // 1. 全局缩放 (Global Scale)
                centeredUV /= _GlobalScale;
                
                // 2. 全局平移 (Global Offset)
                float2 baseOffsetUV = centeredUV + _GlobalOffset.xy;
                
                // 3. 视频局部逻辑坐标 (Video Logic UV) - 用于视频和 Mask
                float2 videoCenteredUV = baseOffsetUV * _UvScale.xy;
                float2 logicUV = videoCenteredUV + 0.5;
                
                // 4. Procedural Mask 
                // 传入 logicUV。因为 logicUV 已经被缩放和平移过，
                // 所以 Mask 现在像相框一样死死咬住视频边界，Feather 也会等比缩放。
                half proceduralMask = CalculateProceduralMask(logicUV, _Aspect);
                half edgeMask = (half)(step(0.0, logicUV.x) * step(logicUV.x, 1.0) * step(0.0, logicUV.y) * step(logicUV.y, 1.0));

                // 5. Loading Spinner 逻辑
                // 使用没被 _UvScale (视频画面长宽比) 污染过的基础偏移坐标
                float2 spinnerUV = baseOffsetUV;
                
                // [核心修复] 对抗球体 Mesh 的 UV 拉伸。
                // 如果球体让圆变成了横向椭圆，增大这个值即可压扁回圆形。
                spinnerUV.x *= _SpinnerAspect; 

                half4 spinnerColor = _SpinnerBgColor;
                UNITY_BRANCH 
                if (_BlendFactor < 0.995) 
                {
                    spinnerColor = GenerateLoadingSpinner(spinnerUV, _Time.y);
                }

                // 6. 视频采样与双目/单目模式控制 (支持 SBS 双目与非 SBS 单目)
                float2 scaledOffsetUV = logicUV * _TexScale.xy + _TexOffset.xy;
                float2 videoSampleUV = scaledOffsetUV;
                
                UNITY_BRANCH
                if (_StereoEnabled > 0.5)
                {
                    videoSampleUV.x = videoSampleUV.x * 0.5 + (eyeIndex == 0 ? 0.0 : 0.5);
                }
                
                half4 videoColor = tex2D(_MainTex, videoSampleUV);

                // 7. Core Blend
                half3 blendedRGB = lerp(spinnerColor.rgb, videoColor.rgb, _BlendFactor);
                half blendedAlpha = lerp(spinnerColor.a, videoColor.a, _BlendFactor);

                half finalAlpha = blendedAlpha * proceduralMask * edgeMask * _GlobalAlpha;
                clip(finalAlpha - _Cutoff);
                
                return half4(blendedRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}