Shader "Custom/PrismLight"
{
    Properties
    {
        [HDR] _BaseColor      ("Base Color", Color) = (1, 1, 1, 1)
        _ColorIntensity       ("Color Intensity", Range(0, 10)) = 2.5
        _FresnelPower         ("Fresnel Power", Range(0.1, 8)) = 3.0
        _FresnelIntensity     ("Rim Intensity", Range(0, 10)) = 3.0
        _Dispersion           ("Chromatic Dispersion", Range(0, 2)) = 1.5

        _RainbowScale         ("Rainbow Band Scale", Range(0.5, 40)) = 10.0
        _RainbowFlowSpeed     ("Rainbow Flow Speed", Range(0, 5)) = 0.6
        _RainbowIntensity     ("Rainbow Intensity", Range(0, 10)) = 4.0
        _BodyAlpha            ("Facet Body Opacity", Range(0, 1)) = 0.45
        _Refraction           ("Refraction Strength", Range(0, 0.3)) = 0.09

        _ShimmerSpeed         ("Shimmer Speed", Range(0, 5)) = 1.0
        _ShimmerScale         ("Shimmer Scale", Range(0.5, 20)) = 5.0
        _PulseSpeed           ("Pulse Speed", Range(0, 10)) = 2.0
        _PulseAmount          ("Pulse Amount", Range(0, 1)) = 0.15
        _VerticalFade         ("Vertical Fade", Range(0, 1)) = 0.5
        _FadeMin              ("Fade Min Y", Float) = 0.0
        _FadeMax              ("Fade Max Y", Float) = 1.0
        _LightBoost           ("Light Response", Range(0, 4)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderQueue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        // ---------- Pass 1: translucent body, refracting the scene behind ----------
        Pass
        {
            Name "FacetBody"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _ColorIntensity;
                float _FresnelPower;
                float _FresnelIntensity;
                float _Dispersion;
                float _RainbowScale;
                float _RainbowFlowSpeed;
                float _RainbowIntensity;
                float _BodyAlpha;
                float _Refraction;
                float _ShimmerSpeed;
                float _ShimmerScale;
                float _PulseSpeed;
                float _PulseAmount;
                float _VerticalFade;
                float _FadeMin;
                float _FadeMax;
                float _LightBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float4 positionNDC: TEXCOORD4;
                float3 positionWS : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vpi.positionCS;
                output.positionNDC = vpi.positionNDC;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS  = GetWorldSpaceNormalizeViewDir(vpi.positionWS);
                output.fogFactor  = ComputeFogFactor(vpi.positionCS.z);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = vpi.positionWS;
                return output;
            }

            // accumulates all per-pixel lights (zone lamps, flashlight, torches...) hitting the surface
            half3 AccumulateSceneLights(float3 normalWS, float3 positionWS)
            {
                half3 lightSum = 0;

                int pixelLightCount = GetAdditionalLightsCount();
                for (int i = 0; i < pixelLightCount; ++i)
                {
                    Light light = GetAdditionalLight(i, positionWS);
                    lightSum += light.color * (light.shadowAttenuation * light.distanceAttenuation) * saturate(dot(normalWS, light.direction) * 0.5 + 0.5);
                }

                Light mainLight = GetMainLight();
                lightSum += mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                return lightSum;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 nrm = normalize(input.normalWS);
                float3 vdir = normalize(input.viewDirWS);
                float ndotv   = saturate(dot(nrm, vdir));
                float fresnel = pow(1.0 - ndotv, _FresnelPower);

                // per-facet shading so every face reads distinctly
                float facetLight = saturate(abs(dot(nrm, normalize(float3(0.4, 0.8, 0.3)))));

                // scene lights falling on this point (zone lamps + flashlight)
                half3 sceneLights = AccumulateSceneLights(nrm, input.positionWS);
                half  lightLift = 1.0 + _LightBoost * Luminance(sceneLights);
                half3 lightTint = 1.0 + sceneLights * _LightBoost;

                float pulse = 1.0 - _PulseAmount * 0.5 * (1.0 + sin(_Time.y * _PulseSpeed));

                float height01 = saturate((input.positionOS.y - _FadeMin) / max(_FadeMax - _FadeMin, 0.001));
                float verticalFade = lerp(1.0, smoothstep(0.0, 1.0, height01), _VerticalFade);

                // --- scene refraction with chromatic dispersion (RGB split) ---
                float2 screenUV = input.positionNDC.xy / input.positionNDC.w;
                float2 refractOffset = nrm.xy * _Refraction;
                float2 chroma = nrm.xy * _Refraction * _Dispersion * 0.35;

                half3 refracted;
                refracted.r = SampleSceneColor(screenUV + refractOffset + chroma).r;
                refracted.g = SampleSceneColor(screenUV + refractOffset).g;
                refracted.b = SampleSceneColor(screenUV - chroma).b;

                // rainbow tint of the body (linear bands along the pyramid, no circles)
                float r = input.positionOS.x + input.positionOS.z + input.positionOS.y * 0.5;
                float phase = r * _RainbowScale - (1.0 - ndotv) * _Dispersion * 2.0 + _Time.y * _RainbowFlowSpeed;
                half3 rainbow = saturate(abs(frac(phase + float3(0.0, 0.6666, 0.3333)) * 6.0 - 3.0) - 1.0);

                half3 body = refracted * (0.55 + 0.45 * facetLight) * _BaseColor.rgb * 1.4
                           + _BaseColor.rgb * (0.35 + 0.65 * facetLight) * 0.4
                           + rainbow * 0.45 * _RainbowIntensity * lightLift
                           + fresnel * _FresnelIntensity * _BaseColor.rgb * lightTint;
                body *= _ColorIntensity * 0.35 * pulse * lerp(1.0, verticalFade, 0.4);

                half alpha = saturate(_BodyAlpha + fresnel * 0.35) * pulse * verticalFade;

                body = MixFog(body, input.fogFactor);
                return half4(body, alpha);
            }
            ENDHLSL
        }

        // ---------- Pass 2: CD rainbow sheen (additive on top) ----------
        Pass
        {
            Name "RainbowSheen"
            Blend SrcAlpha One
            ZWrite Off
            ZTest Equal
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _ColorIntensity;
                float _FresnelPower;
                float _FresnelIntensity;
                float _Dispersion;
                float _RainbowScale;
                float _RainbowFlowSpeed;
                float _RainbowIntensity;
                float _BodyAlpha;
                float _Refraction;
                float _ShimmerSpeed;
                float _ShimmerScale;
                float _PulseSpeed;
                float _PulseAmount;
                float _VerticalFade;
                float _FadeMin;
                float _FadeMax;
                float _LightBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS  = GetWorldSpaceNormalizeViewDir(positionWS);
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = positionWS;
                return output;
            }

            // accumulates all per-pixel lights (zone lamps, flashlight, torches...) hitting the surface
            half3 AccumulateSceneLights(float3 normalWS, float3 positionWS)
            {
                half3 lightSum = 0;

                int pixelLightCount = GetAdditionalLightsCount();
                for (int i = 0; i < pixelLightCount; ++i)
                {
                    Light light = GetAdditionalLight(i, positionWS);
                    lightSum += light.color * (light.shadowAttenuation * light.distanceAttenuation) * saturate(dot(normalWS, light.direction) * 0.5 + 0.5);
                }

                Light mainLight = GetMainLight();
                lightSum += mainLight.color * (mainLight.shadowAttenuation * mainLight.distanceAttenuation) * saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                return lightSum;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 nrm = normalize(input.normalWS);
                float3 vdir = normalize(input.viewDirWS);
                float ndotv   = saturate(dot(nrm, vdir));
                float fresnel = pow(1.0 - ndotv, _FresnelPower);

                // scene lights falling on this point (zone lamps + flashlight)
                half3 sceneLights = AccumulateSceneLights(nrm, input.positionWS);
                half  lightLift = 1.0 + _LightBoost * Luminance(sceneLights);
                half3 lightTint = 1.0 + sceneLights * _LightBoost;

                // CD sheen: linear rainbow bands flowing down the pyramid, no circles.
                // Per-facet constant view shift separates the faces spectrally.
                float r = input.positionOS.x + input.positionOS.z + input.positionOS.y * 0.5;
                float phase = r * _RainbowScale
                            - (1.0 - ndotv) * _Dispersion * 3.0
                            + _Time.y * _RainbowFlowSpeed;
                half3 rainbow = saturate(abs(frac(phase + float3(0.0, 0.6666, 0.3333)) * 6.0 - 3.0) - 1.0);

                // traveling shimmer for extra sparkle
                float shimmerCoord = input.positionWS.x * _ShimmerScale
                                   + input.positionWS.y * _ShimmerScale * 0.7
                                   - _Time.y * _ShimmerSpeed;
                float shimmer = pow(0.5 + 0.5 * sin(shimmerCoord + sin(shimmerCoord * 0.5) * 2.0), 4.0);

                float pulse = 1.0 - _PulseAmount * 0.5 * (1.0 + sin(_Time.y * _PulseSpeed));

                float height01 = saturate((input.positionOS.y - _FadeMin) / max(_FadeMax - _FadeMin, 0.001));
                float verticalFade = lerp(1.0, smoothstep(0.0, 1.0, height01), _VerticalFade);

                // rainbow is full-surface, boosted on the rim; view angle shifts the phase
                half3 rgb = rainbow * _RainbowIntensity * lightLift * (0.55 + 0.45 * fresnel)
                          + _BaseColor.rgb * fresnel * _FresnelIntensity * lightTint;
                rgb *= _ColorIntensity * 0.5 * pulse * lerp(1.0, verticalFade, 0.4);

                half alpha = saturate(dot(rainbow, float3(0.35, 0.5, 0.15)) * (0.6 + 0.4 * fresnel) + shimmer * 0.5)
                           * pulse;

                rgb = MixFog(rgb, input.fogFactor);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
