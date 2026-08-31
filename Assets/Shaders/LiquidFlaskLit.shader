Shader "Custom/LiquidFlaskLit"
{
    Properties
    {
        _FillAmount     ("Fill Amount",         Range(0, 1))    = 0.5
        _LocalMeshMin   ("Mesh Min Y",          Float)          = -0.5
        _LocalMeshMax   ("Mesh Max Y",          Float)          = 0.5

        _LiquidColor    ("Liquid Color",        Color)          = (0.1, 0.5, 0.9, 1)
        _SurfaceColor   ("Surface Color",       Color)          = (0.3, 0.7, 1.0, 1)
        _FoamWidth      ("Foam Width",          Range(0, 0.5))  = 0.02
        _EmissionColor  ("Emission Color",      Color)          = (0, 0, 0, 1)
        _EmissionPower  ("Emission Power",      Range(0, 10))   = 0.0
        _Turbidity      ("Turbidity",           Range(0, 1))    = 0.0
        _NoiseScale     ("Noise Scale",         Range(0.1, 10)) = 1.0
        _NoiseSpeed     ("Noise Speed",         Range(0, 5))    = 0.5

        // Transparency & Refraction
        _Opacity            ("Opacity",             Range(0, 1))    = 0.82
        _RefractionStrength ("Refraction Strength", Range(0, 0.2))  = 0.03
        _ChromaticAberration("Chromatic Aberration",Range(0, 0.02)) = 0.004

        // Distortion & Lens
        _DistortionStrength ("Distortion Strength", Range(0, 0.3))  = 0.08
        _DistortionSpeed    ("Distortion Speed",    Range(0, 5))    = 1.0
        _LensStrength       ("Lens Strength",       Range(0, 1))    = 0.15
        _LensPower          ("Lens Power",          Range(0, 3))    = 1.0

        // Depth shading
        _DepthDarken    ("Depth Darken",        Range(0, 1))    = 0.5

        // Lighting floor — minimum brightness in darkness.
        // 0.15 = liquid dimly visible in complete darkness (flasks, inventory preview).
        // 0 = liquid fully black without light (sinks, baths in dark rooms).
        _MinLightFloor  ("Min Light Floor",     Range(0, 1))    = 0.15

        _PivotWS   ("Pivot World Space", Vector) = (0,0,0,0)
        _WobbleX   ("WobbleX",           Float)  = 0.0
        _WobbleZ   ("WobbleZ",           Float)  = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent-100"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "DisableBatching" = "True"
        }

        // ─── Main Forward Pass ────────────────────────────────────────────────
        Pass
        {
            Name "LiquidForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull    Off
            ZWrite  Off
            Blend   One Zero

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.5

            #pragma multi_compile_fog
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Opaque scene colour — captured by URP before the transparent pass.
            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                float  _FillAmount;
                float  _LocalMeshMin;
                float  _LocalMeshMax;
                float4 _PivotWS;
                float4 _LiquidColor;
                float4 _SurfaceColor;
                float  _FoamWidth;
                float4 _EmissionColor;
                float  _EmissionPower;
                float  _Turbidity;
                float  _NoiseScale;
                float  _NoiseSpeed;
                float  _WobbleX;
                float  _WobbleZ;
                float  _Opacity;
                float  _RefractionStrength;
                float  _ChromaticAberration;
                float  _DistortionStrength;
                float  _DistortionSpeed;
                float  _LensStrength;
                float  _LensPower;
                float  _DepthDarken;
                float  _MinLightFloor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionWS : TEXCOORD0; // xyz = world pos, w = local Y (mesh space)
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
            };

            // ── Value noise ───────────────────────────────────────────────────
            float Hash(float n) { return frac(sin(n) * 43758.5453123); }

            float Noise(float3 x)
            {
                float3 p = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n = p.x + p.y * 57.0 + p.z * 113.0;
                return lerp(
                    lerp(lerp(Hash(n),         Hash(n +   1.0), f.x),
                         lerp(Hash(n +  57.0), Hash(n +  58.0), f.x), f.y),
                    lerp(lerp(Hash(n + 113.0), Hash(n + 114.0), f.x),
                         lerp(Hash(n + 170.0), Hash(n + 171.0), f.x), f.y), f.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS  = float4(worldPos, IN.positionOS.y);
                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fogFactor   = ComputeFogFactor(OUT.positionCS.z);
                OUT.screenPos   = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                // ── Liquid surface clip ───────────────────────────────────────
                float localYForClip   = lerp(_LocalMeshMin, _LocalMeshMax, _FillAmount);
                float3 surfacePivotWS = TransformObjectToWorld(float3(0, localYForClip, 0));

                float3 relPos         = IN.positionWS.xyz - _PivotWS.xyz;
                float  wobble         = relPos.x * _WobbleX + relPos.z * _WobbleZ;
                float  surfaceHeightWS = surfacePivotWS.y + wobble;

                float clipVal = surfaceHeightWS - IN.positionWS.y;
                float bias    = (_FillAmount <= 0.001) ? -0.1
                              : (_FillAmount >= 0.999) ?  0.1
                              : 0.005;
                clip(clipVal + bias);

                // ── Depth-based darkening (Beer–Lambert, tilt-aware) ──────────
                float3 meshBotWS   = TransformObjectToWorld(float3(0, _LocalMeshMin, 0));
                float  maxDepthWS  = surfacePivotWS.y - meshBotWS.y;
                float  maxDistWS   = distance(surfacePivotWS, meshBotWS);
                float  uprightness = saturate(maxDepthWS / max(maxDistWS, 0.001));
                float  depthRatio  = saturate(clipVal / max(maxDepthWS, 0.001));
                float  depthFactor = lerp(1.0, 1.0 - _DepthDarken * (depthRatio * depthRatio),
                                         uprightness);

                // ── Liquid colour + turbidity ─────────────────────────────────
                float  noiseVal  = Noise(IN.positionWS.xyz * _NoiseScale
                                        + float3(0, _Time.y * _NoiseSpeed, 0));
                float3 liquidCol = lerp(_LiquidColor.rgb,
                                        _LiquidColor.rgb * noiseVal,
                                        _Turbidity);

                bool isMeshLid = IN.positionWS.w > (_LocalMeshMax - 0.001);

                float3 finalColor;
                if (facing < 0)
                {
                    // Inner (back) faces → liquid surface plane.
                    finalColor = _SurfaceColor.rgb;
                }
                else
                {
                    // Outer (front) faces
                    float foam = 1.0 - smoothstep(0.0, _FoamWidth, clipVal);
                    finalColor = lerp(liquidCol, _SurfaceColor.rgb, foam);
                    if (isMeshLid) finalColor = _SurfaceColor.rgb;

                    // Fresnel rim
                    float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS.xyz));
                    float  rim       = 1.0 - saturate(dot(normalize(IN.normalWS), viewDirWS));
                    finalColor += pow(rim, 4.0) * _SurfaceColor.rgb * 0.3;
                }

                // Emission is additive light — not subject to depth darkening.
                finalColor += _EmissionColor.rgb * _EmissionPower;

                // ── URP Lighting (Forward+ / Light Layers) ─────────────────────
                // Full URP-compatible lighting loop:
                //   • _CLUSTER_LIGHT_LOOP  — enables Forward+ cluster iteration
                //   • _LIGHT_LAYERS        — filters lights by rendering layer mask
                // Without these keywords the shader compiled a legacy variant
                // where GetAdditionalLightsCount() returned 0 in Forward+ mode,
                // leaving the liquid pitch-black in indoor rooms.
                {
                    float3 normalWS = normalize(IN.normalWS);
                    if (facing < 0) normalWS = -normalWS;

                    // Minimal InputData for the LIGHT_LOOP_BEGIN cluster macro.
                    InputData inputData = (InputData)0;
                    inputData.positionWS              = IN.positionWS.xyz;
                    inputData.positionCS              = IN.positionCS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                    uint meshRenderingLayers = GetMeshRenderingLayer();
                    half4 shadowMask = half4(1, 1, 1, 1);

                    float3 directLighting = 0;

                    // ── Main directional light ──
                    Light mainLight = GetMainLight();
                #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
                #endif
                    {
                        float NdotL = dot(normalWS, mainLight.direction) * 0.5 + 0.5;
                        directLighting = mainLight.color * NdotL;
                    }

                    // ── Additional point/spot lights ──
                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();

                    // Forward+ directional additional lights (stored first in buffer).
                #if USE_CLUSTER_LIGHT_LOOP
                    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK

                        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS);
                    #ifdef _LIGHT_LAYERS
                        if (IsMatchingLightLayer(addLight.layerMask, meshRenderingLayers))
                    #endif
                        {
                            float addNdotL = dot(normalWS, addLight.direction) * 0.5 + 0.5;
                            directLighting += addLight.color * addLight.distanceAttenuation * addNdotL;
                        }
                    }
                #endif

                    // Per-pixel / cluster light loop.
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS);
                    #ifdef _LIGHT_LAYERS
                        if (IsMatchingLightLayer(addLight.layerMask, meshRenderingLayers))
                    #endif
                        {
                            float addNdotL = dot(normalWS, addLight.direction) * 0.5 + 0.5;
                            directLighting += addLight.color * addLight.distanceAttenuation * addNdotL;
                        }
                    LIGHT_LOOP_END
                #endif

                    // Ambient from spherical harmonics
                    float3 ambient = SampleSH(normalWS);

                    float3 lighting = directLighting + ambient;

                    // Soft floor: liquid is dim in darkness, full in light.
                    float lightIntensity = max(max(lighting.r, lighting.g), lighting.b);
                    float dimFactor = max(lightIntensity, _MinLightFloor);
                    finalColor *= dimFactor;
                }

                // ── Enhanced Refraction / Distortion / Lens ─────────────────
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalize(IN.normalWS));

                float3 noisePos = IN.positionWS.xyz * _NoiseScale * 2.5;
                float  distT    = _Time.y * _DistortionSpeed;

                float noiseX = (Noise(noisePos + float3(0,    distT * 0.35, 3.7)) * 2.0 - 1.0) * 0.5
                             + (Noise(noisePos * 2.1 + float3(5.2,  distT * 0.5,  1.9)) * 2.0 - 1.0) * 0.3
                             + (Noise(noisePos * 4.3 + float3(2.8,  distT * 0.7,  6.4)) * 2.0 - 1.0) * 0.2;

                float noiseY = (Noise(noisePos + float3(1.3,  distT * 0.35, 8.1)) * 2.0 - 1.0) * 0.5
                             + (Noise(noisePos * 2.1 + float3(7.1,  distT * 0.5,  4.3)) * 2.0 - 1.0) * 0.3
                             + (Noise(noisePos * 4.3 + float3(3.5,  distT * 0.7,  9.2)) * 2.0 - 1.0) * 0.2;

                float lensFactor = pow(depthRatio, _LensPower);

                float2 refractOffset =  normalVS.xy                * _RefractionStrength
                                      + float2(noiseX, noiseY)     * _DistortionStrength
                                      + float2(noiseX, noiseY)     * _DistortionStrength * lensFactor * 0.5;

                float2 toCenter = screenUV - 0.5;
                float2 lensUV   = screenUV - toCenter * lensFactor * _LensStrength;

                float2 refractUV = clamp(lensUV + refractOffset, 0.001, 0.999);

                float  ca = _ChromaticAberration;
                half r = SAMPLE_TEXTURE2D(_CameraOpaqueTexture,
                            sampler_CameraOpaqueTexture,
                            clamp(refractUV + float2( ca, 0), 0.001, 0.999)).r;
                half g = SAMPLE_TEXTURE2D(_CameraOpaqueTexture,
                            sampler_CameraOpaqueTexture, refractUV).g;
                half b = SAMPLE_TEXTURE2D(_CameraOpaqueTexture,
                            sampler_CameraOpaqueTexture,
                            clamp(refractUV + float2(-ca, 0), 0.001, 0.999)).b;
                half3 bgColor = half3(r, g, b);

                half3 tintedBg = bgColor * lerp(half3(1, 1, 1),
                                                _LiquidColor.rgb * 1.4,
                                                _Opacity * 0.4);

                half3 outColor = lerp(tintedBg, finalColor, _Opacity);

                outColor *= depthFactor;

                return half4(MixFog(outColor, IN.fogFactor), 1.0);
            }
            ENDHLSL
        }

        // ─── Shadow Caster ────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite  On
            ZTest   LEqual
            Cull    Off

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _FillAmount;
                float  _LocalMeshMin;
                float  _LocalMeshMax;
                float4 _PivotWS;
                float  _WobbleX;
                float  _WobbleZ;
                float  _Opacity;
                float  _RefractionStrength;
                float  _ChromaticAberration;
                float  _DistortionStrength;
                float  _DistortionSpeed;
                float  _LensStrength;
                float  _LensPower;
                float  _DepthDarken;
                float  _MinLightFloor;
            CBUFFER_END

            struct ShadowAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct ShadowVary { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            ShadowVary shadowVert(ShadowAttr IN)
            {
                ShadowVary OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS  = worldPos;
                float3 posWS    = ApplyShadowBias(worldPos,
                                      TransformObjectToWorldNormal(IN.normalOS),
                                      _MainLightPosition.xyz);
                OUT.positionCS  = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 shadowFrag(ShadowVary IN) : SV_Target
            {
                float localY          = lerp(_LocalMeshMin, _LocalMeshMax, _FillAmount);
                float3 surfacePivotWS = TransformObjectToWorld(float3(0, localY, 0));
                float3 relPos         = IN.positionWS - _PivotWS.xyz;
                float  wobble         = relPos.x * _WobbleX + relPos.z * _WobbleZ;

                float clipVal = surfacePivotWS.y + wobble - IN.positionWS.y;
                float bias    = (_FillAmount <= 0.001) ? -0.1
                              : (_FillAmount >= 0.999) ?  0.1
                              : 0.005;
                clip(clipVal + bias);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
