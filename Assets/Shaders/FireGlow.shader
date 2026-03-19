Shader "Custom/FireGlow"
{
    Properties
    {
        [Header(Fire Texture)]
        _MainTex            ("Fire Texture RGBA",       2D)             = "white" {}

        [Header(Distortion)]
        _DistortTex         ("Distortion Noise RG",     2D)             = "white" {}
        _DistortStrength    ("Distort Strength",        Range(0, 0.1))  = 0.03
        _DistortScale       ("Distort Noise Scale",     Float)          = 1.5
        _ScrollA            ("Scroll Layer A XY",       Vector)         = (0.0, -0.4, 0, 0)
        _ScrollB            ("Scroll Layer B XY",       Vector)         = (0.05, -0.25, 0, 0)

        [Header(Color Tint)]
        [Toggle(_USE_COLOR)]
        _UseColor           ("Apply Color Tint",        Float)          = 1
        _ColorBottom        ("Color Bottom",            Color)          = (1.0, 0.3, 0.0, 1)
        _ColorTop           ("Color Top",               Color)          = (1.0, 0.8, 0.1, 0)
        _ColorStrength      ("Color Blend Strength",    Range(0, 1))    = 0.5

        [Header(Glow Emission)]
        [Toggle(_USE_GLOW)]
        _UseGlow            ("Enable Glow",             Float)          = 1
        [HDR]
        _GlowColor          ("Glow Color HDR",          Color)          = (2.0, 0.6, 0.1, 1)
        _GlowStrength       ("Glow Strength",           Range(0, 8))    = 2.5
        _GlowFalloff        ("Glow Falloff",            Range(0, 1))    = 0.7

        [Header(Output)]
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
            Name "Distortion"
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

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DistortTex_ST;
                float  _DistortStrength;
                float  _DistortScale;
                float4 _ScrollA;
                float4 _ScrollB;
                float4 _ColorBottom;
                float4 _ColorTop;
                float  _ColorStrength;
                float4 _GlowColor;
                float  _GlowStrength;
                float  _GlowFalloff;
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
                float4 color      : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color;
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float  t   = _Time.y;

                // Два слоя шума для смещения UV текстуры огня
                float2 uvA = IN.uv * _DistortScale + _ScrollA.xy * t;
                float2 uvB = IN.uv * _DistortScale * 0.7 + _ScrollB.xy * t;
                float2 nA  = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, uvA).rg;
                float2 nB  = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, uvB).rg;

                // Смещаем UV сэмплирования самой текстуры огня
                float2 distortedUV = IN.uv + ((nA + nB) * 0.5 - 0.5) * _DistortStrength;

                // Читаем текстуру огня по деформированным UV
                float4 fireTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, distortedUV);
                float4 result  = fireTex * IN.color;

                #ifdef _USE_COLOR
                float4 tint = lerp(_ColorBottom, _ColorTop, saturate(IN.uv.y));
                result.rgb  = result.rgb * tint.rgb;
                #endif

                result.a *= _Opacity;
                return result;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Emission"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vertE
            #pragma fragment fragE
            #pragma shader_feature_local _USE_GLOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_DistortTex); SAMPLER(sampler_DistortTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _DistortTex_ST;
                float  _DistortStrength;
                float  _DistortScale;
                float4 _ScrollA;
                float4 _ScrollB;
                float4 _ColorBottom;
                float4 _ColorTop;
                float  _ColorStrength;
                float4 _GlowColor;
                float  _GlowStrength;
                float  _GlowFalloff;
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

            VaryE vertE(AttrE IN)
            {
                VaryE OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color;
                return OUT;
            }

            float4 fragE(VaryE IN) : SV_Target
            {
                #ifndef _USE_GLOW
                return float4(0.0, 0.0, 0.0, 0.0);
                #endif

                float4 fireTex  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float  mask     = fireTex.a * IN.color.a;
                clip(mask - 0.01);

                float  t        = _Time.y;
                float2 uvA      = IN.uv * _DistortScale + _ScrollA.xy * t;
                float2 nA       = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, uvA).rg;
                float  flicker  = lerp(0.75, 1.0, nA.r);

                float  fade     = 1.0 - saturate(IN.uv.y / max(_GlowFalloff, 0.001));
                float3 glow     = _GlowColor.rgb * _GlowStrength * mask * fade * flicker;

                return float4(glow, 1.0);
            }
            ENDHLSL
        }

    }

    FallBack Off
}
