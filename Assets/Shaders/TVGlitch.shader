Shader "Custom/TVGlitch"
{
    Properties
    {
        [Header(Source)]
        [MainTexture]
        _BaseMap            ("Render Texture",          2D)           = "white" {}

        [Header(Scanlines)]
        _ScanlineCount      ("Scanline Count",          Float)        = 180
        _ScanlineDarkness   ("Scanline Darkness",       Range(0,0.5)) = 0.12

        [Header(Noise)]
        _NoiseAmount        ("Noise Amount",            Range(0,1))   = 0.12
        _NoiseSpeed         ("Noise Speed",             Range(1,60))  = 30

        [Header(Emission)]
        _EmissionStrength   ("Screen Brightness",       Range(0,10))  = 2.5
        [HDR]
        _EmissionColor      ("Screen Glow (additive)",  Color)        = (0,0,0,0)

        [Header(Glitch  driven by script)]
        _GlitchAmount       ("Glitch Amount",           Range(0,1))   = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "TVGlitch"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float  _GlitchAmount;
                float  _ScanlineCount;
                float  _ScanlineDarkness;
                float  _EmissionStrength;
                float4 _EmissionColor;
                float  _NoiseAmount;
                float  _NoiseSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Детерминированный псевдорандом [0,1]
            float hash11(float  n) { return frac(sin(n)                          * 43758.5453); }
            float hash21(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv   = IN.uv;
                float  time = _Time.y;

                // ── Микро-джиттер по строкам ────────────────────────────────
                float lineY    = floor(uv.y * 120.0);
                float timeTick = floor(time  * 18.0);
                float microN   = hash21(float2(lineY, timeTick));
                float microJit = (microN - 0.5) * _GlitchAmount * 0.06;

                // ── Блочный сдвиг (крупные полосы съезжают горизонтально) ──
                float blockY     = floor(uv.y * 6.0);
                float blockTick  = floor(time  * 5.0);
                float blockN     = hash21(float2(blockY, blockTick));
                float blockShift = step(0.80, blockN)
                                 * (hash11(blockTick + blockY * 0.1) - 0.5)
                                 * _GlitchAmount * 0.22;

                uv.x = saturate(uv.x + microJit + blockShift);

                // ── Сэмпл RT ────────────────────────────────────────────────
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);

                // ── Скан-линии (всегда активны) ─────────────────────────────
                float scanline = frac(IN.uv.y * _ScanlineCount);
                float scanMask = smoothstep(0.0, 0.25, scanline)
                               * smoothstep(1.0, 0.75, scanline);
                col.rgb *= lerp(1.0 - _ScanlineDarkness, 1.0, scanMask);

                // ── Мигание яркости ──────────────────────────────────────────
                float flickerTick = floor(time * 22.0);
                float flickerN    = hash11(flickerTick);
                float flicker     = lerp(1.0, 0.6 + 0.4 * flickerN, _GlitchAmount * 0.5);
                col.rgb          *= flicker;

                // ── Шум / статик ──────────────────────────────────────────────
                // Используем UV (0-1) + frac(), чтобы входные значения оставались
                // малыми и sin() на GPU не терял точность на больших числах.
                float frame = floor(time * _NoiseSpeed);
                float noise = hash21(frac(IN.uv + frame * float2(0.1731339, 0.3170697)));
                col.rgb     = lerp(col.rgb, noise.xxx, _NoiseAmount);

                // ── Emission ──────────────────────────────────────────────────
                // _EmissionStrength — яркость RT-контента на экране
                // _EmissionColor    — аддитивное свечение экрана (HDR), не зависит от контента
                half3 emitted = col.rgb * _EmissionStrength + _EmissionColor.rgb;
                return half4(emitted, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
