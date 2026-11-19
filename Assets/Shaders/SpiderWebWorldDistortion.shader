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
        _AmbientVisibility ("Ambient Visibility (видимость в темноте)", Range(0, 1)) = 0.0
        _FlashlightBoost ("Flashlight Boost", Range(0.5, 4)) = 1.0
        _MaxLitDistance ("Max Flashlight Distance (м)", Float) = 4.0
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
                half fogFactor : TEXCOORD3;
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
                half _AmbientVisibility;
                half _FlashlightBoost;
                float _MaxLitDistance;
            CBUFFER_END

            // Flashlight properties — set by FogFlashlightReceiver script (same as FloorFog)
            float3 _FlashlightPos;
            float3 _FlashlightDir;
            half3  _FlashlightColor;
            float  _FlashlightIntensity;
            float  _FlashlightRange;
            float  _FlashlightSpotAngle;
            float  _FlashlightInnerAngle;
            float  _FlashlightEnabled;

            // Ручной расчёт прожектора фонарика (те же _Flashlight*, что у FloorFog).
            // Per-pixel additional lights URP не использует: пер-объектный culling
            // не отдаёт фонарик на этот объект (unity_LightData.y = 0).
            half3 ComputeFlashlight(float3 worldPos)
            {
                if (_FlashlightEnabled < 0.5)
                    return half3(0, 0, 0);

                float3 toFrag = worldPos - _FlashlightPos;
                float dist = length(toFrag);
                float3 fragDir = toFrag / dist;

                // Physical inverse-square falloff: свет заметен только вблизи,
                // с расстояния быстро гаснет (квадрат расстояния).
                float distAtten = 1.0 / (dist * dist + 1.0);

                // Дальность подсветки паутины: за _MaxLitDistance свет гаснет
                // полностью, на подходе — плавное затухание (дальняя треть).
                float litWindow = saturate(1.0 - (dist / max(_MaxLitDistance, 0.01)));
                litWindow = saturate(litWindow * 3.0);

                // Smooth cutoff за пределами range фонарика — дальний край не обрывается резко
                float rangeWindow = saturate(1.0 - (dist / _FlashlightRange));
                distAtten *= rangeWindow * rangeWindow * litWindow;

                // Spot cone attenuation
                float cosOuter = cos(radians(_FlashlightSpotAngle * 0.5));
                float cosInner = cos(radians(_FlashlightInnerAngle * 0.5));
                float cosDir = dot(fragDir, _FlashlightDir);
                float spotAtten = saturate((cosDir - cosOuter) / max(cosInner - cosOuter, 0.0001));
                spotAtten = spotAtten * spotAtten;

                return _FlashlightColor * _FlashlightIntensity * distAtten * spotAtten;
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.screenPos = ComputeScreenPos(posInputs.positionCS);
                // Туман считаем в вершинном шейдере от клипспейс-Z (канонический паттерн URP),
                // а не во фрагменте от SV_Position.z — иначе при малом near/far глубина
                // конвертируется неверно и весь объект уходит в цвет тумана.
                OUT.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
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

                // Видимость нитей: ambient floor (видимость в темноте) + ручная
                // подсветка от фонарика (FogFlashlightReceiver → _Flashlight*).
                half3 baseStrand = tex.rgb * _BaseColor.rgb;
                half3 flashlight = ComputeFlashlight(IN.positionWS) * _FlashlightBoost;
                half3 col = baseStrand * (_AmbientVisibility + flashlight);

            #if _DISTORT_BACKGROUND
                // искажаем фон за паутиной: там, где нити нет, виден «дрожащий» мир
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 bgOffset = (ripple * centered + sway) * 10.0;
                half3 bg = SampleSceneColor(screenUV + bgOffset * _BackgroundDistortion);
                col = lerp(bg, col, alpha);
                return half4(MixFog(col, IN.fogFactor), 1.0);
            #else
                return half4(MixFog(col, IN.fogFactor), alpha);
            #endif
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
