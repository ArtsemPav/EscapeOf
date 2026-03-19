Shader "Custom/FireDistortion"
{
    Properties
    {
        [Header(Fire Texture)]
        [Space(4)]
        _MainTex            ("Fire Texture (RGBA)",     2D)             = "white" {}

        [Header(Distortion)]
        [Space(4)]
        _DistortTex         ("Distortion Noise (RG)",   2D)             = "white" {}
        _DistortStrength    ("Distort Strength",        Range(0, 0.1))  = 0.03
        _DistortScale       ("Distort Noise Scale",     Float)          = 1.5
        _DistortScrollA     ("Scroll Layer A (X Y)",    Vector)         = (0.0, -0.4, 0, 0)
        _DistortScrollB     ("Scroll Layer B (X Y)",    Vector)         = (0.05, -0.25, 0, 0)

        [Header(Color Tint)]
        [Space(4)]
        [Toggle(_USE_COLOR)]
        _UseColor           ("Apply Color Tint",        Float)          = 1
        _ColorBottom        ("Color Bottom",            Color)          = (1.0, 0.3, 0.0, 1)
        _ColorTop           ("Color Top",               Color)          = (1.0, 0.8, 0.1, 0)
        _ColorStrength      ("Color Blend Strength",    Range(0, 1))    = 0.5

        [Header(Emission Glow)]
        [Space(4)]
        [Toggle(_EMISSION)]
        _UseEmission        ("Enable Glow",             Float)          = 1
        [HDR]
        _EmissionColor      ("Glow Color (HDR)",        Color)          = (2.0, 0.6, 0.1, 1)
        _EmissionStrength   ("Glow Strength",           Range(0, 8))    = 2.5
        _EmissionFalloff    ("Glow Falloff top to tip",Range(0, 1))    = 0.7

        [Header(Output)]
        [Space(4)]
        _Opacity            ("Opacity",                 Range(0, 1))    = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+10"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "FireDistortion"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma shader_feature_local _USE_COLOR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);             SAMPLER(sampler_MainTex);
            TEXTURE2D(_DistortTex);          SAMPLER(sampler_DistortTex);
            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DistortTex_ST;
                float  _DistortStrength;
                float  _DistortScale;
                float4 _DistortScrollA;
                float4 _DistortScrollB;
                float4 _ColorBottom;
                float4 _ColorTop;
                float  _ColorStrength;
                float4 _EmissionColor;
                float  _EmissionStrength;
                float  _EmissionFalloff;
                float  _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float4 color      : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                OUT.color      = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                // 1. Форма огня — читаем из основной текстуры
                float4 fireTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float  mask    = fireTex.a * IN.color.a;
                clip(mask - 0.01);  // отсекаем пиксели вне формы

                // 2. Двухслойный анимированный шум
                float  t     = _Time.y;
                float2 uvA   = IN.uv * _DistortScale + _DistortScrollA.xy * t;
                float2 uvB   = IN.uv * _DistortScale * 0.7 + _DistortScrollB.xy * t;
                float2 nA    = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, uvA).rg;
                float2 nB    = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, uvB).rg;

                // Центрируем [0,1] в [-0.5, 0.5] и масштабируем
                float2 distort = ((nA + nB) * 0.5 - 0.5) * _DistortStrength * mask;

                // 3. Смещаем экранные UV и читаем деформированный фон
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float4 bg       = SAMPLE_TEXTURE2D(_CameraOpaqueTexture,
                                                   sampler_CameraOpaqueTexture,
                                                   screenUV + distort);

                // 4. Опциональный цветовой тинт по высоте UV
                float4 result = bg;
                #ifdef _USE_COLOR
                {
                    float4 tint    = lerp(_ColorBottom, _ColorTop, saturate(IN.uv.y));
                    result.rgb     = lerp(bg.rgb, bg.rgb * tint.rgb * 1.5, _ColorStrength * mask);
                }
                #endif

                result.a = mask * _Opacity;
                return result;
            }
            ENDHLSL
        }

        // ── Pass 2: Emission — аддитивное свечение, подхватывается Bloom ──
        Pass
        {
            Name "FireEmission"

            Blend One One           // аддитивный: чёрный = невидим, яркий = свечение
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vertEmit
            #pragma fragment fragEmit
            #pragma shader_feature_local _EMISSION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_DistortTex); SAMPLER(sampler_DistortTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DistortTex_ST;
                float  _DistortStrength;
                float  _DistortScale;
                float4 _DistortScrollA;
                float4 _DistortScrollB;
                float4 _ColorBottom;
                float4 _ColorTop;
                float  _ColorStrength;
                float4 _EmissionColor;
                float  _EmissionStrength;
                float  _EmissionFalloff;
                float  _Opacity;
            CBUFFER_END

            struct AttrE
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryE
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VaryE vertEmit(AttrE IN)
            {
                VaryE OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color;
                return OUT;
            }

            float4 fragEmit(VaryE IN) : SV_Target
            {
                #ifndef _EMISSION
                return float4(0, 0, 0, 0);
                #endif

                // Форма из alpha основной текстуры
                float4 fireTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float  mask    = fireTex.a * IN.color.a;
                clip(mask - 0.01);

                // Небольшая анимация шума для мерцающего свечения
                float  t    = _Time.y;
                float2 uvA  = IN.uv * _DistortScale + _DistortScrollA.xy * t;
                float2 nA   = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, uvA).rg;
                float  flicker = lerp(0.75, 1.0, nA.r);    // лёгкое мерцание

                // Свечение сильнее у основания, угасает к верхушке
                float heightFade = 1.0 - saturate(IN.uv.y / max(_EmissionFalloff, 0.001));

                float3 emission = _EmissionColor.rgb * _EmissionStrength * mask * heightFade * flicker;

                return float4(emission, mask);
            }
            ENDHLSL
        }

    }   // SubShader

    FallBack Off
}

