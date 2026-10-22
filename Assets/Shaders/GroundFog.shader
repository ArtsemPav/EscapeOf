Shader "Custom/GroundFog"
{
    Properties
    {
        [HDR] _Color      ("Fog Color", Color) = (0.55, 0.65, 0.8, 0.45)
        _Density     ("Density", Range(0.0, 2.0)) = 0.55
        _HeightFade  ("Height Fade (m)", Float) = 1.2
        _BaseHeight  ("Base Height (world Y)", Float) = 0.0
        _NoiseScale  ("Noise Scale", Float) = 0.35
        _ScrollSpeedA("Scroll Speed A", Vector) = (0.03, 0.0, 0.02, 0.0)
        _ScrollSpeedB("Scroll Speed B", Vector) = (-0.021, 0.0, 0.014, 0.0)
        _Steps       ("Raymarch Steps", Float) = 14
        _DistFadeStart("Dist Fade Start (m)", Float) = 18.0
        _DistFadeEnd ("Dist Fade End (m)", Float) = 38.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+90"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "GroundFogVolume"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Front
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Density;
                float _HeightFade;
                float _BaseHeight;
                float _NoiseScale;
                float4 _ScrollSpeedA;
                float4 _ScrollSpeedB;
                float _Steps;
                float _DistFadeStart;
                float _DistFadeEnd;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm2(float2 p)
            {
                return ValueNoise(p) * 0.65 + ValueNoise(p * 2.3 + 17.17) * 0.35;
            }

            bool RayBox(float3 ro, float3 rd, out float tNear, out float tFar)
            {
                float3 inv = 1.0 / (rd + 1e-6);
                float3 t0 = (float3(-0.5, -0.5, -0.5) - ro) * inv;
                float3 t1 = (float3( 0.5,  0.5,  0.5) - ro) * inv;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                tNear = max(max(tmin.x, tmin.y), tmin.z);
                tFar  = min(min(tmax.x, tmax.y), tmax.z);
                return tFar > max(tNear, 0.0);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 camPosWS = _WorldSpaceCameraPos.xyz;
                float3 rayDirWS = normalize(input.positionWS - camPosWS);

                float3 ro = mul(unity_WorldToObject, float4(camPosWS, 1.0)).xyz;
                float3 rd = mul(unity_WorldToObject, float4(rayDirWS, 0.0)).xyz;

                float tNear, tFar;
                if (!RayBox(ro, rd, tNear, tFar))
                    discard;

                float tEnd = tFar;

                // Convert object-space distances to world distances
                float objToWorldScale = 1.0 / max(length(mul((float3x3)unity_ObjectToWorld, rd)), 1e-5);
                float tStart = max(tNear, 0.0) * objToWorldScale;
                tEnd *= objToWorldScale;

                // Depth clip: march only up to opaque geometry. rawDepth 0 = no depth texture, skip clip.
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(screenUV);
                if (rawDepth > 1e-6)
                {
                    float3 scenePosWS = ComputeWorldSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_VP);
                    tEnd = min(tEnd, distance(camPosWS, scenePosWS));
                }

                int steps = (int)clamp(_Steps, 2, 32);
                float stepLen = (tEnd - tStart) / steps;
                if (stepLen <= 0.0)
                    discard;

                float t = _Time.y;
                float distFade = 1.0 - smoothstep(_DistFadeStart, _DistFadeEnd, distance(camPosWS, input.positionWS));

                // Moonlight shadow sampled once (NaN-guarded — shadow data may be
                // uninitialized in menus / before first shadow update)
                float shad = MainLightRealtimeShadow(TransformWorldToShadowCoord(camPosWS + rayDirWS * tStart));
                if (isnan(shad)) shad = 1.0;

                float alphaAcc = 0.0;
                half3 scatter = half3(0.0, 0.0, 0.0);

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float3 pWS = camPosWS + rayDirWS * (tStart + (i + 0.5) * stepLen);

                    // Height falloff: dense at _BaseHeight, gone _HeightFade meters up
                    float h = saturate(1.0 - (pWS.y - _BaseHeight) / max(_HeightFade, 0.01));
                    h *= h;

                    float2 uv = pWS.xz * _NoiseScale;

                    // Large-scale billow mask: distinct clumps with clear gaps between them
                    float mask = Fbm2(uv * 0.3 + _ScrollSpeedA.xz * t * 2.0);
                    mask = saturate((mask - 0.4) * 2.5);

                    // High-frequency detail inside the clumps, animated by scroll + warp
                    float2 uvA = uv + _ScrollSpeedA.xz * t;
                    float2 uvB = uv * 1.7 + _ScrollSpeedB.xz * t;
                    float nA = Fbm2(uvA + Fbm2(uvA + t * 0.05) * 0.7);
                    float nB = Fbm2(uvB + Fbm2(uvB - t * 0.04) * 0.7);
                    float detail = saturate(nA * 0.7 + nB * 0.5);

                    // Vertical shaping: clumps are puffy at the top, solid at the base
                    float puff = lerp(1.0, detail, saturate((pWS.y - _BaseHeight) / max(_HeightFade, 0.01) * 1.5));

                    float density = mask * puff * detail;

                    float lum = 0.35 + 0.65 * shad * h;

                    float aSample = saturate(density * _Density) * h * distFade * _Color.a * stepLen;
                    alphaAcc += (1.0 - alphaAcc) * aSample;
                    scatter += _Color.rgb * lum * aSample;

                    if (alphaAcc > 0.98)
                        break;
                }

                // Global URP fog integration (if enabled)
                float fogFactor = ComputeFogFactor(input.positionCS.z);
                scatter = MixFog(scatter, fogFactor);

                return half4(scatter, saturate(alphaAcc));
            }
            ENDHLSL
        }
    }
}
