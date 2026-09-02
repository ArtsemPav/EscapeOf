Shader "Custom/LiquidBath"
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

        _Opacity            ("Opacity",             Range(0, 1))    = 0.82
        _RefractionStrength ("Refraction Strength", Range(0, 0.2))  = 0.03
        _ChromaticAberration("Chromatic Aberration",Range(0, 0.02)) = 0.004

        _DistortionStrength ("Distortion Strength", Range(0, 0.3))  = 0.08
        _DistortionSpeed    ("Distortion Speed",    Range(0, 5))    = 1.0
        _LensStrength       ("Lens Strength",       Range(0, 1))    = 0.15
        _LensPower          ("Lens Power",          Range(0, 3))    = 1.0

        _DepthDarken    ("Depth Darken",        Range(0, 1))    = 0.5
        _BlurStrength   ("Blur Strength",       Range(0, 0.05)) = 0.03
        _MinLightFloor  ("Min Light Floor",     Range(0, 1))    = 0.15

        _CapOpacity     ("Cap Opacity",         Range(0, 1))    = 0.85
        _CapDistortion  ("Cap Distortion Boost",Range(1, 5))   = 1.0

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

        Pass
        {
            Name "LiquidForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull   Off
            ZWrite Off
            Blend  One Zero

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
                float  _BlurStrength;
                float  _CapOpacity;
                float  _CapDistortion;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
            };

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

            // Blur helper: 17-tap two-ring blur of _CameraOpaqueTexture.
            half3 BlurOpaque(float2 uv, float radius)
            {
                float far = radius * 2.5;
                half3 s = 0;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(radius, 0), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(-radius, 0), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(0, radius), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(0, -radius), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(radius * 0.7, radius * 0.7), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(-radius * 0.7, radius * 0.7), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(radius * 0.7, -radius * 0.7), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(-radius * 0.7, -radius * 0.7), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(far, 0), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(-far, 0), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(0, far), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(0, -far), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(far * 0.7, far * 0.7), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(-far * 0.7, far * 0.7), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(far * 0.7, -far * 0.7), 0.001, 0.999)).rgb;
                s += SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, clamp(uv + float2(-far * 0.7, -far * 0.7), 0.001, 0.999)).rgb;
                return s / 17.0;
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

                float3 normalWS = normalize(IN.normalWS);

                // Skip rendering when nearly empty.
                if (_FillAmount < 0.005)
                    discard;

                // Determine which faces to render.
                // The mesh is scaled by Y from the script (transform.localScale.y
                // = baseScaleY * fillFraction), so the top face IS at the water
                // surface level.  We render only horizontal upward faces (the cap)
                // and discard everything else — sides, bottom, internal.
                bool isHorizontalUp = normalWS.y > 0.15;

                if (!isHorizontalUp)
                    discard;

                // ── Shared depth calculations ─────────────────────────────────
                float3 meshBotWS   = TransformObjectToWorld(float3(0, _LocalMeshMin, 0));
                float  maxDepthWS  = surfacePivotWS.y - meshBotWS.y;
                float  maxDistWS   = distance(surfacePivotWS, meshBotWS);
                float  uprightness = saturate(maxDepthWS / max(maxDistWS, 0.001));
                float  depthRatio  = saturate(clipVal / max(maxDepthWS, 0.001));
                float  depthFactor = lerp(1.0, 1.0 - _DepthDarken * (depthRatio * depthRatio),
                                         uprightness);

                // ── Screen-space UV ───────────────────────────────────────────
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalize(IN.normalWS));

                // ── URP Lighting (Forward+ / Light Layers) ─────────────────────
                float3 lightNormalWS = normalWS;
                if (facing < 0) lightNormalWS = -lightNormalWS;

                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.positionWS.xyz;
                inputData.positionCS              = IN.positionCS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                uint meshRenderingLayers = GetMeshRenderingLayer();
                float3 directLighting = 0;

                Light mainLight = GetMainLight();
            #ifdef _LIGHT_LAYERS
                if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
            #endif
                {
                    float NdotL = dot(lightNormalWS, mainLight.direction) * 0.5 + 0.5;
                    directLighting = mainLight.color * NdotL;
                }

            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();

            #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS);
                #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(addLight.layerMask, meshRenderingLayers))
                #endif
                    {
                        float addNdotL = dot(lightNormalWS, addLight.direction) * 0.5 + 0.5;
                        directLighting += addLight.color * addLight.distanceAttenuation * addNdotL;
                    }
                }
            #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, inputData.positionWS);
                #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(addLight.layerMask, meshRenderingLayers))
                #endif
                    {
                        float addNdotL = dot(lightNormalWS, addLight.direction) * 0.5 + 0.5;
                        directLighting += addLight.color * addLight.distanceAttenuation * addNdotL;
                    }
                LIGHT_LOOP_END
            #endif

                float3 ambient = SampleSH(lightNormalWS);
                float3 lighting = directLighting + ambient;
                float lightIntensity = max(max(lighting.r, lighting.g), lighting.b);
                float dimFactor = max(lightIntensity, _MinLightFloor);

                // ═══════════════════════════════════════════════════════════════
                // MURKY CAP — top horizontal face
                // The mesh is Y-scaled by the script so this face sits at the
                // water surface level.  It renders as a hazy, blurred, distorted
                // layer that obscures items while showing animated water ripples.
                // ═══════════════════════════════════════════════════════════════
                {
                    float3 capNoisePos = IN.positionWS.xyz * _NoiseScale * 0.7;
                    float  capT        = _Time.y * _DistortionSpeed * 0.15;

                    float capNX = (Noise(capNoisePos + float3(0, capT, 5))        * 2.0 - 1.0) * 0.5
                                + (Noise(capNoisePos * 2 + float3(4, capT * 0.7, 2)) * 2.0 - 1.0) * 0.3
                                + (Noise(capNoisePos * 4 + float3(8, capT * 0.5, 1)) * 2.0 - 1.0) * 0.2;

                    float capNY = (Noise(capNoisePos + float3(3, capT, 1))        * 2.0 - 1.0) * 0.5
                                + (Noise(capNoisePos * 2 + float3(7, capT * 0.7, 6)) * 2.0 - 1.0) * 0.3
                                + (Noise(capNoisePos * 4 + float3(2, capT * 0.5, 9)) * 2.0 - 1.0) * 0.2;

                    // Distortion offset — item is sampled from a shifted position.
                    float2 capRefract = float2(capNX, capNY) * _DistortionStrength * 2.0;
                    float2 capUV = clamp(screenUV + capRefract, 0.001, 0.999);

                    // Heavy blur — item is hazy and unrecognizable.
                    float capBlur = _BlurStrength * 1.5;
                    half3 capBg = BlurOpaque(capUV, capBlur);

                    // Tint the blurred background with liquid colour.
                    float capDepthFog = _Turbidity * 2.0;
                    half3 capTinted = capBg * lerp(half3(1, 1, 1), _LiquidColor.rgb * 1.2, saturate(capDepthFog * 0.5));

                    // Animated water surface.
                    float capRipple = capNX * 0.5 + capNY * 0.5;
                    half3 capSurface = _SurfaceColor.rgb * (0.75 + capRipple * 0.25);
                    float capSpec = pow(saturate(capRipple * 1.5 - 0.5), 3.0);
                    capSurface += _SurfaceColor.rgb * capSpec * 0.4;

                    float3 capViewDir = normalize(GetWorldSpaceViewDir(IN.positionWS.xyz));
                    float capFresnel = 1.0 - saturate(dot(normalWS, capViewDir));
                    capSurface += _SurfaceColor.rgb * pow(capFresnel, 3.0) * 0.3;

                    float capWaterline = 1.0 - smoothstep(0.0, _FoamWidth * 3.0, abs(clipVal));
                    capSurface = lerp(capSurface, _SurfaceColor.rgb * 1.5, capWaterline * 0.6);

                    // Blend: distorted blurred item → water surface.
                    half3 capColor = lerp(capTinted, capSurface, _Opacity * 0.6);
                    capColor *= dimFactor;

                    return half4(MixFog(capColor, IN.fogFactor), 1.0);
                }
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
            Cull    Back

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
                float  _BlurStrength;
                float  _CapOpacity;
                float  _CapDistortion;
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
                if (_FillAmount < 0.005)
                    discard;
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
