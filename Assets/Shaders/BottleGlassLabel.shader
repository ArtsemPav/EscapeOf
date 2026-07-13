Shader "Custom/BottleGlassLabel"
{
    // Two-subshader approach: opaque alpha-clipped labels + transparent fresnel glass.
    // Both receive main directional light + additional per-pixel lights (point/spot/flashlight).
    // Supports Forward+ clustered lighting via LIGHT_LOOP_BEGIN/END macros.
    Properties
    {
        _BaseMap        ("Label Texture",       2D)    = "white" {}
        _BaseColor      ("Label Tint",          Color) = (1, 1, 1, 1)
        _Cutoff         ("Alpha Cutoff",        Range(0, 1)) = 0.5
        _LabelSmoothness("Label Smoothness",    Range(0, 1)) = 0.3
        _GlassColor     ("Glass Tint",          Color) = (0.85, 0.9, 0.95, 0.15)
        _GlassFresnel   ("Glass Fresnel Power", Range(0.5, 8)) = 2.5
        _GlassRim       ("Glass Rim Intensity", Range(0, 1))  = 0.4
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SubShader 1: LABELS — opaque, alpha-clipped, Queue = AlphaTest
    // ═══════════════════════════════════════════════════════════════════════
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"           = "AlphaTest"
            "RenderType"      = "TransparentCutout"
        }

        Pass
        {
            Name "LabelForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite      On
            ZTest       LEqual
            Cull        Back
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex   vertLabel
            #pragma fragment fragLabel
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _LabelSmoothness;
                half4  _GlassColor;
                half   _GlassFresnel;
                half   _GlassRim;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 normalWS     : TEXCOORD0;
                float2 uv           : TEXCOORD1;
                float  fogFactor    : TEXCOORD2;
                float3 positionWS   : TEXCOORD3;
            };

            Varyings vertLabel(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS  = posInputs.positionCS;
                OUT.normalWS    = normInputs.normalWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor   = ComputeFogFactor(posInputs.positionCS.z);
                OUT.positionWS  = posInputs.positionWS;
                return OUT;
            }

            half4 fragLabel(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(texColor.a - _Cutoff);

                half3 albedo = texColor.rgb * _BaseColor.rgb;
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);

                // ── Main directional light ──
                Light mainLight = GetMainLight();
                half  NdotL = saturate(dot(normalWS, mainLight.direction));
                half  shadowAtt = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));
                half3 directLight = mainLight.color * NdotL * shadowAtt;

                // Specular from main light
                half3 halfDir = normalize(mainLight.direction + viewDirWS);
                half  spec    = pow(saturate(dot(normalWS, halfDir)), 32.0) * _LabelSmoothness;
                half3 specCol = mainLight.color * spec;

                // ── Additional lights (Forward+ cluster-aware) ──
                // Build InputData with fields needed by LIGHT_LOOP_BEGIN macro.
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = IN.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                    inputData.shadowMask = half4(1, 1, 1, 1);

                    uint addCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(addCount)
                        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                        half  addNdotL = saturate(dot(normalWS, addLight.direction));
                        half3 addDiff  = addLight.color * addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation;
                        directLight   += addDiff;

                        half3 addHalf = normalize(addLight.direction + viewDirWS);
                        half  addSpec = pow(saturate(dot(normalWS, addHalf)), 32.0) * _LabelSmoothness;
                        specCol      += addLight.color * addSpec * addLight.distanceAttenuation;
                    LIGHT_LOOP_END
                }
                #endif

                half3 ambient = SampleSH(normalWS);
                half3 finalColor = albedo * (directLight + ambient) + specCol;

                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // ── Shadow Caster (labels only) ──
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest  LEqual
            Cull   Back
            AlphaToMask On

            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _LabelSmoothness;
                half4  _GlassColor;
                half   _GlassFresnel;
                half   _GlassRim;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            float3 _LightDirection;

            struct ShadowAttr
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct ShadowVar
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            ShadowVar vertShadow(ShadowAttr IN)
            {
                ShadowVar OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, positionCS.w * 0.999);
                #else
                    positionCS.z = max(positionCS.z, positionCS.w * 0.001);
                #endif
                OUT.positionCS = positionCS;
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 fragShadow(ShadowVar IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(texColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        // ── DepthOnly (labels only) ──
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest  LEqual
            Cull   Back
            AlphaToMask On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vertDepth
            #pragma fragment fragDepth

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _LabelSmoothness;
                half4  _GlassColor;
                half   _GlassFresnel;
                half   _GlassRim;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct DepthAttr
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct DepthVar
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            DepthVar vertDepth(DepthAttr IN)
            {
                DepthVar OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 fragDepth(DepthVar IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(texColor.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SubShader 2: GLASS — transparent, alpha-blend, Queue = Transparent
    // ═══════════════════════════════════════════════════════════════════════
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
        }

        Pass
        {
            Name "GlassForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest  LEqual
            Cull   Back
            Blend  SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vertGlass
            #pragma fragment fragGlass
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Cutoff;
                half   _LabelSmoothness;
                half4  _GlassColor;
                half   _GlassFresnel;
                half   _GlassRim;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
            };

            Varyings vertGlass(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.normalWS   = normInputs.normalWS;
                OUT.viewDirWS  = GetWorldSpaceViewDir(posInputs.positionWS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor  = ComputeFogFactor(posInputs.positionCS.z);
                OUT.positionWS = posInputs.positionWS;
                return OUT;
            }

            half4 fragGlass(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                clip(_Cutoff - texColor.a);

                float3 normalWS = normalize(IN.normalWS);
                float3 viewDir  = normalize(IN.viewDirWS);

                // Fresnel rim
                half fresnel = pow(1.0 - saturate(dot(normalWS, viewDir)), _GlassFresnel);

                // ── Main directional light ──
                Light mainLight = GetMainLight();
                half  NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 lightColor = mainLight.color * NdotL;

                // ── Additional lights (Forward+ cluster-aware) ──
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = IN.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                    inputData.shadowMask = half4(1, 1, 1, 1);

                    uint addCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(addCount)
                        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
                        half  addNdotL = saturate(dot(normalWS, addLight.direction));
                        lightColor    += addLight.color * addNdotL * addLight.distanceAttenuation * addLight.shadowAttenuation;
                    LIGHT_LOOP_END
                }
                #endif

                half3 ambient = SampleSH(normalWS);

                // Glass responds to both ambient and direct light
                half3 glassColor = _GlassColor.rgb * (ambient + lightColor);
                glassColor      += fresnel * _GlassRim * (ambient + lightColor + 0.3h);

                half alpha = saturate(_GlassColor.a + fresnel * _GlassRim);

                glassColor = MixFog(glassColor, IN.fogFactor);
                return half4(glassColor, alpha);
            }
            ENDHLSL
        }
    }
}
