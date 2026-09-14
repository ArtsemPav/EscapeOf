Shader "Custom/PortalFunnel"
{
    Properties
    {
        [Header(Swirl Rotation)]
        _SwirlSpeed      ("Swirl Speed (rev/sec)",  Float) = 0.08
        _SwirlScale      ("Swirl Scale",            Float)  = 5.0
        _SwirlStretch    ("Swirl Height Stretch",   Float)  = 1.5
        _FunnelHeight    ("Funnel Height",          Float)  = 7.0

        [Header(Vertex Animation)]
        _WobbleStrength  ("Wobble Strength",    Range(0, 0.5)) = 0.15
        _WobbleSpeed     ("Wobble Speed",       Float) = 1.2
        _TwistAmp        ("Twist Amplitude (deg)", Float) = 40.0
        _TwistFreq       ("Twist Wave Speed",   Float) = 0.8

        [Header(Spiral Lines)]
        _LineScale       ("Line Noise Scale U",  Float)  = 2.5
        _LineScaleV      ("Line Noise Scale V",  Float)  = 10.0
        _LineWidth       ("Line Width",          Range(0.01, 0.5)) = 0.28
        _ScrollSpeed     ("Scroll Speed",        Float)  = 0.7
        _LineIntensity   ("Line Intensity",      Range(0, 5)) = 4.5

        [Header(Secondary Flow)]
        _FlowScale       ("Flow Noise Scale",    Float)  = 3.0
        _FlowSpeed       ("Flow Speed",          Float)  = 0.25
        _FlowStrength    ("Flow Strength",       Range(0, 1)) = 0.55

        [Header(Colors)]
        [HDR] _ColorOuter ("Outer Color (wide end)",   Color) = (0.2, 0.5, 1.4, 1)
        [HDR] _ColorInner ("Inner Color (narrow end)", Color) = (1.6, 1.1, 0.55, 1)
        [HDR] _RimColor   ("Fresnel Rim Color",        Color) = (0.25, 0.55, 1.2, 1)

        [Header(Fresnel)]
        _FresnelPower    ("Fresnel Power",       Range(0.5, 8)) = 2.0
        _FresnelStrength ("Fresnel Strength",    Range(0, 4)) = 2.2

        [Header(Fade)]
        _DepthFadeStart  ("Depth Fade Start (U)", Range(0, 1)) = 0.75
        _DepthFadeWidth  ("Depth Fade Width",    Range(0.01, 0.5)) = 0.2
        _EdgeFade        ("Edge Fade (V)",       Range(0, 0.5)) = 0.04
        _StartFade       ("Start Fade (U=0)",    Range(0, 0.5)) = 0.02

        [Header(Global)]
        _Opacity         ("Global Opacity",      Range(0, 1)) = 1.0
        _TimeOffset      ("Time Offset",         Float)  = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+20"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalFunnel"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _SwirlSpeed;
                float  _SwirlScale;
                float  _SwirlStretch;
                float  _FunnelHeight;
                float  _WobbleStrength;
                float  _WobbleSpeed;
                float  _TwistAmp;
                float  _TwistFreq;
                float  _LineScale;
                float  _LineScaleV;
                float  _LineWidth;
                float  _ScrollSpeed;
                float  _LineIntensity;
                float  _FlowScale;
                float  _FlowSpeed;
                float  _FlowStrength;
                half4  _ColorOuter;
                half4  _ColorInner;
                half4  _RimColor;
                float  _FresnelPower;
                float  _FresnelStrength;
                float  _DepthFadeStart;
                float  _DepthFadeWidth;
                float  _EdgeFade;
                float  _StartFade;
                float  _Opacity;
                float  _TimeOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
                float3 posOS      : TEXCOORD3;
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

                float3 pos = IN.positionOS.xyz;

                // Normalized height along the funnel (0 = wide end, 1 = narrow end)
                float hNorm = saturate(pos.y / max(_FunnelHeight, 0.001));

                // Twist wave: a wave of rotation travelling along the height,
                // so the spiral looks like it is pumping new streams upward.
                float twistDeg = sin(pos.y * 0.35 - _Time.y * _TwistFreq * 6.2831853) * _TwistAmp * hNorm;
                float tw = radians(twistDeg);
                float ct = cos(tw);
                float st = sin(tw);
                pos.xz = float2(
                    pos.x * ct - pos.z * st,
                    pos.x * st + pos.z * ct
                );

                // Radial wobble: organic breathing of the ribbon (cancels at both ends).
                float wobble = sin(_Time.y * _WobbleSpeed + pos.y * 0.8) * _WobbleStrength * 0.1;
                pos.xz *= 1.0 + wobble * sin(hNorm * 3.14159265);

                OUT.positionCS = TransformObjectToHClip(pos);
                OUT.uv = IN.uv;
                // Rotate the normal with the same twist so fresnel stays consistent
                float3 n = IN.normalOS;
                n.xz = float2(
                    n.x * ct - n.z * st,
                    n.x * st + n.z * ct
                );
                OUT.normalWS = TransformObjectToWorldNormal(n);
                float3 positionWS = TransformObjectToWorld(pos);
                OUT.viewDirWS = GetCameraPositionWS() - positionWS;
                OUT.posOS = pos;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y + _TimeOffset;
                float u = IN.uv.x;
                float v = IN.uv.y;

                // Cylindrical coordinates around the funnel axis
                float angle = atan2(IN.posOS.z, IN.posOS.x);
                float hNorm = saturate(IN.posOS.y / max(_FunnelHeight, 0.001));

                // --- Rotating swirl layer A (rotates around the axis) ---
                // Noise sampled on a circle (periodic, no seam), shifted by height;
                // advancing the angle over time spins the whole pattern.
                float angA = angle + t * _SwirlSpeed * 6.2831853;
                float2 circleA = float2(cos(angA), sin(angA));
                float nA = fbm(circleA * _SwirlScale + float2(hNorm * _SwirlStretch, -hNorm * _SwirlStretch));
                nA = nA * 0.5 + 0.5;
                float streakA = smoothstep(1.0 - _LineWidth, 1.0, nA);

                // --- Rotating swirl layer B (counter-rotation, finer) ---
                float angB = angle - t * _SwirlSpeed * 6.2831853 * 0.6;
                float2 circleB = float2(cos(angB), sin(angB));
                float nB = fbm(circleB * _SwirlScale * 1.7 + float2(hNorm * _SwirlStretch * 1.6 + 7.3, 3.1));
                nB = nB * 0.5 + 0.5;
                float streakB = smoothstep(1.0 - _LineWidth * 0.7, 1.0, nB);

                // --- Flowing detail streaks along the ribbon (scroll toward depth) ---
                float scrolledU = u * _LineScale - t * _ScrollSpeed;
                float2 noiseUV = float2(scrolledU, v * _LineScaleV);
                float n1 = fbm(noiseUV);
                n1 = n1 * 0.5 + 0.5;
                float lines1 = smoothstep(1.0 - _LineWidth, 1.0, n1);

                // --- Slow broad flow bands ---
                float flowU = u * _FlowScale - t * _FlowSpeed;
                float nFlow = fbm(float2(flowU, v * _FlowScale * 0.5));
                nFlow = nFlow * 0.5 + 0.5;
                float flowBands = smoothstep(0.4, 0.7, nFlow) * _FlowStrength;

                float lines = max(streakA, streakB * 0.8) * 0.9
                            + lines1 * 0.5
                            + flowBands;
                lines = saturate(lines);

                // Fresnel rim glow (abs for double-sided)
                float3 normalWS = normalize(IN.normalWS);
                float3 viewDirWS = normalize(IN.viewDirWS);
                float ndotv = abs(dot(normalWS, viewDirWS));
                float fresnel = pow(1.0 - saturate(ndotv), _FresnelPower);
                fresnel *= _FresnelStrength;

                // Color gradient: outer (u=0, wide) to inner (u=1, narrow)
                half3 baseColor = lerp(_ColorOuter.rgb, _ColorInner.rgb, smoothstep(0.0, 1.0, u));

                // Combine line emission + fresnel rim
                half3 emission = baseColor * lines * _LineIntensity
                               + _RimColor.rgb * fresnel;

                // Depth fade — fade out toward the narrow end
                float depthFade = 1.0 - smoothstep(_DepthFadeStart, _DepthFadeStart + _DepthFadeWidth, u);

                // Start fade — soft fade at the wide end
                float startFade = smoothstep(0.0, _StartFade, u);

                // Edge fade across ribbon width
                float edgeFade = smoothstep(0.0, _EdgeFade, v) * (1.0 - smoothstep(1.0 - _EdgeFade, 1.0, v));

                float fade = depthFade * startFade * edgeFade * _Opacity;
                half3 finalColor = emission * fade;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
