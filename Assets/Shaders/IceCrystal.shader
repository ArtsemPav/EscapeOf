Shader "Custom/IceCrystal"
{
    Properties
    {
        [Header(Ice Surface)]
        _IceColor       ("Ice Color",           Color) = (0.62, 0.85, 1.0, 1)
        _DeepColor      ("Deep Color",          Color) = (0.04, 0.13, 0.21, 1)
        _NoiseScale     ("Noise Scale",         Float) = 1.5
        _FresnelPower   ("Fresnel Power",       Range(0.5, 8)) = 3.0
        _IridescenceStrength ("Iridescence",    Range(0, 2)) = 0.8

        [Header(Reflections)]
        _EnvSpeed       ("Environment Speed",   Float) = 1.0
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 0.8

        [Header(Refraction)]
        _RefractStrength ("Refraction Strength", Range(0, 0.5)) = 0.20
        _WobbleStrength  ("Internal Wobble",     Range(0, 1)) = 0.35
        _BlurStrength    ("Thick Blur",          Range(0, 20)) = 6.0

        [Header(Internal Sparkles)]
        _SparkleIntensity ("Sparkle Intensity", Range(0, 3)) = 1.0
        _SparkleScale     ("Sparkle Density",   Float) = 24.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent+20"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "IceCrystal"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                half4  _IceColor;
                half4  _DeepColor;
                float  _NoiseScale;
                float  _FresnelPower;
                float  _IridescenceStrength;
                float  _EnvSpeed;
                float  _ReflectionStrength;
                float  _RefractStrength;
                float  _WobbleStrength;
                float  _BlurStrength;
                float  _SparkleIntensity;
                float  _SparkleScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 normalVS   : TEXCOORD2;
                float4 screenPos  : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(hash(i), hash(i + float2(1.0, 0.0)), u.x),
                    lerp(hash(i + float2(0.0, 1.0)), hash(i + float2(1.0, 1.0)), u.x),
                    u.y);
            }

            float fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    value += amplitude * noise(p);
                    p *= 2.1;
                    amplitude *= 0.5;
                }
                return value;
            }

            float3 iridescentColor(float t)
            {
                return 0.5 + 0.5 * cos(6.28318 * (t + float3(0.0, 0.33, 0.67)));
            }

            // Procedural living environment: sky gradient + two orbiting light
            // sources + drifting aurora streaks, used for surface reflections.
            float3 environment(float3 dir, float t)
            {
                float h = clamp(dir.y * 0.5 + 0.5, 0.0, 1.0);
                float3 sky = lerp(float3(0.02, 0.05, 0.12), float3(0.30, 0.60, 0.90), h);

                float ang1 = t * 0.55 * _EnvSpeed;
                float3 light1 = normalize(float3(cos(ang1), 0.55 * sin(t * 0.31 * _EnvSpeed), sin(ang1)));
                float band1 = pow(max(dot(dir, light1), 0.0), 28.0);

                float ang2 = -t * 0.38 * _EnvSpeed + 2.2;
                float3 light2 = normalize(float3(cos(ang2), 0.8 * sin(ang2), sin(ang2 * 0.7)));
                float band2 = pow(max(dot(dir, light2), 0.0), 14.0);

                float aur = fbm(dir.xy * 2.2 + float2(t * 0.10 * _EnvSpeed, -t * 0.06 * _EnvSpeed) + dir.z);
                aur = smoothstep(0.55, 0.85, aur);

                float3 warm = float3(1.00, 0.75, 0.45);
                float3 cool = float3(0.50, 0.85, 1.00);
                return sky + warm * band1 * 3.0 + cool * band2 * 1.8 + cool * aur * 0.30;
            }

            // Single ROUND soft sparkle: random dot inside the cell, smooth
            // falloff, slow twinkle — no square cells, no strobing.
            float sparkleCell(float3 samplePos)
            {
                float3 cell = floor(samplePos);
                float3 f = frac(samplePos);
                float rnd = hash(cell.xy + cell.z * 17.0);
                float3 offset = float3(
                    hash(cell.xy + 3.7),
                    hash(cell.yz + 9.1),
                    hash(cell.zx + 5.3)) * 0.6 + 0.2;
                float d = length(f - offset);
                float flash = step(0.975, rnd);
                float twinkle = 0.5 + 0.5 * sin(_Time.y * 2.0 + rnd * 62.8318);
                float dotMask = smoothstep(0.30, 0.05, d);
                return flash * twinkle * dotMask;
            }

            // Sparkles INSIDE the volume: sample along the view ray at several
            // depths — they parallax against the surface as the camera moves.
            float internalSparkles(float3 worldPos, float3 viewDirWS)
            {
                float acc = 0.0;
                for (int i = 1; i <= 3; i++)
                {
                    float depth = float(i) * 0.16;
                    float3 p = (worldPos + viewDirWS * depth) * _SparkleScale;
                    p += float3(0.0, -_Time.y * 0.12, _Time.y * 0.05);
                    acc += sparkleCell(p) / float(i);
                }
                return acc;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.screenPos  = vpi.positionNDC;

                // world normal -> view space
                OUT.normalVS = mul((float3x3)UNITY_MATRIX_V, vni.normalWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t = _Time.y;
                float3 normal  = normalize(IN.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float ndv = saturate(dot(normal, viewDir));
                float fresnel = pow(1.0 - ndv, _FresnelPower);

                // Fake optical thickness: looking through the middle of the
                // body = thick, grazing silhouettes = thin, modulated by
                // internal density noise. World-space noise keeps the pattern
                // at real size regardless of the mesh's transform scale.
                float density = fbm(IN.positionWS * _NoiseScale);
                float thickness = clamp(ndv * (0.55 + 0.45 * density), 0.0, 1.0);

                float3 reflectDir = reflect(-viewDir, normal);
                float3 refractDir = refract(-viewDir, normal, 0.72);
                float3 envRefl = environment(reflectDir, t);
                float3 envRefr = environment(normalize(refractDir + float3(0.0, 0.25, 0.0)), t * 0.7);

                // Refracted background in SCREEN SPACE. Two independent
                // distortions: refraction from the view-space normal, and a
                // noise wobble from WORLD position — the latter does not
                // vanish face-on, so the thick core visibly warps everything.
                float refrAmount = pow(thickness, 1.3);
                float2 suv = IN.screenPos.xy / IN.screenPos.w;

                float2 warp = float2(
                    fbm(IN.positionWS.zy * _NoiseScale * 1.7 - t * 0.1),
                    fbm(IN.positionWS.xz * _NoiseScale * 1.7 + t * 0.12)) - 0.5;
                float wobble = fbm(IN.positionWS.xy * _NoiseScale * 2.0 + warp * 3.0 + t * 0.15) - 0.5;
                float2 refrScreen = IN.normalVS.xy * _RefractStrength * (0.4 + 0.6 * refrAmount)
                                  + (warp * 1.2 + wobble) * _WobbleStrength * (0.2 + 0.8 * refrAmount);

                float2 refrUv = clamp(suv + refrScreen, 0.001, 0.999);
                float blur = thickness * _BlurStrength;

                // 5-tap cross blur — thicker ice gets fuzzier (frost inside).
                // _ScreenSize.zw = 1/screen width, 1/screen height.
                float2 texel = _ScreenSize.zw;
                float3 bg = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv + float2(texel.x, 0) * blur).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv - float2(texel.x, 0) * blur).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv + float2(0, texel.y) * blur).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv - float2(0, texel.y) * blur).rgb;
                bg /= 5.0;

                // What you see through the ice: distorted, blurred background.
                float3 seen = bg + envRefr * lerp(0.45, 0.15, thickness);

                // Fogging: sharp contrast — thin parts clear, thick core
                // dissolves into deep ice.
                float3 fogColor = _DeepColor.rgb * 1.7 + envRefl * 0.12 + float3(0.05, 0.12, 0.22);
                float fog = smoothstep(0.35, 0.85, thickness) * 0.93;
                float3 through = lerp(seen, fogColor, fog);

                float3 body = lerp(_IceColor.rgb, _DeepColor.rgb, thickness * 0.85) * lerp(0.22, 0.55, thickness);

                float shimmerPhase = IN.positionWS.y * 1.2 + density * 4.0 + t * 0.35;
                float3 shimmer = iridescentColor(shimmerPhase) * _IridescenceStrength * (0.18 + fresnel);

                float spark = internalSparkles(IN.positionWS, viewDir) * _SparkleIntensity;

                float3 color = through
                             + body
                             + envRefl * (0.30 + 0.70 * fresnel) * _ReflectionStrength
                             + shimmer
                             + float3(1.0, 0.98, 0.92) * spark * 2.4
                             + float3(0.40, 0.70, 1.00) * fresnel * 0.5;

                // Thickness-driven transparency: thin spots reveal the true
                // background behind, the thick core is dense and hides what's
                // inside it. Sparkles punch through regardless.
                float alpha = smoothstep(0.30, 0.80, thickness) * 0.82 + 0.15;
                alpha = max(alpha, spark * 0.85);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}