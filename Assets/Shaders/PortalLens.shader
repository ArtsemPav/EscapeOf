Shader "Custom/PortalLens"
{
    // Camera-facing black-hole lens quad. Bends the opaque scene like gravity does:
    // the background around the center is pulled into a ring (Einstein-ring profile),
    // twisted by frame-dragging, split chromatically and swallowed by a black event horizon.
    // Requires "Opaque Texture" enabled in the URP asset.
    Properties
    {
        _Strength  ("Deflection Strength", Range(0, 3)) = 1.0
        _Einstein  ("Einstein Radius (quad units)", Range(0.02, 0.6)) = 0.22
        _Swirl     ("Frame-Drag Twist (rad)", Range(0, 6)) = 2.2
        _Chromatic ("Chromatic Split", Range(0, 0.5)) = 0.18
        _Horizon   ("Event Horizon Radius (quad units)", Range(0, 0.5)) = 0.06
        _Shadow    ("Shadow Darkening", Range(0, 1)) = 0.6
        _Opacity   ("Opacity", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+10"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalLens"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float _Einstein;
                float _Swirl;
                float _Chromatic;
                float _Horizon;
                float _Shadow;
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
                float4 screenPos  : TEXCOORD1;
                float  halfWidthUV : TEXCOORD2; // half quad width in screen UV (x axis)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float sx = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float sy = length(float3(unity_ObjectToWorld._m01, unity_ObjectToWorld._m11, unity_ObjectToWorld._m21));
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up    = UNITY_MATRIX_V[1].xyz;
                float3 posWS = centerWS + right * IN.positionOS.x * sx + up * IN.positionOS.y * sy;

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.uv = IN.uv;

                // Projected half-width of the quad, so deflection is measured in quad units
                float4 centerCS = TransformWorldToHClip(centerWS);
                float4 edgeCS = TransformWorldToHClip(centerWS + right * sx * 0.5);
                OUT.halfWidthUV = abs(edgeCS.x / edgeCS.w - centerCS.x / centerCS.w) * 0.5;
                return OUT;
            }

            float2 rotate2(float2 p, float a)
            {
                float s = sin(a);
                float c = cos(a);
                return float2(p.x * c - p.y * s, p.x * s + p.y * c);
            }

            half3 sampleScene(float2 suv)
            {
                return SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, saturate(suv)).rgb;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 c = (IN.uv - 0.5) * 2.0;     // -1..1 across the quad
                float r = length(c);
                float2 dir = c / max(r, 0.0001);

                float rim = 1.0 - smoothstep(0.45, 1.0, r);   // deflection fades to nothing at the edge

                // Einstein-ring deflection: alpha = E^2 / r  (strong near the center)
                float rSafe = max(r, 0.04);
                float deflection = _Strength * _Einstein * _Einstein / rSafe * rim;

                // Frame dragging: twist the background more the closer it is, slowly breathing
                float twist = _Swirl * pow(saturate(1.0 - r), 2.0) * (1.0 + 0.15 * sin(_Time.y * 0.9));

                // Source position seen at this pixel (in quad units)
                float2 srcR = rotate2(c - dir * deflection * (1.0 + _Chromatic), twist);
                float2 srcG = rotate2(c - dir * deflection, twist);
                float2 srcB = rotate2(c - dir * deflection * (1.0 - _Chromatic), twist);

                // Quad units -> screen UV (quad is square in pixels)
                float2 suv = IN.screenPos.xy / IN.screenPos.w;
                float2 unit = float2(IN.halfWidthUV, IN.halfWidthUV * _ScreenParams.x / _ScreenParams.y);

                half3 col;
                col.r = sampleScene(suv + (srcR - c) * unit).r;
                col.g = sampleScene(suv + (srcG - c) * unit).g;
                col.b = sampleScene(suv + (srcB - c) * unit).b;

                // Gravitational shadow: light dims toward the horizon, which is fully black
                float shadow = lerp(1.0 - _Shadow, 1.0, smoothstep(_Horizon, _Horizon * 5.0 + 0.1, r));
                col *= shadow;
                float horizon = 1.0 - smoothstep(_Horizon * 0.85, _Horizon, r);
                col = lerp(col, half3(0, 0, 0), horizon);

                // Fully opaque near the center, blending back to the real scene at the rim
                float alpha = saturate(rim * 1.6) * _Opacity;
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
