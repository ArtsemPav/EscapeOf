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

        [Header(Light Layers)]
        _LightLayer0 ("Light Layer Bit #1",  Int) = -1
        _LightLayer1 ("Light Layer Bit #2",  Int) = -1
        _LightLayer2 ("Light Layer Bit #3",  Int) = -1
        _LightLayer3 ("Light Layer Bit #4",  Int) = -1

        [Header(Lighting)]
        _SpecularStrength ("Specular Strength", Range(0, 4)) = 1.2
        _SpecularPower    ("Specular Sharpness", Range(8, 256)) = 64.0
        _AmbientStrength  ("Ambient Strength",   Range(0, 2)) = 0.6
        _LightAbsorption  ("Light Absorption",   Range(0, 1)) = 0.5

        [Header(Reflections)]
        _EnvSpeed       ("Environment Speed",   Float) = 1.0
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 0.8

        [Header(Refraction)]
        _RefractStrength ("Refraction Strength", Range(0, 0.5)) = 0.20
        _WobbleStrength  ("Internal Wobble",     Range(0, 1)) = 0.35
        _BlurStrength    ("Thick Blur",          Range(0, 20)) = 6.0

        [Header(Internal Sparkles)]
        _SparkleIntensity ("Sparkle Intensity", Range(0, 3)) = 1.0
        _SparkleScale     ("Sparkle Density",   Float) = 6.0
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
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // Real URP lights: main directional + per-pixel additional lights
            // (room lamps, LightZone lights). Shadows on the main light.
            // NOTE: _FORWARD_PLUS is deprecated since URP 6.1 — cluster light
            // loop is the current keyword; it makes GetAdditionalLightsCount()
            // return real cluster data in Forward+ mode.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                half4  _IceColor;
                half4  _DeepColor;
                float  _NoiseScale;
                float  _FresnelPower;
                float  _IridescenceStrength;
                int    _LightLayer0;
                int    _LightLayer1;
                int    _LightLayer2;
                int    _LightLayer3;
                float  _SpecularStrength;
                float  _SpecularPower;
                float  _AmbientStrength;
                float  _LightAbsorption;
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
                half   fogFactor  : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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

            // Light-layer filter: the material lists LIGHT LAYER BITS
            // (_LightLayer0..3, 0-based bit numbers as shown by the light's
            // renderingLayers mask, -1 = unused). A lamp lights the crystal
            // only if its layer bit matches one of them. Lamp with no layers
            // (mask 0) is treated as match-all.
            bool LightMatchesLayer(uint lightLayerMask)
            {
                int bits[4] = { _LightLayer0, _LightLayer1, _LightLayer2, _LightLayer3 };
                if (lightLayerMask == 0)
                    return true;
                for (int k = 0; k < 4; k++)
                {
                    int b = bits[k];
                    if (b < 0 || b > 31)
                        continue;
                    if (lightLayerMask & (1u << b))
                        return true;
                }
                // No layer fields set (all -1) = accept all lights.
                if (bits[0] < 0 && bits[1] < 0 && bits[2] < 0 && bits[3] < 0)
                    return true;
                return false;
            }

            // Procedural "sky" fallback used for reflections when there is no
            // meaningful environment contribution — dimmer in dark rooms.
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

            // Approximate volumetric light spill: how strongly the crystal's
            // interior glows from nearby real lights. Cluster loop.
            float3 interiorLightGlow(InputData inputData, float3 normalWS)
            {
                float3 glow = 0;
                int lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    if (!LightMatchesLayer(light.layerMask))
                        continue;
                    float distAtt = saturate(light.distanceAttenuation);
                    distAtt *= saturate(dot(normalWS, light.direction) * 0.5 + 0.5);
                    glow += light.color * distAtt;
                LIGHT_LOOP_END
                return saturate(glow * 0.5);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs vpi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vni = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = vpi.positionCS;
                OUT.positionWS = vpi.positionWS;
                OUT.normalWS   = vni.normalWS;
                OUT.screenPos  = vpi.positionNDC;
                OUT.fogFactor  = ComputeFogFactor(vpi.positionCS.z);

                // world normal -> view space
                OUT.normalVS = mul((float3x3)UNITY_MATRIX_V, vni.normalWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float t = _Time.y;
                float3 normal  = normalize(IN.normalWS);
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float ndv = saturate(dot(normal, viewDir));
                float fresnel = pow(1.0 - ndv, _FresnelPower);

                // Fake optical thickness: looking through the middle of the
                // body = thick, grazing silhouettes = thin, modulated by
                // internal density noise.
                float density = fbm(IN.positionWS * _NoiseScale);
                float thickness = clamp(ndv * (0.55 + 0.45 * density), 0.0, 1.0);

                // ===== REAL URP LIGHTING =====
                // InputData drives the cluster light loop in Forward+ — the
                // LIGHT_LOOP_BEGIN macro requires the variable be named
                // exactly `inputData`.
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.positionCS = IN.positionCS;
                inputData.normalWS = normal;
                inputData.viewDirectionWS = viewDir;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // Main light (directional / sun) with shadows.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord, IN.positionWS, 1.0);
                float3 mainLightColor = mainLight.color * mainLight.shadowAttenuation;

                // Additional lights (room lamps, LightZone lights) —
                // per-pixel cluster loop, so flicker and on/off follow the
                // real lamps. GetAdditionalLightsCount() returns 0 in
                // Forward+, the cluster iterator enumerates lights itself.
                float3 additionalLightSum = 0;
                float3 additionalDirect = 0;
                float3 additionalSpecular = 0;

                int lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    if (!LightMatchesLayer(light.layerMask))
                        continue;
                    // light.color carries the lamp's raw colour * intensity;
                    // distance falloff lives separately in
                    // light.distanceAttenuation — must multiply it here,
                    // otherwise the crystal glows as if the lamp touched it.
                    float3 lampColor = light.color * light.distanceAttenuation * light.shadowAttenuation;
                    additionalLightSum += lampColor;
                    additionalDirect += lampColor * saturate((dot(normal, light.direction) + 0.6) / 1.6);
                    float3 h = normalize(light.direction + viewDir);
                    additionalSpecular += lampColor * pow(saturate(dot(normal, h)), _SpecularPower);
                LIGHT_LOOP_END

                // Ambient / spherical harmonics (indoor bounce, skybox).
                float3 ambient = SampleSH(normal);

                // Main light: wrap diffuse + specular, same as lamps.
                float3 mainColor = mainLightColor;
                float3 directLight = additionalDirect + mainColor * saturate((dot(normal, mainLight.direction) + 0.6) / 1.6);
                float3 hMain = normalize(mainLight.direction + viewDir);
                float3 specular = (additionalSpecular + mainColor * pow(saturate(dot(normal, hMain)), _SpecularPower)) * _SpecularStrength;
                // How much light survives INSIDE the crystal (thickness
                // absorption): thick core stays dark even under a lamp.
                float lightThrough = exp(-thickness * _LightAbsorption * 2.5);

                // Glow of the interior from nearby lights (volumetric-ish).
                float3 glow = interiorLightGlow(inputData, normal);

                // Lighting multiplier applied to the ice body colour and the
                // seen-through background. Ambient keeps silhouettes visible.
                float3 lightingMul = ambient * _AmbientStrength + directLight * lightThrough + glow * lightThrough;

                // Global light level from the REAL scene lights (direct +
                // interior glow). Deliberately NO ambient here: ambient SH
                // is always-on sky/bounce light, and if it counted toward
                // the light level the crystal would glow in a dark room.
                float sceneLightLevel = saturate(Luminance(directLight + glow) * 2.0);

                float3 reflectDir = reflect(-viewDir, normal);
                float3 refractDir = refract(-viewDir, normal, 0.72);
                float3 envRefl = environment(reflectDir, t);
                float3 envRefr = environment(normalize(refractDir + float3(0.0, 0.25, 0.0)), t * 0.7);

                // Refracted background in SCREEN SPACE.
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
                float2 texel = _ScreenSize.zw;
                float3 bg = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv + float2(texel.x, 0) * blur).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv - float2(texel.x, 0) * blur).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv + float2(0, texel.y) * blur).rgb;
                bg += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrUv - float2(0, texel.y) * blur).rgb;
                bg /= 5.0;

                // What you see through the ice: distorted, blurred background,
                // tinted by the light passing through the ice.
                float3 seen = bg * lerp(1.0, lightingMul, 0.6) + envRefr * lerp(0.45, 0.15, thickness) * sceneLightLevel;

                // Fogging: thin parts clear, thick core dissolves into deep
                // ice — fog colour is lit by the room lights too.
                float3 fogColor = (_DeepColor.rgb * 1.7 + float3(0.05, 0.12, 0.22)) * lightingMul + envRefl * 0.08 * sceneLightLevel;
                float fog = smoothstep(0.35, 0.85, thickness) * 0.93;
                float3 through = lerp(seen, fogColor, fog);

                float3 body = lerp(_IceColor.rgb, _DeepColor.rgb, thickness * 0.85) * lerp(0.22, 0.55, thickness) * lightingMul * 2.2;

                // Iridescence is a LIGHT-DRIVEN effect: no light = no sheen.
                float shimmerPhase = IN.positionWS.y * 1.2 + density * 4.0 + t * 0.35;
                float3 shimmer = iridescentColor(shimmerPhase) * _IridescenceStrength * (0.18 + fresnel) * sceneLightLevel;

                float spark = internalSparkles(IN.positionWS, viewDir) * _SparkleIntensity * sceneLightLevel;

                float3 color = through
                             + body
                             + specular * (0.30 + 0.70 * fresnel)
                             + envRefl * (0.30 + 0.70 * fresnel) * _ReflectionStrength * sceneLightLevel
                             + shimmer
                             + mainLightColor * spark * 1.6
                             + directLight * spark * 1.2
                             + float3(0.40, 0.70, 1.00) * fresnel * (sceneLightLevel * 0.9);

                float alpha = smoothstep(0.30, 0.80, thickness) * 0.82 + 0.15;
                alpha = max(alpha, spark * 0.85);

                color = MixFog(color, IN.fogFactor);

                return half4(color, alpha);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
