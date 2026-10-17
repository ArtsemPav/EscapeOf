// "Waking up" eyelid overlay for the WakeUpIntroController.
// Renders two EYELIDS as the area OUTSIDE an elliptical "eye opening":
// the lids hang from the top and bottom edges of the screen and their
// facing edges are CONCAVE arcs (an ellipse cut out of the screen center),
// so at eyeOpen = 1 the opening covers the screen and at 0 the lids close
// over it like natural eyelids — no "hourglass" pinch.
Shader "Custom/EyelidOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _EyeOpen ("Eye Open (0 closed, 1 open)", Range(0, 1)) = 0
        _Curve ("Lid Curve (0 straight, 1 deep arc)", Range(0, 1)) = 0.6
        _Softness ("Edge Softness (screen fraction)", Range(0.01, 0.3)) = 0.08
        _Tint ("Sleep Tint", Color) = (0.02, 0.02, 0.04, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _EyeOpen;
            float _Curve;
            float _Softness;
            fixed4 _Tint;
            sampler2D _MainTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv; // 0..1 across the quad
                return o;
            }

            // Signed distance of the ellipse boundary, in "edge units":
            // negative inside the opening, positive outside (on the lids).
            // Simplest proxy: normalized elliptical radius r(uv) —
            //   r < 1 → inside the eye opening, r > 1 → lid.
            // r = sqrt((x/rx)^2 + (y/ry)^2) with the half-axes driven by _EyeOpen.
            fixed4 frag (v2f i) : SV_Target
            {
                // uGUI compatibility: sample (unused) main texture like the
                // built-in UI shaders do, so Image doesn't warn about _MainTex.
                fixed4 mainTex = tex2D(_MainTex, i.uv);

                // Screen UV of the quad: 0..1 both axes.
                float2 p = i.uv * 2.0 - 1.0; // -1..1, center 0

                // Half-extents of the eye opening: the opening grows from a
                // slit (both lids meeting at the center) to beyond the screen.
                // Vertical opening: 0 = closed (slit at center) … 1 = fully open.
                float open = saturate(_EyeOpen);
                // Curve pushes the lids' facing edges toward the screen edges
                // at the sides (concave from the lid's point of view).
                float sideRelief = _Curve * (1.0 - abs(p.x)); // 1 at center, 0 at sides

                float ry = lerp(0.0, 1.15, open) + sideRelief * 0.2 * open;
                float rx = 0.95; // half-width in UV space — the arcs curve visibly at the sides

                float2 scaled = float2(p.x / rx, p.y / ry);
                float r = length(scaled);

                // Edge softness in r units: a WIDE gradient along the lid arc —
                // the lids read as blurred, out-of-focus edges, not hard masks.
                float soft = _Softness * 2.5 + 0.03;

                // Lid alpha: 1 outside the opening, 0 inside, soft edge between.
                float lidAlpha = saturate((r - 1.0) / soft);

                // Total overlay = lid black + sleep tint underneath.
                // The lids are pure black; the tint darkens the "seen through
                // the lashes" region slightly (alpha follows closedness).
                fixed4 col;
                col.rgb = 0.0;
                col.a = lidAlpha * mainTex.a;

                // Sleep tint appears over the whole screen when mostly closed.
                float sleep = (1.0 - open);
                col.rgb = lerp(col.rgb, _Tint.rgb, _Tint.a * sleep * 0.5);
                col.a = saturate(col.a + _Tint.a * sleep * (1.0 - lidAlpha));

                // Smooth fade-out as the eyes finish opening: the ellipse never
                // fully covers the screen corners, so at eyeOpen = 1 the canvas
                // disable would pop the last lid slivers off in a single frame.
                // Reaching zero alpha BEFORE the canvas turns off makes the
                // handoff invisible (and softens the blink re-entry too).
                col.a *= 1.0 - smoothstep(0.85, 1.0, open);

                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
