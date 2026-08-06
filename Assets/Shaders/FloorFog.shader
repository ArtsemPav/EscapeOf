Shader "Custom/FloorFog"
{
    Properties
    {
        [HDR] _FogColor  ("Fog Color", Color) = (1.0, 1.0, 1.0, 1.0)
        _FogScale       ("Noise Scale", Float) = 3.0
        _ScrollSpeed    ("Scroll Speed", Float) = 0.3
        _SwirlSpeed     ("Swirl Speed", Float) = 0.15
        _EdgeFade       ("Edge Fade", Float) = 0.3
        _DarkOpacity    ("Dark Opacity", Range(0, 1)) = 0.0
        _LitOpacity     ("Lit Opacity", Range(0, 1)) = 0.5
        _Opacity        ("Global Opacity", Range(0, 1)) = 1.0
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
            Name "FloorFog"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float _FogScale;
                float _ScrollSpeed;
                float _SwirlSpeed;
                float _EdgeFade;
                float _DarkOpacity;
                float _LitOpacity;
                float _Opacity;
            CBUFFER_END

            // Flashlight properties — set by FogFlashlightReceiver script
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
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
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

            float fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                float frequency = 1.0;
                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * gradientNoise(p * frequency);
                    amplitude *= 0.5;
                    frequency *= 2.0;
                }
                return value;
            }

            // Compute spotlight illumination manually.
            // Returns rgb intensity in 0..1+ range.
            half3 ComputeFlashlight(float3 worldPos)
            {
                if (_FlashlightEnabled < 0.5)
                    return half3(0, 0, 0);

                float3 toFrag = worldPos - _FlashlightPos;
                float dist = length(toFrag);
                float3 fragDir = toFrag / dist;

                // Distance attenuation — smooth falloff over range
                float distAtten = saturate(1.0 - (dist / _FlashlightRange));
                distAtten = distAtten * distAtten;

                // Spot cone attenuation
                float cosOuter = cos(radians(_FlashlightSpotAngle * 0.5));
                float cosInner = cos(radians(_FlashlightInnerAngle * 0.5));
                float cosDir = dot(fragDir, _FlashlightDir);
                float spotAtten = saturate((cosDir - cosOuter) / max(cosInner - cosOuter, 0.0001));
                spotAtten = spotAtten * spotAtten;

                half3 illumination = _FlashlightColor * _FlashlightIntensity * distAtten * spotAtten;
                return illumination;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 positionWS = input.positionWS;
                float2 pos = positionWS.xz * _FogScale * 0.1;
                float t = _Time.y * _ScrollSpeed;

                // Procedural fog noise
                float n1 = fbm(pos + float2(t, t * 0.7));
                float n2 = fbm(pos * 1.8 - float2(t * 0.6, t * 0.9) + 17.3);
                float fog = (n1 * 0.6 + n2 * 0.4) * 0.5 + 0.5;
                fog = saturate(fog);
                fog = smoothstep(0.25, 0.8, fog);

                float angle = _Time.y * _SwirlSpeed;
                float s = sin(angle);
                float c = cos(angle);
                float2 swirled = mul(float2x2(c, -s, s, c), input.uv - 0.5) + 0.5;
                float swirlNoise = fbm(swirled * _FogScale * 0.15 + float2(t * 0.3, 0));
                fog *= 0.6 + swirlNoise * 0.8;
                fog = saturate(fog);

                // Radial edge fade
                float2 centeredUV = input.uv - 0.5;
                float distFromCenter = length(centeredUV) * 2.0;
                float edgeFade = 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, distFromCenter);

                // Manual flashlight illumination
                half3 flashlight = ComputeFlashlight(positionWS);
                float lightAmount = length(flashlight);
                float lightNormalized = saturate(lightAmount * 0.25);

                // Dark: invisible. Lit: bright white tinted by flashlight color.
                half3 finalColor = _FogColor.rgb * flashlight * 0.5;
                float opacity = lerp(_DarkOpacity, _LitOpacity, lightNormalized);
                float alpha = fog * edgeFade * opacity * _Opacity;

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
