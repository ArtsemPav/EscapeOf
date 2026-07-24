Shader "Custom/BonsaiLeaf"
{
    Properties
    {
        _BaseMap ("Base Map (RGB)", 2D) = "white" {}
        _AlphaMask ("Alpha Mask (A)", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _BaseColor ("Base Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 100
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // URP global keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 tangentOS    : TANGENT;
                float2 uv           : TEXCOORD0;
                float2 lightmapUV   : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS      : SV_POSITION;
                float2 uv               : TEXCOORD0;
                float3 normalWS         : TEXCOORD1;
                float3 positionWS       : TEXCOORD2;
                float  fogFactor        : TEXCOORD3;
                float3 tangentWS        : TEXCOORD4;
                float3 bitangentWS      : TEXCOORD5;
                #if defined(LIGHTMAP_ON)
                float2 lightmapUV       : TEXCOORD6;
                #endif
                #if defined(DYNAMICLIGHTMAP_ON)
                float2 dynamicLightmapUV : TEXCOORD7;
                #endif
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord      : TEXCOORD8;
                #endif
            };

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_AlphaMask); SAMPLER(sampler_AlphaMask);
            TEXTURE2D(_BumpMap);   SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _AlphaMask_ST;
                float4 _BumpMap_ST;
                float  _Cutoff;
                float  _Smoothness;
                float  _BumpScale;
                half4  _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   norInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionHCS  = posInputs.positionCS;
                output.positionWS   = posInputs.positionWS;
                output.normalWS     = norInputs.normalWS;
                output.tangentWS    = norInputs.tangentWS;
                output.bitangentWS  = norInputs.bitangentWS;
                output.uv           = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor    = ComputeFogFactor(posInputs.positionCS.z);

                #if defined(LIGHTMAP_ON)
                output.lightmapUV = input.lightmapUV.xy * unity_LightmapST.xy + unity_LightmapST.zw;
                #endif
                #if defined(DYNAMICLIGHTMAP_ON)
                output.dynamicLightmapUV = input.lightmapUV.zw * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
                #endif
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(posInputs);
                #endif

                return output;
            }

            half4 frag(Varyings input, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // Sample color from base map, alpha from separate mask
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half  alpha  = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, input.uv).a;

                // Alpha clip
                clip(alpha - _Cutoff);

                // Flip normals on back faces so lighting is correct on both sides
                float facing = IS_FRONT_VFACE(isFrontFace, 1.0, -1.0);
                float3 normalWS    = input.normalWS    * facing;
                float3 tangentWS   = input.tangentWS   * facing;
                float3 bitangentWS = input.bitangentWS * facing;

                // Normal mapping
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3x3 TBN = float3x3(tangentWS, bitangentWS, normalWS);
                normalWS = TransformTangentToWorld(normalTS, TBN);
                normalWS = NormalizeNormalPerPixel(normalWS);

                // Build input data for URP PBR
                InputData inputData = (InputData)0;
                inputData.positionWS              = input.positionWS;
                inputData.normalWS                = normalWS;
                inputData.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord             = input.shadowCoord;
                #elif defined(MAIN_LIGHT_CALCULATESHADOWS)
                inputData.shadowCoord             = TransformWorldToShadowCoord(input.positionWS);
                #else
                inputData.shadowCoord             = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord                = input.fogFactor;
                inputData.vertexLighting           = half3(0, 0, 0);
                inputData.bakedGI                  = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV  = GetNormalizedScreenSpaceUV(input.positionHCS);
                inputData.shadowMask              = CalculateShadowMask(inputData);

                #if defined(DYNAMICLIGHTMAP_ON)
                inputData.bakedGI = CalculateDynamicLighting(input.dynamicLightmapUV, input.positionWS, normalWS);
                #endif

                #if defined(LIGHTMAP_ON)
                uint meshRenderingLayers = GetMeshRenderingLayer();
                inputData.bakedGI = SampleLightmap(input.lightmapUV, normalWS);
                #endif

                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                inputData.vertexLighting = half3(0, 0, 0); // simplified
                #endif

                SurfaceData surface = (SurfaceData)0;
                surface.albedo     = albedo;
                surface.metallic   = 0;
                surface.smoothness = _Smoothness;
                surface.alpha      = 1;
                surface.occlusion  = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_AlphaMask); SAMPLER(sampler_AlphaMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _AlphaMask_ST;
                float4 _BumpMap_ST;
                float  _Cutoff;
                float  _Smoothness;
                float  _BumpScale;
                half4  _BaseColor;
            CBUFFER_END

            float3 _LightDirection;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, _LightDirection));
                output.positionHCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _AlphaMask);
                #if UNITY_REVERSED_Z
                    output.positionHCS.z = min(output.positionHCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionHCS.z = max(output.positionHCS.z, ZERO_W);
                #endif
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, input.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_AlphaMask); SAMPLER(sampler_AlphaMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _AlphaMask_ST;
                float4 _BumpMap_ST;
                float  _Cutoff;
                float  _Smoothness;
                float  _BumpScale;
                half4  _BaseColor;
            CBUFFER_END

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _AlphaMask);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, input.uv).a;
                clip(alpha - _Cutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
            };

            TEXTURE2D(_AlphaMask); SAMPLER(sampler_AlphaMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _AlphaMask_ST;
                float4 _BumpMap_ST;
                float  _Cutoff;
                float  _Smoothness;
                float  _BumpScale;
                half4  _BaseColor;
            CBUFFER_END

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _AlphaMask);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, input.uv).a;
                clip(alpha - _Cutoff);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                return half4(normalWS, 0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
