Shader "Custom/StereoVideoStream"
{
    Properties
    {
        [Header(Smooth Transitions)]
        _VideoFade ("Video Opacity (0=Hidden, 1=Playing)", Range(0.0, 1.0)) = 0.0
        _LoadingFade ("Spinner Opacity (0=Hidden, 1=Loading)", Range(0.0, 1.0)) = 0.0
        _RevealProgress ("Reveal Progress (0=Loading, 1=Video)", Range(0.0, 1.0)) = 0.0
        
        [Header(Video Stream)]
        [NoScaleOffset] _MainTex ("Video Stream (SBS)", 2D) = "black" {}
        _TexScale ("Texture Scale", Vector) = (1, 1, 0, 0)
        _TexOffset ("Texture Offset", Vector) = (0, 0, 0, 0)
        [Toggle] _StereoEnabled ("Enable SBS", Float) = 1
        
        [Header(Procedural Mask)]
        [HideInInspector] _Aspect ("Screen Aspect Ratio (W/H)", Float) = 1.77778
        _MaskRadius ("Mask Corner Radius", Range(0.0, 0.5)) = 0.025
        _MaskFeather ("Mask Softness", Range(0.001, 0.3)) = 0.02
        _MaskFadeOffset ("Mask Fade Offset", Range(-0.1, 0.1)) = -0.02
        
        [Header(Standardized Plasma Gradient)]
        [HDR] _PlasmaColor1 ("Plasma Color 1", Color) = (0, 0.2890874, 1.9406, 1)
        [HDR] _PlasmaColor2 ("Plasma Color 2", Color) = (1.404652, 1.502217, 1.672956, 1)
        [HDR] _PlasmaColor3 ("Plasma Color 3", Color) = (0.01666667, 0.01960784, 0.044, 1)
        _PlasmaContrast ("Plasma Contrast", Range(1.0, 10.0)) = 3
        _GradientUvScale ("Gradient UV Scale (X: U, Y: V)", Vector) = (1, 1, 0, 0)
        _Speed ("Animation Speed", Float) = 0.3
        
        [Header(Loading Spinner)]
        [HDR] _SpinnerColor ("Spinner Load Color", Color) = (0.2, 0.6, 1.0, 1.0)
        _SpinnerBgColor ("Spinner Background", Color) = (0.0, 0.0, 0.0, 0.8)
        _SpinnerSpeed ("Spinner Speed", Float) = 1.0
        _SpinnerRadius ("Spinner Circle Radius", Range(0.0, 0.5)) = 0.12
        _SpinnerDotSize ("Spinner Dot Base Size", Range(0.001, 0.05)) = 0.012
        _SpinnerAspect ("Spinner Aspect Correction", Range(0.1, 3.0)) = 1.0 

        [Header(Global Rendering)]
        _GlobalUvScale ("Global UV Scale", Vector) = (1, 1, 0, 0)
        _GlobalOffset ("Global UV Offset", Vector) = (0, 0, 0, 0)
        _GlobalScale ("Global Content Scale", Range(0.05, 1.0)) = 0.2
        _GlobalAlpha ("Global Alpha", Range(0, 1)) = 1.0
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.005
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0 
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull Mode", Float) = 2
    }
    
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
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
                float4 _TexScale;
                float4 _TexOffset;
                float _StereoEnabled;
                
                float _VideoFade;
                float _LoadingFade;
                float _RevealProgress;

                float _Aspect;
                float _MaskRadius;
                float _MaskFeather;
                float _MaskFadeOffset;
                
                half4 _PlasmaColor1;
                half4 _PlasmaColor2;
                half4 _PlasmaColor3;
                float _PlasmaContrast;
                float4 _GradientUvScale;
                float _Speed;

                half4 _SpinnerColor;
                half4 _SpinnerBgColor;
                float _SpinnerSpeed;
                float _SpinnerRadius;
                float _SpinnerDotSize;
                float _SpinnerAspect;

                float4 _GlobalUvScale;
                float4 _GlobalOffset;
                float _GlobalScale;
                half _GlobalAlpha;
                half _Cutoff;
            CBUFFER_END

            // =====================================
            // 1. Procedural Mask 
            // =====================================
            half CalculateProceduralMask(float2 logicUV, float aspect)
            {
                float2 p = abs(logicUV - 0.5);
                p.x *= aspect;
                float2 half_size = float2(0.5 * aspect, 0.5);
                float safe_radius = min(_MaskRadius, 0.5);
                float2 d_vec = p - half_size + safe_radius;
                float d = min(max(d_vec.x, d_vec.y), 0.0) + length(max(d_vec, 0.0)) - safe_radius;
                return (half)(1.0 - smoothstep(-_MaskFeather + _MaskFadeOffset, _MaskFeather + _MaskFadeOffset, d));
            }

            // =====================================
            // 2. 解耦修复版：高对比度全通道动态渐变
            // =====================================
            half4 GenerateYourGradient(float2 logicUV, float time)
            {
                float2 uv = (logicUV - 0.5) * 2.0 * _GradientUvScale.xy;

                float t = time * _Speed;
                float d = -t * 0.5;
                float a = 0.0;

                [unroll]
                for (int i = 0; i < 8; ++i) 
                {
                    float fi = (float)i;
                    a += cos(fi - d - a * uv.x);
                    d += sin(uv.y * fi + a);
                }
                d += t * 0.5;

                float2 cosUV = cos(uv * float2(d, a)) * 0.6 + 0.4;
                float cosAD = cos(a + d) * 0.5 + 0.5;
                float3 col = float3(cosUV.x, cosUV.y, cosAD);
                
                col = cos(col * cos(float3(d, a, 2.5)) * 0.5 + 0.5);

                // 提取原始硬核暗部遮罩，仅作用于 RGB，产生死寂的纯黑深谷
                float brightness = max(col.r, max(col.g, col.b));
                float shadowMask = saturate(brightness);

                // 计算非线性色彩权重硬化
                float3 weights = saturate(col);
                weights = pow(weights, _PlasmaContrast);
                
                float totalWeight = weights.x + weights.z + weights.y + 0.0001;
                weights /= totalWeight;

                // 1. RGB 端：乘以 shadowMask，强制保留高对比度、边界凌厉的纯黑暗部
                half3 finalRGB = (weights.r * _PlasmaColor1.rgb + 
                                 weights.g * _PlasmaColor2.rgb + 
                                 weights.b * _PlasmaColor3.rgb) * shadowMask;

                // 2. Alpha 端【核心修正】：采用纯粹的标准化权重混合，绝不乘以 shadowMask
                // 这样暗部依然拥有完整的透明度（默认全齐不透明），防止背景漏过来摧毁对比度
                half finalAlpha = (weights.r * _PlasmaColor1.a + 
                                   weights.g * _PlasmaColor2.a + 
                                   weights.b * _PlasmaColor3.a);

                return half4(finalRGB, finalAlpha);
            }

            // =====================================
            // 3. Loading Spinner 
            // =====================================
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

            // =====================================
            // 4. 片元着色器合并
            // =====================================
            half4 frag (Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 centeredUV = input.uv - 0.5;
                centeredUV /= _GlobalScale;
                float2 baseOffsetUV = centeredUV + _GlobalOffset.xy;
                
                float2 videoCenteredUV = baseOffsetUV * _GlobalUvScale.xy;
                float2 logicUV = videoCenteredUV + 0.5;
                
                // Simple aspect ratio mapping: effectiveAspect = 1.7 * (scaleY / scaleX)
                float effectiveAspect = 1.7 * (_GlobalUvScale.y / max(_GlobalUvScale.x, 0.0001));

                half proceduralMask = CalculateProceduralMask(logicUV, effectiveAspect);
                half edgeMask = (half)(step(0.0, logicUV.x) * step(logicUV.x, 1.0) * step(0.0, logicUV.y) * step(logicUV.y, 1.0));
                
                float2 screenCenterUV = logicUV - 0.5;
                float dist = length(screenCenterUV * float2(effectiveAspect, 1.0));

                float revealProgress = _RevealProgress;
                float t_spin = saturate(revealProgress / 0.3);
                float t_reveal = saturate((revealProgress - 0.3) / 0.7);

                float maxRadius = 0.55 * sqrt(effectiveAspect * effectiveAspect + 1.0);
                float currentRadius = maxRadius * t_reveal;
                float feather = 0.05;

                half4 videoColor = half4(0, 0, 0, 0);
                UNITY_BRANCH
                if (t_reveal > 0.0)
                {
                    float2 eyeOffset = _TexOffset.xy;
                    UNITY_BRANCH
                    if (_StereoEnabled > 0.5 && unity_StereoEyeIndex != 0)
                    {
                        eyeOffset.x = 1.0 - _TexScale.x - eyeOffset.x;
                    }
                    float2 scaledOffsetUV = logicUV * _TexScale.xy + eyeOffset;
                    float2 videoSampleUV = scaledOffsetUV;
                    
                    UNITY_BRANCH
                    if (_StereoEnabled > 0.5)
                    {
                        videoSampleUV.x = videoSampleUV.x * 0.5 + (unity_StereoEyeIndex == 0 ? 0.0 : 0.5);
                    }
                    
                    videoColor = tex2D(_MainTex, videoSampleUV);
                }

                half4 loadingColor = half4(0, 0, 0, 0);
                UNITY_BRANCH
                if (t_reveal < 0.999 && dist > (currentRadius - feather))
                {
                    half4 gradColor = GenerateYourGradient(logicUV, _Time.y);
                    loadingColor = gradColor;

                    UNITY_BRANCH
                    if (_LoadingFade > 0.01 && t_spin < 0.999)
                    {
                        float2 spinnerUV = baseOffsetUV;
                        spinnerUV.x *= _SpinnerAspect;

                        float spinnerScale = 1.0 - t_spin;
                        float curSpinnerRadius = _SpinnerRadius * spinnerScale;
                        float curSpinnerDotSize = _SpinnerDotSize * spinnerScale;
                        half4 curSpinnerBgColor = _SpinnerBgColor;
                        curSpinnerBgColor.a *= spinnerScale;

                        half c = 0.0;
                        float speed = _Time.y * _SpinnerSpeed;

                        UNITY_UNROLL
                        for (int i = 0; i < 12; i++)
                        {
                            float angle = (float)i * 0.52359877;
                            float cosA, sinA;
                            sincos(angle, sinA, cosA);
                            float2 dotCenter = float2(cosA, sinA) * curSpinnerRadius;
                            float dotDist = length(spinnerUV - dotCenter);
                            float phase = frac(-(float)i / 12.0 - speed);
                            float currentDotSize = curSpinnerDotSize * (0.3 + 0.7 * phase);
                            float dotIntensity = smoothstep(currentDotSize, currentDotSize * 0.1, dotDist);
                            c += dotIntensity * phase;
                        }
                        half3 spinnerRGB = lerp(curSpinnerBgColor.rgb, _SpinnerColor.rgb, saturate(c));
                        half spinnerAlpha = saturate(curSpinnerBgColor.a + (c * _SpinnerColor.a));
                        half4 spinnerColor = half4(spinnerRGB, spinnerAlpha);
                        
                        loadingColor.rgb = lerp(loadingColor.rgb, spinnerColor.rgb, spinnerColor.a * _LoadingFade);
                        loadingColor.a = saturate(loadingColor.a + spinnerColor.a * _LoadingFade);
                    }
                }

                half3 finalRGB = half3(0, 0, 0);
                half finalAlpha = 0.0;

                UNITY_BRANCH
                if (t_reveal <= 0.0)
                {
                    finalRGB = loadingColor.rgb;
                    finalAlpha = loadingColor.a;
                }
                else UNITY_BRANCH if (t_reveal >= 0.999)
                {
                    finalRGB = videoColor.rgb;
                    finalAlpha = videoColor.a;
                }
                else
                {
                    float revealMask = smoothstep(currentRadius - feather, currentRadius, dist);
                    finalRGB = lerp(videoColor.rgb, loadingColor.rgb, revealMask);
                    finalAlpha = lerp(videoColor.a, loadingColor.a, revealMask);
                }

                finalAlpha = finalAlpha * proceduralMask * edgeMask * _GlobalAlpha;
                clip(finalAlpha - _Cutoff);
                
                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
}