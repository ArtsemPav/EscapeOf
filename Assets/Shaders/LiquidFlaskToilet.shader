Shader "Custom/LiquidFlaskToilet"
{
    Properties
    {
        [HDR] _BaseColor       ("Base Color", Color) = (0.05, 0.15, 0.3, 1.0)
        [HDR] _SurfaceColor    ("Surface Color", Color) = (0.1, 0.3, 0.5, 1.0)
        [HDR] _MurkyColor      ("Murky Color", Color) = (0.3, 0.25, 0.1, 1.0)
        [HDR] _DepthColor      ("Depth Color", Color) = (0.02, 0.08, 0.15, 1.0)
        _Murkiness             ("Murkiness", Range(0,1)) = 0.5
        _Opacity               ("Opacity", Range(0,1)) = 0.9
        _WaveAmplitude         ("Wave Amplitude", Range(0,2)) = 0.8
        _WaveFrequency         ("Wave Frequency", Range(0,100)) = 25
        _WaveSpeed             ("Wave Speed", Range(0,10)) = 2
        _Ripple0               ("Ripple 0", Vector) = (0, -10, 0, 0)
        _Ripple1               ("Ripple 1", Vector) = (0, -10, 0, 0)
        _Ripple2               ("Ripple 2", Vector) = (0, -10, 0, 0)
        _Ripple3               ("Ripple 3", Vector) = (0, -10, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        // ===================== Pass 1: LiquidForward =====================
        Pass
        {
            Name "LiquidForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define WAVE_SCALE   0.015
            #define RIPPLE_SCALE 0.01

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SurfaceColor;
                float4 _MurkyColor;
                float4 _DepthColor;
                float  _Murkiness;
                float  _Opacity;
                float  _WaveAmplitude;
                float  _WaveFrequency;
                float  _WaveSpeed;
            CBUFFER_END

            float4 _Ripple0;
            float4 _Ripple1;
            float4 _Ripple2;
            float4 _Ripple3;

            float SurfaceWave(float3 wp)
            {
                float2 p = wp.xz * _WaveFrequency * 0.01;
                float t = _Time.y * _WaveSpeed;
                return sin(p.x + t) * cos(p.y + t * 0.8)
                     + 0.3 * sin((p.x + p.y) * 1.7 + t * 1.3);
            }

            float RippleAt(float3 wp, float4 r)
            {
                if (r.w <= 0.001) return 0;
                float2 d = wp.xz - r.xz;
                float dist = length(d);
                float el = _Time.y - r.y;
                if (el < 0. || el > 3.) return 0;
                float rr = el * 0.4;
                float rd = dist - rr;
                float rw = 0.06 + el * 0.05;
                float rf = exp(-rd * rd / (rw * rw));
                float tf = 1. - smoothstep(1., 3., el);
                float cf = 1. / (1. + dist * 3.);
                return sin(rd * 60.) * rf * cf * tf * r.w;
            }

            float TotalRipple(float3 wp)
            {
                return RippleAt(wp, _Ripple0)
                     + RippleAt(wp, _Ripple1)
                     + RippleAt(wp, _Ripple2)
                     + RippleAt(wp, _Ripple3);
            }

            float TotalDisp(float3 wp)
            {
                return SurfaceWave(wp) * _WaveAmplitude * WAVE_SCALE
                     + TotalRipple(wp) * RIPPLE_SCALE;
            }

            float3 TotalDispgrad(float3 wp)
            {
                float e = 0.01;
                float dx = TotalDisp(wp + float3(e, 0, 0)) - TotalDisp(wp - float3(e, 0, 0));
                float dz = TotalDisp(wp + float3(0, 0, e)) - TotalDisp(wp - float3(0, 0, e));
                return float3(-dx, 1, -dz) * 20.;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float  fogFactor    : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float disp = TotalDisp(posWS);
                float surfDist = smoothstep(0.0, 0.02, posWS.y);
                disp *= surfDist;
                posWS.y += disp;

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = normalize(TotalDispgrad(posWS));
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float3 N = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);

                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                float NdotV = saturate(dot(N, viewDirWS));
                float fresnel = pow(1.0 - NdotV, 3.0);

                float3 baseCol = lerp(_BaseColor.rgb, _MurkyColor.rgb, _Murkiness);
                float3 surfCol = lerp(_SurfaceColor.rgb, _MurkyColor.rgb, _Murkiness * 0.5);
                float3 depthCol = _DepthColor.rgb;

                float depthFactor = saturate(posWS.y * 2.0);
                float3 color = lerp(depthCol, baseCol, depthFactor);
                color = lerp(color, surfCol, fresnel * 0.6);
                color += mainLight.color * NdotL * 0.3;
                color += fresnel * _SurfaceColor.rgb * 0.5;

                color = MixFog(color, input.fogFactor);

                return half4(color, _Opacity);
            }
            ENDHLSL
        }

        // ===================== Pass 2: MurkySurface =====================
        Pass
        {
            Name "MurkySurface"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #define WAVE_SCALE   0.015
            #define RIPPLE_SCALE 0.01

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SurfaceColor;
                float4 _MurkyColor;
                float4 _DepthColor;
                float  _Murkiness;
                float  _Opacity;
                float  _WaveAmplitude;
                float  _WaveFrequency;
                float  _WaveSpeed;
            CBUFFER_END

            float4 _Ripple0;
            float4 _Ripple1;
            float4 _Ripple2;
            float4 _Ripple3;

            float SurfaceWave(float3 wp)
            {
                float2 p = wp.xz * _WaveFrequency * 0.01;
                float t = _Time.y * _WaveSpeed;
                return sin(p.x + t) * cos(p.y + t * 0.8)
                     + 0.3 * sin((p.x + p.y) * 1.7 + t * 1.3);
            }

            float RippleAt(float3 wp, float4 r)
            {
                if (r.w <= 0.001) return 0;
                float2 d = wp.xz - r.xz;
                float dist = length(d);
                float el = _Time.y - r.y;
                if (el < 0. || el > 3.) return 0;
                float rr = el * 0.4;
                float rd = dist - rr;
                float rw = 0.06 + el * 0.05;
                float rf = exp(-rd * rd / (rw * rw));
                float tf = 1. - smoothstep(1., 3., el);
                float cf = 1. / (1. + dist * 3.);
                return sin(rd * 60.) * rf * cf * tf * r.w;
            }

            float TotalRipple(float3 wp)
            {
                return RippleAt(wp, _Ripple0)
                     + RippleAt(wp, _Ripple1)
                     + RippleAt(wp, _Ripple2)
                     + RippleAt(wp, _Ripple3);
            }

            float TotalDisp(float3 wp)
            {
                return SurfaceWave(wp) * _WaveAmplitude * WAVE_SCALE
                     + TotalRipple(wp) * RIPPLE_SCALE;
            }

            float3 TotalDispgrad(float3 wp)
            {
                float e = 0.01;
                float dx = TotalDisp(wp + float3(e, 0, 0)) - TotalDisp(wp - float3(e, 0, 0));
                float dz = TotalDisp(wp + float3(0, 0, e)) - TotalDisp(wp - float3(0, 0, e));
                return float3(-dx, 1, -dz) * 20.;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float  fogFactor    : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float disp = TotalDisp(posWS);
                float surfDist = smoothstep(0.0, 0.02, posWS.y);
                disp *= surfDist;
                posWS.y += disp;

                output.positionWS = posWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.normalWS = normalize(TotalDispgrad(posWS));
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 posWS = input.positionWS;
                float3 N = normalize(input.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(posWS);

                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(N, mainLight.direction));
                float NdotV = saturate(dot(N, viewDirWS));
                float fresnel = pow(1.0 - NdotV, 3.0);

                float3 color = lerp(_MurkyColor.rgb, _SurfaceColor.rgb, fresnel);
                color += mainLight.color * NdotL * 0.3;

                color = MixFog(color, input.fogFactor);

                return half4(color, _Opacity * 0.8);
            }
            ENDHLSL
        }

        // ===================== Pass 3: ShadowCaster =====================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            #define WAVE_SCALE   0.015
            #define RIPPLE_SCALE 0.01

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _SurfaceColor;
                float4 _MurkyColor;
                float4 _DepthColor;
                float  _Murkiness;
                float  _Opacity;
                float  _WaveAmplitude;
                float  _WaveFrequency;
                float  _WaveSpeed;
            CBUFFER_END

            float4 _Ripple0;
            float4 _Ripple1;
            float4 _Ripple2;
            float4 _Ripple3;

            float SurfaceWave(float3 wp)
            {
                float2 p = wp.xz * _WaveFrequency * 0.01;
                float t = _Time.y * _WaveSpeed;
                return sin(p.x + t) * cos(p.y + t * 0.8)
                     + 0.3 * sin((p.x + p.y) * 1.7 + t * 1.3);
            }

            float RippleAt(float3 wp, float4 r)
            {
                if (r.w <= 0.001) return 0;
                float2 d = wp.xz - r.xz;
                float dist = length(d);
                float el = _Time.y - r.y;
                if (el < 0. || el > 3.) return 0;
                float rr = el * 0.4;
                float rd = dist - rr;
                float rw = 0.06 + el * 0.05;
                float rf = exp(-rd * rd / (rw * rw));
                float tf = 1. - smoothstep(1., 3., el);
                float cf = 1. / (1. + dist * 3.);
                return sin(rd * 60.) * rf * cf * tf * r.w;
            }

            float TotalRipple(float3 wp)
            {
                return RippleAt(wp, _Ripple0)
                     + RippleAt(wp, _Ripple1)
                     + RippleAt(wp, _Ripple2)
                     + RippleAt(wp, _Ripple3);
            }

            float TotalDisp(float3 wp)
            {
                return SurfaceWave(wp) * _WaveAmplitude * WAVE_SCALE
                     + TotalRipple(wp) * RIPPLE_SCALE;
            }

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
            };

            ShadowVaryings vert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float disp = TotalDisp(positionWS);
                float surfDist = smoothstep(0.0, 0.02, positionWS.y);
                disp *= surfDist;
                positionWS.y += disp;

                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _MainLightPosition.xyz));

                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                output.positionWS = positionWS;

                return output;
            }

            half4 frag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
