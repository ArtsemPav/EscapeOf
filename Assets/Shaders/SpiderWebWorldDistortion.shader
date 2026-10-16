Shader "EscapeOf/SpiderWebWorldDistortion"
{
    Properties
    {
        _BaseMap ("Spider Web Texture", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Toggle(_ALPHA_FROM_LUMINANCE)] _AlphaFromLuminance ("Alpha From Darkness (JPG без альфы)", Float) = 0
        _AlphaBoost ("Alpha Boost", Range(0.5, 4)) = 1.2
        _RippleStrength ("Ripple Strength", Range(0, 0.05)) = 0.01
        _RippleScale ("Ripple Scale", Range(1, 100)) = 20
        _SwayStrength ("Sway Strength", Range(0, 0.05)) = 0.005
        [Toggle(_DISTORT_BACKGROUND)] _DistortBackground ("Distort Background (нужен Opaque Texture)", Float) = 0
        _BackgroundDistortion ("Background Distortion Strength", Range(0, 0.2)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _DISTORT_BACKGROUND
            #pragma shader_feature_local _ALPHA_FROM_LUMINANCE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _AlphaBoost;
                half _RippleStrength;
                half _RippleScale;
                half _SwayStrength;
                half _BackgroundDistortion;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.screenPos = ComputeScreenPos(posInputs.positionCS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float2 centered = IN.uv - 0.5;
                float dist = length(centered);

                // рябь от центра + медленное покачивание
                float ripple = sin(dist * _RippleScale - t * 4.0) * _RippleStrength;
                float2 sway = float2(sin(t * 0.8 + IN.uv.y * 3.0), cos(t * 0.6 + IN.uv.x * 3.0)) * _SwayStrength;
                float2 uv = IN.uv + ripple * centered + sway;

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

            #if _ALPHA_FROM_LUMINANCE
                // для JPEG без альфы: нити тёмные на белом фоне
                half lum = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                half strand = saturate((1.0 - lum) * _AlphaBoost);
            #else
                half strand = saturate(tex.a * _AlphaBoost);
            #endif

                half alpha = strand * _BaseColor.a;

                // Unlit: цвет нитей = текстура × тинт, без какой-либо реакции на источники света
                half3 col = tex.rgb * _BaseColor.rgb;

            #if _DISTORT_BACKGROUND
                // искажаем фон за паутиной: там, где нити нет, виден «дрожащий» мир
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 bgOffset = (ripple * centered + sway) * 10.0;
                half3 bg = SampleSceneColor(screenUV + bgOffset * _BackgroundDistortion);
                col = lerp(bg, col, alpha);
                return half4(MixFog(col, ComputeFogFactor(IN.positionCS.z)), 1.0);
            #else
                return half4(MixFog(col, ComputeFogFactor(IN.positionCS.z)), alpha);
            #endif
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
