Shader "Custom/PortalRipple"
{
    Properties
    {
        [Header(Water Ripple)]
        _RippleStrength  ("Ripple Strength",     Range(0, 0.3)) = 0.06
        _RippleFreq      ("Ripple Rings",        Float) = 6.0
        _RippleSpeed     ("Ripple Speed (cycles/sec)", Float) = 0.5
        _WobbleScale     ("Surface Wobble",      Float) = 3.0

        [Header(Tint)]
        [HDR] _Tint      ("Ripple Tint",         Color) = (0.3, 0.6, 1.2, 1)
        _TintStrength    ("Tint Strength",       Range(0, 1)) = 0.35

        [Header(Shape)]
        _InnerFade       ("Inner Fade",          Range(0, 1)) = 0.0
        _EdgeFade        ("Edge Fade",           Range(0, 0.5)) = 0.25
        _Opacity         ("Opacity",             Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+17"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalRipple"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float _RippleStrength;
                float _RippleFreq;
                float _RippleSpeed;
                float _WobbleScale;
                half4  _Tint;
                float _TintStrength;
                float _InnerFade;
                float _EdgeFade;
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
                for (int i = 0; i < 3; i++)
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
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float2 centered = IN.uv - 0.5;
                float dist = length(centered) * 2.0; // 0 at center, 1 at rim

                // Concentric water rings expanding outward
                float ripple = sin(dist * _RippleFreq * 6.2831853 - t * _RippleSpeed * 6.2831853);

                // Organic wobble on top of the rings
                float wobble = fbm(centered * _WobbleScale + float2(t * 0.25, -t * 0.2));

                float2 dir = centered / max(length(centered), 0.001);
                float2 distortion = dir * ripple * _RippleStrength
                                  + float2(wobble, wobble * 0.7) * _RippleStrength * 0.6;

                // Distort the opaque background
                float2 suv = IN.screenPos.xy / IN.screenPos.w;
                float3 background = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, suv + distortion).rgb;

                // Fades: soft rim, optional soft center
                float rimFade = 1.0 - smoothstep(1.0 - _EdgeFade, 1.0, dist);
                float centerFade = _InnerFade > 0.001 ? smoothstep(0.0, _InnerFade, dist) : 1.0;
                float fade = rimFade * centerFade;

                // Alpha shimmers with the rings — the surface feels alive
                float alpha = saturate(_Opacity * fade * lerp(0.35, 1.0, 0.5 + 0.5 * ripple));
                float3 color = lerp(background, _Tint.rgb, _TintStrength * fade);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
