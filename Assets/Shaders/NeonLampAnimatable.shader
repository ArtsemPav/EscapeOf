Shader "Custom/NeonLampAnimatable"
{
    // Same properties as URP/Lit, plus _EmissionIntensity (Range 0–20) for animation.
    // The Forward pass multiplies emission by _EmissionIntensity, so you can
    // keyframe the slider in the Animation window to drive the lamp turn-on.

    Properties
    {
        [MainTexture]  _BaseMap        ("Albedo",          2D)    = "white" {}
        [MainColor]    _BaseColor      ("Color",           Color) = (1,1,1,1)
                       _Cutoff         ("Alpha Cutoff",    Range(0, 1)) = 0.5
                       _Smoothness     ("Smoothness",      Range(0, 1)) = 0.5
                       _Metallic       ("Metallic",        Range(0, 1)) = 0.0
                       _MetallicGlossMap("Metallic",       2D)    = "white" {}
                       _BumpMap        ("Normal Map",      2D)    = "bump" {}
                       _BumpScale      ("Normal Scale",    Float) = 1.0
                       _EmissionMap    ("Emission",        2D)    = "white" {}
        [HDR]          _EmissionColor  ("Emission Color",  Color) = (0,0,0,1)

        // ── Animation-ready emission intensity ──────────────────────────────
                       _EmissionIntensity("Emission Intensity", Range(0, 20)) = 0.0

        // ── Hidden blend / surface state (kept for parity with URP/Lit) ─────
        [HideInInspector] _WorkflowMode         ("__workflow", Float) = 1.0
        [HideInInspector] _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        [HideInInspector] _SpecColor            ("Specular",  Color) = (0.2,0.2,0.2)
        [HideInInspector] _SpecGlossMap         ("Specular",  2D)    = "white" {}
        [ToggleOff]  _SpecularHighlights        ("Specular Highlights",   Float) = 1.0
        [ToggleOff]  _EnvironmentReflections    ("Environment Reflections", Float) = 1.0
        [HideInInspector] _Parallax             ("__parallax",     Float) = 0.005
        [HideInInspector] _ParallaxMap          ("__parallaxMap",  2D)    = "black" {}
        [HideInInspector] _OcclusionStrength    ("__occStrength",  Range(0,1)) = 1.0
        [HideInInspector] _OcclusionMap         ("__occMap",       2D)    = "white" {}
        [HideInInspector] _DetailMask           ("__detailMask",   2D)    = "white" {}
        [HideInInspector] _DetailAlbedoMapScale ("__detailAlbedoScale", Range(0,2)) = 1.0
        [HideInInspector] _DetailAlbedoMap      ("__detailAlbedo", 2D)    = "linearGrey" {}
        [HideInInspector] _DetailNormalMapScale ("__detailNormalScale", Range(0,2)) = 1.0
        [HideInInspector] _DetailNormalMap      ("__detailNormal", 2D)    = "bump" {}
        [HideInInspector] _ClearCoatMask        ("__clearCoatMask", Float) = 0.0
        [HideInInspector] _ClearCoatSmoothness  ("__clearCoatSmooth", Float) = 0.0
        [HideInInspector] _Surface              ("__surface",      Float) = 0.0
        [HideInInspector] _Blend                ("__blend",        Float) = 0.0
        [HideInInspector] _Cull                 ("__cull",         Float) = 2.0
        [ToggleUI]   _AlphaClip                 ("__clip",         Float) = 0.0
        [HideInInspector] _SrcBlend             ("__src",          Float) = 1.0
        [HideInInspector] _DstBlend             ("__dst",          Float) = 0.0
        [HideInInspector] _SrcBlendAlpha        ("__srcA",         Float) = 1.0
        [HideInInspector] _DstBlendAlpha        ("__dstA",         Float) = 0.0
        [HideInInspector] _ZWrite               ("__zw",           Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask          ("__alphaToMask",  Float) = 0.0
        [HideInInspector] _AddPrecomputedVelocity("_AddPrecomputedVelocity", Float) = 0.0
        [HideInInspector] _XRMotionVectorsPass  ("_XRMotionVectorsPass", Float) = 1.0
        [ToggleUI]   _ReceiveShadows            ("Receive Shadows", Float) = 1.0
        [HideInInspector] _QueueOffset          ("Queue offset",   Float) = 0.0

        // ObsoleteProperties — kept so the material upgrades cleanly from URP/Lit
        [HideInInspector] _MainTex              ("BaseMap", 2D)    = "white" {}
        [HideInInspector] _Color                ("Base Color", Color) = (1,1,1,1)
        [HideInInspector] _GlossMapScale        ("Smoothness", Float) = 0.0
        [HideInInspector] _Glossiness           ("Smoothness", Float) = 0.0
        [HideInInspector] _GlossyReflections    ("EnvironmentReflections", Float) = 0.0
        [HideInInspector] _Mode                 ("Mode", Float) = 0.0

        [HideInInspector][NoScaleOffset] unity_Lightmaps    ("unity_Lightmaps",    2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset] unity_ShadowMasks  ("unity_ShadowMasks",  2DArray) = "" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType"         = "Opaque"
            "RenderPipeline"     = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector"    = "True"
        }
        LOD 300

        // ── Forward pass — custom fragment multiplies emission by intensity ──
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex   LitPassVertex
            #pragma fragment LitPassFragmentEmission

            // Material Keywords
            // _EMISSION, _METALLICSPECGLOSSMAP and _NORMALMAP are always defined
            // because the corresponding textures are always assigned on this material.
            #define _EMISSION 1
            #define _METALLICSPECGLOSSMAP 1
            #define _NORMALMAP 1
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local_fragment _OCCLUSIONMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local_fragment _SPECULAR_SETUP

            // URP keywords
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // Unity keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ USE_LEGACY_LIGHTMAPS
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"

            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            // Animated emission intensity — declared outside UnityPerMaterial CBUFFER
            // so the standard LitInput layout stays intact for SRP-batcher passes.
            float _EmissionIntensity;

            // ── Custom fragment — identical to LitPassFragment but scales emission
            void LitPassFragmentEmission(
                Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            #if defined(_PARALLAXMAP)
            #if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
                half3 viewDirTS = input.viewDirTS;
            #else
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
            #endif
                ApplyPerPixelDisplacement(viewDirTS, input.uv);
            #endif

                SurfaceData surfaceData;
                InitializeStandardLitSurfaceData(input.uv, surfaceData);

                // Multiply emission by the animated intensity slider.
                surfaceData.emission *= _EmissionIntensity;

            #ifdef LOD_FADE_CROSSFADE
                LODFadeCrossFade(input.positionCS);
            #endif

                InputData inputData;
                InitializeInputData(input, surfaceData.normalTS, inputData);
                SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

            #if defined(_DBUFFER)
                ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
            #endif

                InitializeBakedGIData(input, inputData);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

                outColor = color;

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        // ── ShadowCaster pass ─────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex   ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        // ── DepthOnly pass ────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex   DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // ── DepthNormals pass ─────────────────────────────────────────────────
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex   DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            #define _NORMALMAP 1
            #pragma shader_feature_local _PARALLAXMAP
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A

            #pragma multi_compile _ LOD_FADE_CROSSFADE

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        // ── Meta pass (lightmap baking) ───────────────────────────────────────
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }

            Cull Off

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex   UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaLit

            #pragma shader_feature_local_fragment _SPECULAR_SETUP
            #define _EMISSION 1
            #define _METALLICSPECGLOSSMAP 1
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma shader_feature_local _ _DETAIL_MULX2 _DETAIL_SCALED
            #pragma shader_feature_local_fragment _SPECGLOSSMAP
            #pragma shader_feature EDITOR_VISUALIZATION

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitMetaPass.hlsl"
            ENDHLSL
        }

        // ── Universal2D pass ──────────────────────────────────────────────────
        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend[_SrcBlend][_DstBlend]
            ZWrite[_ZWrite]
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            #pragma vertex   vert
            #pragma fragment frag

            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON

            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Universal2D.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
