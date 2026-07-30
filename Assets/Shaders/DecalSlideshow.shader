Shader "Custom/DecalSlideshow"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _SlideTextures ("Slide Texture Array", 2DArray) = "" {}
        _CurrentIndex ("Current Slide Index", Float) = 0
        _NextIndex ("Next Slide Index", Float) = 0
        _ScrollOffset ("Scroll Offset", Float) = 0
        _FrameGap ("Frame Gap", Range(0, 0.5)) = 0.03
        _GapColor ("Gap Color", Color) = (0,0,0,0)
        [Toggle] _ScrollDirection ("Scroll Vertical", Float) = 0
        _ArraySize ("Array Size", Float) = 1
        _FlickerFrequency ("Flicker Frequency (Hz)", Float) = 25
        _FlickerAmount ("Flicker Amount", Range(0, 1)) = 0.15
        _DrawOrder ("Draw Order", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"   = "UniversalPipeline"
            "RenderType"       = "Overlay"
            "Queue"            = "Overlay"
            "DisableBatching"  = "True"
            "PreviewType"      = "Plane"
        }

        // ==============================================================
        //  Shared code — inserted at the top of every HLSLPROGRAM block
        // ==============================================================
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DecalInput.hlsl"

        #ifdef _DECAL_LAYERS
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareRenderingLayerTexture.hlsl"
        #endif

        UNITY_INSTANCING_BUFFER_START(Decal)
            UNITY_DEFINE_INSTANCED_PROP(half4x4, _NormalToWorld)
            UNITY_DEFINE_INSTANCED_PROP(float, _DecalLayerMaskFromDecal)
        UNITY_INSTANCING_BUFFER_END(Decal)

        TEXTURE2D_ARRAY(_SlideTextures);
        SAMPLER(sampler_SlideTextures);

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _GapColor;
            float _CurrentIndex;
            float _NextIndex;
            float _ScrollOffset;
            float _FrameGap;
            float _ScrollDirection;
            float _ArraySize;
            float _FlickerFrequency;
            float _FlickerAmount;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
            output.positionCS = posInputs.positionCS;
            return output;
        }

        // ----------------------------------------------------------
        //  Decal projection: depth → world → decal-space → UV + fade
        // ----------------------------------------------------------
        void GetDecalProjection(Varyings input,
            out float2 texCoord, out half fadeFactor, out float2 screenPos)
        {
            screenPos = input.positionCS.xy;
            TransformScreenUV(screenPos, _ScreenSize.y);

            #if UNITY_REVERSED_Z
                float depth = LoadSceneDepth(screenPos);
            #else
                float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, LoadSceneDepth(screenPos));
            #endif

            float2 positionSS = screenPos * _ScreenSize.zw;
            float3 positionWS = ComputeWorldSpacePosition(positionSS, depth, UNITY_MATRIX_I_VP);

            float3 positionDS = TransformWorldToObject(positionWS);
            positionDS = positionDS * float3(1.0, -1.0, 1.0);

            float clipValue = 0.5 - Max3(abs(positionDS).x, abs(positionDS).y, abs(positionDS).z);
            clip(clipValue);

            texCoord = positionDS.xz + float2(0.5, 0.5);

            half4x4 normalToWorld = UNITY_ACCESS_INSTANCED_PROP(Decal, _NormalToWorld);
            fadeFactor = clamp(normalToWorld[0][3], 0.0, 1.0);
            float2 uvScale  = float2(normalToWorld[3][0], normalToWorld[3][1]);
            float2 uvOffset = float2(normalToWorld[3][2], normalToWorld[3][3]);
            texCoord = texCoord * uvScale + uvOffset;
        }

        // ----------------------------------------------------------
        //  Film-strip sampling: scrolls through Texture2DArray slices
        //  with configurable gap between frames (diafilm look).
        //  _ScrollOffset: 0 = resting, +1 = next frame, -1 = previous
        //  Applies 25 Hz flicker (projector shutter) to alpha.
        // ----------------------------------------------------------
        half4 SampleFilmStrip(float2 texCoord)
        {
            float scrollCoord = lerp(texCoord.x, texCoord.y, _ScrollDirection);
            float totalSpan   = 1.0 + _FrameGap;
            float actualOffset = _ScrollOffset * totalSpan;
            float filmPos      = scrollCoord + actualOffset;

            float segment      = filmPos / totalSpan;
            float frameOffset  = floor(segment);
            float posInSegment = frac(segment) * totalSpan;

            int arraySize  = max(1, (int)_ArraySize);
            int sliceIndex = (int)(_CurrentIndex + frameOffset);
            sliceIndex     = ((sliceIndex % arraySize) + arraySize) % arraySize;

            half4 col;
            if (posInSegment < 1.0)
            {
                float2 frameUV = texCoord;
                if (_ScrollDirection < 0.5)
                    frameUV.x = posInSegment;
                else
                    frameUV.y = posInSegment;
                col = SAMPLE_TEXTURE2D_ARRAY(_SlideTextures, sampler_SlideTextures, frameUV, sliceIndex);
            }
            else
            {
                col = _GapColor;
            }

            // --- Projector shutter flicker ---
            // Rectangular wave at _FlickerFrequency Hz: half the period is
            // full brightness, the other half is dimmed by _FlickerAmount.
            float flickerWave = step(0.5, frac(_Time.y * _FlickerFrequency));
            half  flicker     = lerp(half(1.0) - half(_FlickerAmount), half(1.0), flickerWave);
            col.a *= flicker;

            return col;
        }
        ENDHLSL

        // ==============================================================
        //  Pass 1 — Screen Space Decal (default URP decal path)
        // ==============================================================
        Pass
        {
            Name "DecalScreenSpaceProjector"
            Tags { "LightMode" = "DecalScreenSpaceProjector" }

            Blend SrcAlpha OneMinusSrcAlpha
            Cull Front
            ZTest Greater
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragScreenSpace
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _DECAL_LAYERS
            #pragma multi_compile_fragment _DECAL_NORMAL_BLEND_LOW _DECAL_NORMAL_BLEND_MEDIUM _DECAL_NORMAL_BLEND_HIGH

            half4 FragScreenSpace(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                UNITY_SETUP_INSTANCE_ID(input);

                float2 texCoord;
                half fadeFactor;
                float2 screenPos;
                GetDecalProjection(input, texCoord, fadeFactor, screenPos);

                #ifdef _DECAL_LAYERS
                uint surfaceRenderingLayer = LoadSceneRenderingLayer(screenPos);
                uint projectorRenderingLayer = asuint(UNITY_ACCESS_INSTANCED_PROP(Decal, _DecalLayerMaskFromDecal));
                clip((surfaceRenderingLayer & projectorRenderingLayer) - 0.1);
                #endif

                half4 col = SampleFilmStrip(texCoord);
                col.rgb *= _BaseColor.rgb;
                col.a   *= fadeFactor * _BaseColor.a;
                return col;
            }
            ENDHLSL
        }

        // ==============================================================
        //  Pass 2 — DBuffer Decal
        // ==============================================================
        Pass
        {
            Name "DBufferProjector"
            Tags { "LightMode" = "DBufferProjector" }

            Blend 0 SrcAlpha OneMinusSrcAlpha, Zero OneMinusSrcAlpha
            Blend 1 SrcAlpha OneMinusSrcAlpha, Zero OneMinusSrcAlpha
            Blend 2 SrcAlpha OneMinusSrcAlpha, Zero OneMinusSrcAlpha
            Cull Front
            ZTest Greater
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragDBuffer
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _DECAL_LAYERS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

            void FragDBuffer(Varyings input, OUTPUT_DBUFFER(outDBuffer))
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                UNITY_SETUP_INSTANCE_ID(input);

                float2 texCoord;
                half fadeFactor;
                float2 screenPos;
                GetDecalProjection(input, texCoord, fadeFactor, screenPos);

                half4 col = SampleFilmStrip(texCoord);
                col.rgb *= _BaseColor.rgb;
                col.a   *= fadeFactor * _BaseColor.a;

                DecalSurfaceData surfaceData = (DecalSurfaceData)0;
                surfaceData.baseColor   = col;
                surfaceData.occlusion   = half(1.0);
                surfaceData.smoothness  = half(0.0);
                ENCODE_INTO_DBUFFER(surfaceData, outDBuffer);
            }
            ENDHLSL
        }

        // ==============================================================
        //  Pass 3 — Forward Emissive Decal
        // ==============================================================
        Pass
        {
            Name "DecalProjectorForwardEmissive"
            Tags { "LightMode" = "DecalProjectorForwardEmissive" }

            Blend 0 SrcAlpha One
            Cull Front
            ZTest Greater
            ZWrite Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragEmissive
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _DECAL_LAYERS

            half4 FragEmissive(Varyings input) : SV_Target0
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                UNITY_SETUP_INSTANCE_ID(input);

                float2 texCoord;
                half fadeFactor;
                float2 screenPos;
                GetDecalProjection(input, texCoord, fadeFactor, screenPos);

                half4 col = SampleFilmStrip(texCoord);
                col.rgb *= _BaseColor.rgb * fadeFactor;
                col.a   *= _BaseColor.a;
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden"
}
