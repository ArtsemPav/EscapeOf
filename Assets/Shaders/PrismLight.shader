Shader "Custom/PrismLight"
{
    Properties
    {
        [HDR] _BaseColor  ("Base Color", Color) = (1, 1, 1, 1)
        _ColorIntensity   ("Color Intensity", Range(0, 10)) = 2.0
        _FresnelPower     ("Fresnel Power", Range(0.1, 8)) = 3.0
        _FresnelIntensity ("Rim Intensity", Range(0, 10)) = 3.0
        _Dispersion       ("Rainbow Dispersion", Range(0, 2)) = 1.0
        _ShimmerSpeed     ("Shimmer Speed", Range(0, 5)) = 1.0
        _ShimmerScale     ("Shimmer Scale", Range(0.5, 20)) = 5.0
        _PulseSpeed       ("Pulse Speed", Range(0, 10)) = 2.0
        _PulseAmount      ("Pulse Amount", Range(0, 1)) = 0.2
        _VerticalFade     ("Vertical Fade", Range(0, 1)) = 0.5
        _FadeMin          ("Fade Min Y", Float) = 0.0
        _FadeMax          ("Fade Max Y", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderQueue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PrismLight"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _ColorIntensity;
                float _FresnelPower;
                float _FresnelIntensity;
                float _Dispersion;
                float _ShimmerSpeed;
                float _ShimmerScale;
                float _PulseSpeed;
                float _PulseAmount;
                float _VerticalFade;
                float _FadeMin;
                float _FadeMax;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float3 positionOS : TEXCOORD5;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS  = GetWorldSpaceNormalizeViewDir(positionWS);
                output.uv         = input.uv;
                output.positionOS = input.positionOS.xyz;
                output.fogFactor  = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half3 Spectrum(float t)
            {
                half3 c;
                c.r = saturate(1.0 - abs(t - 0.0) * 2.0);
                c.g = saturate(1.0 - abs(t - 0.5) * 2.0);
                c.b = saturate(1.0 - abs(t - 1.0) * 2.0);
                return c;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float time = _Time.y;

                float ndotv   = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                float fresnel = pow(1.0 - ndotv, _FresnelPower);

                float fr  = pow(1.0 - ndotv, _FresnelPower * (1.0 + 0.25 * _Dispersion));
                float fg  = fresnel;
                float fb  = pow(1.0 - ndotv, _FresnelPower * (1.0 - 0.25 * _Dispersion));
                float3 dispersedFresnel = float3(fr, fg, fb);

                float3 rainbow = Spectrum(saturate(fresnel * _Dispersion + frac(input.positionOS.y * 0.5 + time * 0.1)));

                float shimmerCoord = input.positionWS.x * _ShimmerScale
                                   + input.positionWS.y * _ShimmerScale * 0.7
                                   - time * _ShimmerSpeed;
                float shimmer = 0.5 + 0.5 * sin(shimmerCoord + sin(shimmerCoord * 0.5) * 2.0);
                shimmer = pow(shimmer, 3.0);

                float pulse = 1.0 - _PulseAmount * 0.5 * (1.0 + sin(time * _PulseSpeed));

                float height01 = saturate((input.positionOS.y - _FadeMin) / max(_FadeMax - _FadeMin, 0.001));
                float verticalFade = lerp(1.0, smoothstep(0.0, 1.0, height01), _VerticalFade);

                half3 rainbowEdge = rainbow * fresnel * 3.0;
                half3 rgb = (_BaseColor.rgb * (dispersedFresnel * _FresnelIntensity + shimmer * 0.5) + rainbowEdge)
                          * _ColorIntensity * pulse * verticalFade;

                half alpha = saturate(fresnel * _FresnelIntensity * 0.5 + shimmer * 0.3) * pulse;

                rgb = MixFog(rgb, input.fogFactor);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
