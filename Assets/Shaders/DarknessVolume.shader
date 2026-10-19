Shader "Custom/DarknessVolume"
{
    // Inky-black volume that ignores all scene lighting, including the flashlight.
    // Unlit, transparent, alpha softened at silhouette edges via fresnel.
    Properties
    {
        _DarkColor      ("Dark Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _BaseOpacity    ("Base Opacity", Range(0, 1)) = 1.0
        _FresnelPower   ("Edge Softness Power", Float) = 2.0
        _EdgeOpacity    ("Edge Opacity", Range(0, 1)) = 0.0
        _NoiseScale     ("Wobble Noise Scale", Float) = 1.5
        _NoiseSpeed     ("Wobble Speed", Float) = 0.15
        _NoiseStrength  ("Wobble Strength", Float) = 0.15

        // --- World-space rectangular bounds (clip the volume to a room, corridor, etc.) ---
        _BoundsEnabled  ("Use World Bounds", Float) = 0.0
        _BoundsMin      ("Bounds Min (X,Y,Z)", Vector) = (-1000, -1000, -1000, 0)
        _BoundsMax      ("Bounds Max (X,Y,Z)", Vector) = (1000, 1000, 1000, 0)
        _BoundsFalloff  ("Bounds Falloff", Float) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "DarknessVolume"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DarkColor;
                float _BaseOpacity;
                float _FresnelPower;
                float _EdgeOpacity;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseStrength;

                // --- World-space rectangular bounds ---
                float  _BoundsEnabled;
                float4 _BoundsMin;
                float4 _BoundsMax;
                float  _BoundsFalloff;
            CBUFFER_END

            // Flashlight parameters — set by DarknessVolumeReceiver script.
            // Kept so the volume can *deliberately* ignore it and stay pitch black.
            float3 _FlashlightPos;
            float3 _FlashlightDir;
            half3  _FlashlightColor;
            float  _FlashlightIntensity;
            float  _FlashlightRange;
            float  _FlashlightSpotAngle;
            float  _FlashlightInnerAngle;
            float  _FlashlightEnabled;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
            };

            float2 hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float gradientNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = dot(hash2(i + float2(0, 0)), f - float2(0, 0));
                float b = dot(hash2(i + float2(1, 0)), f - float2(1, 0));
                float c = dot(hash2(i + float2(0, 1)), f - float2(0, 1));
                float d = dot(hash2(i + float2(1, 1)), f - float2(1, 1));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS  = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS    = normalize(TransformObjectToWorldNormal(input.normalOS));
                output.viewDirWS   = normalize(_WorldSpaceCameraPos.xyz - output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float3 normalWS   = input.normalWS;

                // Fresnel: faces seen head-on are fully opaque,
                // silhouette edges fade out so the box dissolves into the dark.
                float fresnel = 1.0 - saturate(dot(normalWS, input.viewDirWS));
                float edgeAlpha = lerp(_BaseOpacity, _EdgeOpacity, pow(fresnel, _FresnelPower));

                // Slow noise wobble so the boundary "breathes" instead of being a clean glass box.
                float n = gradientNoise(positionWS.xz * _NoiseScale + float2(_Time.y * _NoiseSpeed, positionWS.y * _NoiseScale));
                edgeAlpha += (n - 0.5) * 2.0 * _NoiseStrength;
                edgeAlpha = saturate(edgeAlpha);

                // World-space rectangular boundary fade
                float boundsMask = 1.0;
                if (_BoundsEnabled > 0.5)
                {
                    float distToMinX = positionWS.x - _BoundsMin.x;
                    float distToMaxX = _BoundsMax.x - positionWS.x;
                    float distToMinY = positionWS.y - _BoundsMin.y;
                    float distToMaxY = _BoundsMax.y - positionWS.y;
                    float distToMinZ = positionWS.z - _BoundsMin.z;
                    float distToMaxZ = _BoundsMax.z - positionWS.z;

                    float fadeX = smoothstep(0.0, _BoundsFalloff, min(distToMinX, distToMaxX));
                    float fadeY = smoothstep(0.0, _BoundsFalloff, min(distToMinY, distToMaxY));
                    float fadeZ = smoothstep(0.0, _BoundsFalloff, min(distToMinZ, distToMaxZ));
                    boundsMask = fadeX * fadeY * fadeZ;
                }

                float alpha = edgeAlpha * boundsMask * _DarkColor.a;
                return half4(_DarkColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
