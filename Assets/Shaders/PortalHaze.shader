Shader "Custom/PortalHaze"
{
    Properties
    {
        [Header(Distortion)]
        _DistortStrength ("Distort Strength",     Range(0, 0.2)) = 0.05
        _DistortScale    ("Distort Scale",        Float) = 4.0
        _ScrollSpeed     ("Scroll Speed",         Float) = 0.5
        _SwirlSpeed      ("Swirl Speed (rev/sec)", Float) = 0.06

        [Header(Haze)]
        [HDR] _HazeColor ("Haze Color", Color) = (0.25, 0.5, 1.2, 1)
        _HazeDensity     ("Haze Density",       Range(0, 2)) = 1.0
        _TintStrength    ("Tint Strength",      Range(0, 1)) = 0.7

        [Header(Shape)]
        _FunnelHeight    ("Funnel Height",      Float) = 7.0
        _BottomFade      ("Bottom Fade",        Range(0, 0.5)) = 0.15
        _TopFade         ("Top Fade",           Range(0, 0.5)) = 0.3
        _Opacity         ("Global Opacity",     Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+15"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalHaze"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float _DistortStrength;
                float _DistortScale;
                float _ScrollSpeed;
                float _SwirlSpeed;
                half4  _HazeColor;
                float _HazeDensity;
                float _TintStrength;
                float _FunnelHeight;
                float _BottomFade;
                float _TopFade;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float3 posOS      : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = vpi.positionCS;
                OUT.screenPos = vpi.positionNDC;
                OUT.uv = IN.uv;
                OUT.posOS = IN.positionOS.xyz;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float hNorm = IN.uv.y;

                // Cylindrical noise around the axis (seamless circle domain)
                float angle = atan2(IN.posOS.z, IN.posOS.x);
                float ang = angle + t * _SwirlSpeed * 6.2831853;
                float2 circle = float2(cos(ang), sin(ang));

                float n1 = fbm(circle * _DistortScale + float2(hNorm * 2.0 - t * _ScrollSpeed, -t * _ScrollSpeed * 0.7));
                float n2 = fbm(circle * _DistortScale * 0.5 + float2(3.7, hNorm * 1.5 + t * _ScrollSpeed * 0.5));

                // Distort the opaque background
                float2 suv = IN.screenPos.xy / IN.screenPos.w;
                float2 offset = float2(n1, n2) * _DistortStrength;
                float3 background = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, suv + offset).rgb;

                // Haze density: noise-modulated, faded at both ends of the cone
                float haze = _HazeDensity * (0.55 + 0.45 * n1);
                haze *= smoothstep(0.0, _BottomFade, hNorm);
                haze *= 1.0 - smoothstep(1.0 - _TopFade, 1.0, hNorm);

                float3 color = lerp(background, _HazeColor.rgb, saturate(haze * _TintStrength));
                float alpha = saturate(haze * _Opacity);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
