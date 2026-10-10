Shader "Custom/PortalCore"
{
    // Camera-facing additive quad: blazing HDR core glow, animated god-rays and a sharp ring.
    // Used for the portal throat core and (with only the ring enabled) for the shockwave.
    Properties
    {
        [HDR] _CoreColor ("Core Color", Color) = (3.0, 2.4, 1.6, 1)
        [HDR] _RayColor  ("Ray Color",  Color) = (1.2, 0.8, 2.4, 1)
        [HDR] _RingColor ("Ring Color", Color) = (2.5, 1.6, 3.5, 1)

        _GlowIntensity ("Glow Intensity", Float) = 1.0
        _GlowSoftness  ("Glow Softness",  Range(0.002, 0.3)) = 0.03

        _RayIntensity  ("Ray Intensity",  Float) = 1.0
        _RayFrequency  ("Ray Noise Frequency", Float) = 3.0
        _RayFalloff    ("Ray Falloff", Range(0.5, 8)) = 2.5
        _RaySpin       ("Ray Spin Speed", Float) = 0.15

        _RingRadius    ("Ring Radius", Range(0, 1)) = 0.2
        _RingWidth     ("Ring Width",  Range(0.002, 0.5)) = 0.03
        _RingIntensity ("Ring Intensity", Float) = 0.0

        _HoleRadius    ("Dark Hole Radius", Range(0, 0.5)) = 0.0
        _Opacity       ("Opacity", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+30"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalCore"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _RayColor;
                half4 _RingColor;
                float _GlowIntensity;
                float _GlowSoftness;
                float _RayIntensity;
                float _RayFrequency;
                float _RayFalloff;
                float _RaySpin;
                float _RingRadius;
                float _RingWidth;
                float _RingIntensity;
                float _HoleRadius;
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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Billboard: expand the quad along the camera right/up axes
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float sx = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float sy = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up    = UNITY_MATRIX_V[1].xyz;
                float3 posWS = centerWS + right * IN.positionOS.x * sx + up * IN.positionOS.y * sy;

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float2 c = (IN.uv - 0.5) * 2.0;
                float r = length(c);
                float edge = 1.0 - smoothstep(0.7, 1.0, r);

                // Hot core glow (inverse-square-like falloff)
                float glow = _GlowIntensity * _GlowSoftness / (r * r + _GlowSoftness) * edge;

                // Animated rays: noise sampled on a circle, so there is no seam
                float2 dir = c / max(r, 0.0001);
                float sp = sin(t * _RaySpin);
                float cp = cos(t * _RaySpin);
                float2 dirR = float2(dir.x * cp - dir.y * sp, dir.x * sp + dir.y * cp);
                float n1 = gradientNoise(dirR * _RayFrequency + float2(t * 0.35, -t * 0.2));
                float n2 = gradientNoise(dirR * _RayFrequency * 2.3 + float2(-t * 0.5, 4.1));
                float rayMask = saturate(smoothstep(0.0, 0.55, n1) + 0.5 * smoothstep(0.1, 0.6, n2));
                float rays = rayMask * exp(-r * _RayFalloff) * edge * smoothstep(0.0, 0.08, r);

                // Sharp ring (event horizon / shockwave)
                float rd = (r - _RingRadius) / _RingWidth;
                float ring = exp(-rd * rd) * _RingIntensity * (1.0 - smoothstep(0.9, 1.0, r));

                // Carve a dark event horizon out of the glow so the lens' black disc stays black
                float holeMask = _HoleRadius > 0.0001 ? smoothstep(_HoleRadius * 0.9, _HoleRadius * 1.25, r) : 1.0;

                half3 col = (_CoreColor.rgb * glow
                          + _RayColor.rgb * rays * _RayIntensity) * holeMask
                          + _RingColor.rgb * ring;

                return half4(col * _Opacity, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
