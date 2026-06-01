Shader "Custom/LiquidFlask"
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
        _RefractionStrength ("Refraction Strength", Range(0, 0.08)) = 0.03
        _ChromaticAberration("Chromatic Aberration",Range(0, 0.02)) = 0.004

        // Depth shading
        _DepthDarken    ("Depth Darken",        Range(0, 1))    = 0.5

        [HideInInspector] _PivotWS   ("Pivot World Space", Vector) = (0,0,0,0)
        [HideInInspector] _WobbleX   ("WobbleX",           Float)  = 0.0
        [HideInInspector] _WobbleZ   ("WobbleZ",           Float)  = 0.0
    }

    SubShader
    {
        Tags
        {
            // Queue 2900: safely inside transparent pass (URP opaque = 0-2500, transparent = 2501+).
            // _CameraOpaqueTexture is correctly captured before this renders.
            // Renders before glass mesh (typically Queue = 3000).
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
            // Final colour is composited manually with _CameraOpaqueTexture in HLSL.
            Blend   One Zero

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog

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
                float  _DepthDarken;
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
                // maxDepthWS  — vertical (world-Y) extent of the liquid column.
                // maxDistWS   — 3D length of the column (rotation-invariant).
                // uprightness — ratio: 1 when flask is upright, 0 when fully sideways.
                //   Smoothly disables the effect as the flask tilts, preventing the
                //   sharp bright-line artefact that appears when maxDepthWS → 0.
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

                // ── Refraction / Transparency ─────────────────────────────────
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

                // View-space normal XY → direction of screen-space refraction.
                // Stable under object rotation (XY in view space = screen X/Y).
                float3 normalVS = mul((float3x3)UNITY_MATRIX_V, normalize(IN.normalWS));

                // Two independent noise octaves for X and Y → 2D distortion visible
                // even at the flask centre where normalVS.xy ≈ 0.
                float noiseX = Noise(IN.positionWS.xyz * _NoiseScale * 2.5
                                     + float3(0,   _Time.y * _NoiseSpeed * 0.35, 3.7)) * 2.0 - 1.0;
                float noiseY = Noise(IN.positionWS.xyz * _NoiseScale * 2.5
                                     + float3(1.3, _Time.y * _NoiseSpeed * 0.35, 8.1)) * 2.0 - 1.0;

                // Normal-based refraction (edge distortion) + noise base (centre distortion).
                float2 refractOffset =  normalVS.xy                      * _RefractionStrength
                                      + float2(noiseX, noiseY)           * _RefractionStrength * 0.6;

                float2 refractUV = clamp(screenUV + refractOffset, 0.001, 0.999);

                // Chromatic aberration: R/G/B sampled with slight horizontal splits.
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

                // Tint background with liquid colour, scaled by opacity so at opacity=0
                // the background shows without any tinting.
                half3 tintedBg = bgColor * lerp(half3(1, 1, 1),
                                                _LiquidColor.rgb * 1.4,
                                                _Opacity * 0.4);

                // Blend: distorted-tinted background → liquid surface.
                // At _Opacity=0 → only refracted background.
                // At _Opacity=1 → only liquid surface colour.
                half3 outColor = lerp(tintedBg, finalColor, _Opacity);

                // Depth darkening applied to the full composite result (liquid + refracted bg).
                // Physically: deeper liquid column absorbs more light — darkens everything,
                // including the background visible through the transparent liquid.
                // This prevents the background's 18% contribution from washing out the effect.
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
                float  _DepthDarken;
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
}
