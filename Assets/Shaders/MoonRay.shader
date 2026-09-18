Shader "Custom/MoonRay"
{
    Properties
    {
        [HDR] _Color   ("Color", Color) = (0.75, 0.82, 1.0, 1)
        _FadePower     ("Fade Power", Range(0.1, 10)) = 2.0
        _FadeStart     ("Fade Start", Range(0.0, 1.0)) = 0.1
        _HeadFade      ("Head Fade", Range(0.0, 1.0)) = 0.15
        _TailFade      ("Tail Fade", Range(0.0, 1.0)) = 0.3
        _TopY          ("Top World Y (fade out above)", Float) = 2.5
        _TopFadeH      ("Top Fade Band Height", Float) = 0.8
        _SideFade      ("Side Fade", Range(0.0, 0.5)) = 0.3
        _EdgeSoftness  ("Edge Softness", Range(0.001, 1.0)) = 0.3
        [Toggle] _UseU ("Use U instead of V", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderQueue" = "Transparent+50"
        }

        Pass
        {
            Name "MoonRay"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _FadePower;
                float _FadeStart;
                float _HeadFade;
                float _TailFade;
                float _TopY;
                float _TopFadeH;
                float _SideFade;
                float _EdgeSoftness;
                float _UseU;
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
                float3 worldPos   : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.worldPos = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Choose UV axis: V (default) or U
                float coord = _UseU > 0.5 ? input.uv.x : input.uv.y;

                // Invert so the ray starts visible and fades to transparent at the end
                float fadeCoord = 1.0 - coord;

                // Smoothstep for soft edge at the start of the fade
                float edge = smoothstep(0.0, _EdgeSoftness, fadeCoord);

                // Power curve for the main falloff
                float alpha = saturate(pow(fadeCoord, _FadePower));

                // Combine edge softness with power fade
                alpha *= edge;

                // Apply start offset — keep full opacity near the source
                float startMask = smoothstep(0.0, _FadeStart, coord);
                alpha = lerp(1.0, alpha, startMask);

                // Head fade — transparent at the very top of the ray,
                // ramping in over the first _HeadFade fraction of its length
                float headMask = smoothstep(0.0, _HeadFade, coord);
                alpha *= headMask;

                // Tail fade — mirror of the head: fade out to fully transparent
                // over the last _TailFade fraction of the ray length
                float tailMask = 1.0 - smoothstep(1.0 - _TailFade, 1.0, coord);
                alpha *= tailMask;

                // World-space top cutoff — rays vanish as they approach the glass plane,
                // regardless of quad length or particle drift
                float topMask = 1.0 - smoothstep(_TopY - _TopFadeH, _TopY, input.worldPos.y);
                alpha *= topMask;

                // Side fade — soft fade to transparent along both side edges of the ray
                float widthCoord = _UseU > 0.5 ? input.uv.y : input.uv.x;
                float sideMask = smoothstep(0.0, _SideFade, widthCoord)
                               * smoothstep(1.0, 1.0 - _SideFade, widthCoord);
                alpha *= sideMask;

                half3 rgb = _Color.rgb;
                alpha *= _Color.a;

                // Apply fog
                rgb = MixFog(rgb, input.fogFactor);

                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
