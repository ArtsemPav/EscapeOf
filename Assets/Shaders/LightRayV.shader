Shader "Custom/LightRay"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)
        _fade       ("fade", Range(0, 5)) = 1.0
        _Float      ("Float", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderQueue" = "Transparent"
        }

        Pass
        {
            Name "LightRay"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _fade;
                float _Float;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Fade computed on V coordinate (uv.y)
                // Original shadergraph was using U (uv.x) — swapped to V
                float coord = input.uv.y;

                // smoothstep(Edge1=0.05, Edge2=_fade, coord)
                float fadeSmooth = smoothstep(0.05, _fade, coord);

                // pow(result, 2.0)
                float fadePower = pow(fadeSmooth, 2.0);

                half3 rgb   = _Color.rgb * fadePower;
                half  alpha = fadePower;

                // Apply fog
                rgb = MixFog(rgb, input.fogFactor);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
