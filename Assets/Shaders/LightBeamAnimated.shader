Shader "Custom/LightBeamAnimated"
{
    Properties
    {
        [HDR] _Color       ("Color", Color)          = (1, 1, 1, 1)
        _fade              ("fade", Range(0, 5))     = 1.0
        _Float             ("Float", Float)          = 1.0

        _MusicIntensity    ("Music Intensity", Range(0, 2))  = 0.0
        _ScrollSpeed       ("Scroll Speed", Range(0, 3))     = 0.4
        _WaveStrength      ("Wave Strength", Range(0, 1))    = 0.4
        _WaveFrequency     ("Wave Frequency", Range(0, 20))  = 6.0
        _EdgeSharpness      ("Edge Sharpness", Range(1, 8))   = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "RenderQueue"    = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType"    = "Plane"
        }

        Pass
        {
            Name "LightBeamAnimated"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                // ProjectorLightFlicker writes these — same layout as Custom/LightRay
                half4 _Color;
                float _fade;
                float _Float;
                // Music-driven animation layer
                float _MusicIntensity;
                float _ScrollSpeed;
                float _WaveStrength;
                float _WaveFrequency;
                float _EdgeSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  edge       : TEXCOORD1;  // cylindrical edge fade
                float  fogFactor  : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                // Cylindrical edge fade: beam is brighter in the middle,
                // soft toward the cone silhouette edges.
                float nx = input.normalOS.x;
                float ny = input.normalOS.z;
                float radial = sqrt(nx * nx + ny * ny);
                output.edge = saturate(pow(saturate(radial), 1.0 / _EdgeSharpness));

                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ── Base fade along the beam (same shape as Custom/LightRay) ──
                float fadeSmooth = smoothstep(0.05, _fade, input.uv.y);
                float fadePower  = pow(fadeSmooth, 2.0);

                // ── Music layer: scrolling energy waves along the beam ──────
                // Scroll the V coordinate downward so light pulses travel
                // from the source (top) toward the floor.
                float scroll     = frac(input.uv.y - _Time.y * _ScrollSpeed);
                float wave       = 0.5 + 0.5 * sin((scroll * _WaveFrequency - _Time.y * 2.0) * 6.2831);
                wave             = pow(wave, 2.0);

                // Sharp pulses at high music intensity, gentle shimmer at low
                float musicWave  = lerp(0.75, wave, saturate(_MusicIntensity));

                // ── Combine ─────────────────────────────────────────────────
                float energy     = fadePower * input.edge;
                float brightness = 1.0 + _MusicIntensity * musicWave * 1.5;

                half3 rgb   = _Color.rgb * energy * brightness;
                half  alpha = saturate(energy * brightness);

                rgb = MixFog(rgb, input.fogFactor);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
