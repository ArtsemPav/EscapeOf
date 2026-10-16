Shader "EscapeOf/SpiderWebOverlayDistortion"
{
    Properties
    {
        _MainTex ("Web Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SwayStrength ("Sway Strength", Float) = 0.01
        _SwaySpeed ("Sway Speed", Float) = 2.0
        _RippleStrength ("Ripple Strength", Float) = 0.02
        _RippleScale ("Ripple Scale", Float) = 12.0
        _AlphaBoost ("Alpha Boost", Float) = 1.0
        _MipLevel ("Sampling Mip Level", Range(0, 6)) = 0.0
        _EdgeFade ("Edge Fade", Range(0, 0.5)) = 0.15
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 centered : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            fixed4 _Color;
            float _SwayStrength;
            float _SwaySpeed;
            float _RippleStrength;
            float _RippleScale;
            float _AlphaBoost;
            float _MipLevel;
            float _EdgeFade;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                // -1..1 across the full-screen quad, used for the radial ripple/mask
                o.centered = v.texcoord * 2.0 - 1.0;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.texcoord;

                // Radial ripple radiating from the screen center
                float dist = length(i.centered);
                float ripple = sin(dist * _RippleScale - _Time.y * 4.0) * _RippleStrength;

                // Slow wandering of the whole web
                float t = _Time.y * _SwaySpeed;
                float2 sway = float2(sin(t + uv.y * 3.0), cos(t * 0.8 + uv.x * 3.0)) * _SwayStrength;

                uv += ripple * i.centered + sway;

                // Sample at a fixed mip level: the ripple/sway UV displacement
                // breaks the GPU's mip derivation and makes thin strands alias
                // into dashes, so we never rely on automatic gradients here.
                // The texture has clean alpha: white strands on transparent.
                float4 tex = tex2Dlod(_MainTex, float4(uv, 0, _MipLevel));
                float webAlpha = tex.a * _AlphaBoost;

                // Soft mask so the web fades out before the screen edges
                float edge = max(abs(i.centered.x), abs(i.centered.y));
                float mask = 1.0 - smoothstep(1.0 - _EdgeFade * 2.0, 1.0, edge);

                fixed4 col = i.color;
                // Draw strands as clean bright lines
                col.rgb = lerp(col.rgb, float3(1, 1, 1), saturate(webAlpha));
                col.a *= saturate(webAlpha) * mask;
                return col;
            }
            ENDCG
        }
    }
    Fallback "UI/Default"
}
