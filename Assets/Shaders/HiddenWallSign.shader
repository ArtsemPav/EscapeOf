Shader "Custom/HiddenWallSign"
{
    Properties
    {
        _MainTex      ("Sprite Texture", 2D)    = "white" {}
        _Color        ("Tint",           Color)  = (1,1,1,1)
        // Set per-instance via MaterialPropertyBlock from HiddenWallSign.cs
        _FlashlightPos    ("Flashlight Position",       Vector) = (0,0,0,0)
        _FlashlightDir    ("Flashlight Direction",      Vector) = (0,0,1,0)
        _SpotAngleCos     ("Spot Angle Cosine",         Float)  = 0.866
        _EdgeSoftness     ("Edge Softness",             Float)  = 0.05
        _RadialFalloff    ("Radial Falloff",            Float)  = 1.5
        _MaxVisibleDist   ("Max Visible Distance",      Float)  = 2.0
        _MinDistAlpha     ("Min Alpha At Max Distance", Float)  = 0.04
        [HDR] _EmissionColor ("Emission Color",         Color)  = (0,0.5,2,1)
        _EmissionIntensity   ("Emission Intensity",     Float)  = 2.0
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull   Off
        ZWrite Off

        Pass
        {
            Name "HiddenWallSign"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float3 _FlashlightPos;
                float3 _FlashlightDir;
                float  _SpotAngleCos;
                float  _EdgeSoftness;
                float  _RadialFalloff;
                float  _MaxVisibleDist;
                float  _MinDistAlpha;
                float4 _EmissionColor;
                float  _EmissionIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;      // SpriteRenderer vertex color
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color      = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                float3 toFrag   = IN.positionWS - _FlashlightPos;
                float  dist     = length(toFrag);
                float3 toFragN  = toFrag / (dist + 1e-5);
                float3 lightDir = normalize(_FlashlightDir);

                // cosAngle: 1.0 = center of beam, _SpotAngleCos = cone edge, < that = outside
                float cosAngle = dot(toFragN, lightDir);

                // ── Edge cutoff: smooth fade at the cone boundary ─────────────────
                float coneMask = smoothstep(
                    _SpotAngleCos - _EdgeSoftness,
                    _SpotAngleCos + _EdgeSoftness,
                    cosAngle
                );

                // ── Radial fade: bright center → dim edges inside the cone ────────
                // radialT = 0 at cone edge, 1 at center
                float radialT    = saturate((cosAngle - _SpotAngleCos) / (1.0 - _SpotAngleCos + 1e-5));
                float radialFade = pow(radialT, _RadialFalloff);

                // ── Distance fade ─────────────────────────────────────────────────
                // Full brightness up to MaxDist*0.5, smooth fade to 0 at MaxDist.
                // With default MaxDist=2: full at ≤1 m, gone at ≥2 m.
                float distFade = 1.0 - smoothstep(_MaxVisibleDist * 0.5, _MaxVisibleDist, dist);

                // Combined visibility mask (alpha only)
                float mask = coneMask * radialFade * distFade;

                // ── Emission: HDR color makes Bloom pick up the glow ─────────────
                // texColor.a drives where the glyph is; RGB is replaced by emission.
                // Multiplying by radialFade makes the center glow brighter than edges.
                half3 emissive = texColor.rgb * _EmissionColor.rgb * _EmissionIntensity * radialFade * distFade;

                // Tint color is applied on top for any non-emissive remnant
                half4 col;
                col.rgb = emissive * _Color.rgb;
                col.a   = texColor.a * _Color.a * IN.color.a * mask;

                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden"
}
