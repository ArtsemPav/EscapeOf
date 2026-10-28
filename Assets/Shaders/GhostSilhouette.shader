// Black ghost silhouette with scrolling noise. The body is one solid unlit
// pass (no rim, no per-normal variance: those leak through inverted normals
// on the tripó scan, lighting up eyes/mouth/hands from inside). Readability
// comes from a separate inverted-hull outline pass and a mist particle layer.
Shader "Custom/GhostSilhouette"
{
    Properties
    {
        _BaseColor ("Silhouette Color", Color) = (0.02, 0.02, 0.03, 0.9)
        _OutlineColor ("Outline Color", Color) = (0.75, 0.78, 0.82, 1.0)
        _OutlineThickness ("Outline Thickness", Float) = 0.012
        _NoiseSpeed ("Noise Scroll Speed", Float) = 0.35
        _NoiseScale ("Noise Scale", Float) = 9.0
        _NoiseStrength ("Noise Strength", Range(0.0, 1.0)) = 0.08
        _MistColor ("Mist Color", Color) = (0.1, 0.11, 0.13, 1.0)
        _DistortStrength ("Background Distortion", Range(0.0, 0.1)) = 0.025
        _WarpVisibility ("Background See-Through", Range(0.0, 1.0)) = 0.2
        _SparkleColor ("Sparkle Color", Color) = (0.85, 0.88, 0.92, 1.0)
        _SparkleIntensity ("Sparkle Intensity", Range(0.0, 4.0)) = 1.6
        _SparkleDensity ("Sparkle Density", Range(0.0, 1.0)) = 0.18
        _SparkleSpeed ("Sparkle Speed", Range(0.0, 10.0)) = 1.2
        [Toggle(_DISSOLVE_ON)] _DissolveOn ("Enable Dissolve", Float) = 0
        _Dissolve ("Dissolve Amount", Range(0.0, 1.0)) = 0.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        // Depth prepass running inside the transparent queue (untagged pass):
        // URP only runs DepthOnly for the opaque queue, so for a transparent
        // figure we write depth here instead. Inner geometry can no longer
        // show through the body, while transparency still applies vs the bg.
        Pass
        {
            Name "GhostDepthPrepass"
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _OutlineColor;
                half _OutlineThickness;
                half _NoiseSpeed;
                half _NoiseScale;
                half _NoiseStrength;
                half4 _MistColor;
                half _DistortStrength;
                half _WarpVisibility;
            CBUFFER_END
            #include "GhostSilhouetteNoise.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 fragDepth(Varyings IN) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // Inverted-hull outline: back faces pushed out along normals render
        // only where the body edge is, so the silhouette edge glows faintly
        // and reads from any angle. Cannot leak interior details.
        Pass
        {
            Name "GhostOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _OutlineColor;
                half _OutlineThickness;
                half _NoiseSpeed;
                half _NoiseScale;
                half _NoiseStrength;
                half4 _MistColor;
                half _DistortStrength;
                half _WarpVisibility;
            CBUFFER_END
            #include "GhostSilhouetteNoise.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz + IN.normalOS * _OutlineThickness);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                GhostDissolveClip(IN.positionWS);
                // Flicker the outline with the same noise field as the body.
                float2 uv = IN.positionWS.xz * _NoiseScale - float2(0.0, _TimeParameters.x * _NoiseSpeed);
                half flicker = GhostNoise(uv);
                half4 col = _OutlineColor;
                col.a *= 0.35 + 0.5 * flicker;
                return col;
            }
            ENDHLSL
        }

        // Body: unlit black that refracts the opaque background behind it —
        // the scene seen through the figure slowly swims (screen-UV distortion
        // driven by the same ghost noise). No rim, no view-dependent terms, so
        // nothing on the interior can stand out against the silhouette.
        Pass
        {
            Name "GhostBody"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _OutlineColor;
                half _OutlineThickness;
                half _NoiseSpeed;
                half _NoiseScale;
                half _NoiseStrength;
                half4 _MistColor;
                half _DistortStrength;
                half _WarpVisibility;
                half4 _SparkleColor;
                half _SparkleIntensity;
                half _SparkleDensity;
                half _SparkleSpeed;
            CBUFFER_END
            #include "GhostSilhouetteNoise.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.positionOS = IN.positionOS.xyz;
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                GhostDissolveClip(IN.positionWS);

                // Object-space coords: always small, so noise stays stable
                // regardless of where the figure stands in the world.
                float2 noiseUV = IN.positionOS.xz * _NoiseScale
                    + float2(IN.positionOS.y * _NoiseScale * 0.25, 0.0)
                    + float2(sin(IN.positionOS.y * 2.0 + _TimeParameters.x * _NoiseSpeed), 0.0)
                    - float2(0.0, _TimeParameters.x * _NoiseSpeed);
                half noise = saturate(GhostNoise(noiseUV));

                // Two scrolling noise layers drive the background warp.
                float t = _TimeParameters.x * _NoiseSpeed;
                float2 wA = IN.positionOS.xz * _NoiseScale * 0.6 + float2(0.0, -t * 0.6);
                float2 wB = IN.positionOS.xy * _NoiseScale * 0.4 + float2(t * 0.3, 0.0);
                half2 warp = half2(saturate(GhostNoise(wA)), saturate(GhostNoise(wB))) - 0.5;

                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 distortedUV = clamp(screenUV + warp * _DistortStrength * 4.0, 0.001, 0.999);
                half3 bg = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, distortedUV).rgb;

                // Multiplicative variation AROUND the base color: the body can
                // only get darker than the base, never lighter, so it always
                // reads as a black silhouette no matter how noise animates.
                half shade = 1.0 - _NoiseStrength * (1.0 - noise);
                half3 col = bg.rgb * _WarpVisibility * shade;

                // Slow cold mist drifting through the body volume.
                float2 mistUV = IN.positionOS.xy * _NoiseScale * 0.35 - float2(_TimeParameters.x * _NoiseSpeed * 0.15, _TimeParameters.x * _NoiseSpeed * 0.4);
                half mist = saturate(GhostNoise(mistUV));
                col = lerp(col, _MistColor.rgb, mist * 0.3);

                // Round drifting sparks: each cell holds one dot with a random
                // center, and the whole field scrolls upward so dots rise like
                // slow embers. The dot radius is smaller than the center's
                // margin from the cell edge, so dots are always full circles.
                float2 dotUV = float2(IN.positionOS.x * 14.0 + IN.positionOS.z * 6.0, IN.positionOS.y * 14.0)
                    - float2(0.0, _TimeParameters.x * _NoiseSpeed * _SparkleSpeed);
                float2 dotCell = floor(dotUV);
                float2 dotF = frac(dotUV) - 0.5;
                float2 dotRnd = GhostHash2(dotCell);
                float2 dotOffset = dotRnd * 0.25; // center stays in the middle half of the cell
                float dotDist = length(dotF - dotOffset);
                float twinkle = 0.5 + 0.5 * sin(_TimeParameters.x * 2.0 + dotRnd.x * 62.83);
                float spark = smoothstep(0.08, 0.015, dotDist) * step(1.0 - _SparkleDensity, dotRnd.y) * twinkle;
                col += _SparkleColor.rgb * spark * _SparkleIntensity;

                return half4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Particles/Unlit"
}
