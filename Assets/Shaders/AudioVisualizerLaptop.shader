Shader "Custom/AudioVisualizer"
{
    Properties
    {
        _Color("Base Color", Color) = (0,1,0,1)
        _ColorTop("Top Color", Color) = (1,0,0,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float4 _Color;
            float4 _ColorTop;
            float _Bands[8]; // Array of 8 bands

            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                // Calculate index and fractional part for gaps between bars
                float rawX = i.uv.x * 8;
                int index = floor(rawX);
                float fracX = frac(rawX);

                // Create a gap (10% on each side of the bar)
                if (fracX > 0.1 && fracX < 0.9) {
                    float bandValue = _Bands[index];
                    if (i.uv.y < bandValue) {
                        // Gradient from bottom (_Color) to top (_ColorTop)
                        return lerp(_Color, _ColorTop, i.uv.y);
                    }
                }
                return fixed4(0,0,0,0);
            }
            ENDCG
        }
    }
}
