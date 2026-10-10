Shader "Custom/PortalAccretionDisk"
{
    // Camera-facing additive quad drawing a black-hole accretion disk seen almost edge-on:
    //  - a thin, turbulent, differentially rotating disk band crossing in front of the hole,
    //  - the far side of the disk bent into a bright arc over (and under) the event horizon,
    //  - relativistic Doppler beaming (one side much brighter and whiter),
    //  - temperature gradient: white-hot inner edge to dark orange outer edge.
    // The black hole itself is drawn by PortalLens; this pass only adds light.
    Properties
    {
        [HDR] _ColorInner ("Inner (hot) Color", Color) = (3.2, 2.6, 1.9, 1)
        [HDR] _ColorOuter ("Outer (cool) Color", Color) = (1.6, 0.5, 0.1, 1)

        _Intensity     ("Intensity", Float) = 2.0
        _Flatten       ("Disk Flatten (edge-on)", Range(0.03, 0.6)) = 0.17
        _InnerRadius   ("Disk Inner Radius (quad units)", Range(0.01, 0.9)) = 0.1
        _OuterRadius   ("Disk Outer Radius (quad units)", Range(0.05, 1)) = 0.6
        _Horizon       ("Event Horizon Radius (quad units)", Range(0, 0.5)) = 0.065
        _Falloff       ("Radial Falloff", Range(0.5, 5)) = 1.8
        _Spin          ("Rotation Speed", Float) = 0.7
        _Doppler       ("Doppler Beaming", Range(0, 0.9)) = 0.55
        _ArcIntensity  ("Lensed Arc Intensity", Float) = 1.4
        _Turbulence    ("Turbulence", Range(0, 1.5)) = 0.9
        _Opacity       ("Opacity", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+31"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalAccretionDisk"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorInner;
                half4 _ColorOuter;
                float _Intensity;
                float _Flatten;
                float _InnerRadius;
                float _OuterRadius;
                float _Horizon;
                float _Falloff;
                float _Spin;
                float _Doppler;
                float _ArcIntensity;
                float _Turbulence;
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
                OUT.uv = IN.uv;
                return OUT;
            }

            // Turbulent, differentially rotating gas: inner orbits are much faster than outer ones
            float diskGas(float radius, float angle)
            {
                float t = _Time.y;
                float omega = _Spin * pow(max(_InnerRadius, 0.001) / max(radius, 0.001), 1.5);
                float a = angle + t * omega;
                float2 ring = float2(cos(a), sin(a));

                float n1 = gradientNoise(ring * 2.2 + float2(radius * 14.0, radius * 9.0));
                float n2 = gradientNoise(ring * 5.5 + float2(radius * 30.0, -radius * 21.0) + 7.7);
                float gas = 0.65 + _Turbulence * (n1 * 1.1 + n2 * 0.5);

                // Fine concentric banding
                gas *= 0.85 + 0.15 * sin(radius * 140.0 + n1 * 4.0);
                return max(gas, 0.0);
            }

            half3 diskColor(float radius, float side)
            {
                // Hot white inner edge to cool dark-orange outer edge
                float heat = saturate(pow(_InnerRadius / max(radius, 0.001), 1.2));
                half3 color = lerp(_ColorOuter.rgb, _ColorInner.rgb, heat);
                // The approaching side is blue-shifted toward white
                color = lerp(color, half3(1.6, 1.7, 2.0), saturate(-side) * 0.25);
                return color;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 p = (IN.uv - 0.5) * 2.0;
                float d = length(p);

                float quadEdge = 1.0 - smoothstep(0.8, 1.0, d);
                if (quadEdge <= 0.0)
                    return half4(0, 0, 0, 1);

                float3 total = 0;

                // --- Front disk band (flattened ellipse in the disk plane) ---
                float2 q = float2(p.x, p.y / _Flatten);
                float rho = length(q);
                float phi = atan2(q.y, q.x);
                float side = q.x / max(rho, 0.0001);   // -1 = approaching (left), +1 = receding

                float inner = smoothstep(_InnerRadius, _InnerRadius * 1.08, rho);
                float outer = 1.0 - smoothstep(_OuterRadius * 0.55, _OuterRadius, rho);
                float radial = pow(_InnerRadius / max(rho, 0.001), _Falloff) * inner * outer;

                // Beaming: approaching side far brighter
                float beaming = pow(saturate(1.0 - _Doppler * side), 2.4) * 0.85 + 0.15;

                // The part of the disk behind the hole is hidden by the horizon
                float behind = q.y > 0.0 ? smoothstep(_Horizon * 1.0, _Horizon * 1.2, d) : 1.0;

                float bandGas = diskGas(rho, phi);
                total += diskColor(rho, side) * radial * bandGas * beaming * behind;

                // --- Lensed far side: disk image bent over the hole in a ring ---
                float arcCenter = _Horizon * 1.55;
                float arcWidth = _Horizon * 0.5;
                float arcProfile = exp(-pow((d - arcCenter) / arcWidth, 2.0));
                float2 pn = p / max(d, 0.0001);
                // Strong on top (back of the disk), faint underneath
                float arcAngularWeight = lerp(0.18, 1.0, smoothstep(-0.35, 0.75, pn.y));
                float arcAngle = atan2(pn.y, pn.x);
                float arcSide = pn.x;
                float arcGas = diskGas(arcCenter * 1.6, arcAngle);
                float arcMask = smoothstep(_Horizon * 1.02, _Horizon * 1.2, d);
                float arcBeam = pow(saturate(1.0 - _Doppler * arcSide), 2.4) * 0.85 + 0.15;
                total += diskColor(_InnerRadius * 1.05, arcSide) * arcProfile * arcAngularWeight
                       * arcGas * arcBeam * arcMask * _ArcIntensity;

                // Faint wider secondary halo (higher-order image)
                float halo = exp(-pow((d - arcCenter * 1.85) / (arcWidth * 2.4), 2.0)) * 0.18 * arcAngularWeight;
                total += _ColorOuter.rgb * halo * arcMask * arcBeam;

                total *= _Intensity * _Opacity * quadEdge;
                return half4(total, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
