// 圖示用：不受光、不受迷霧影響，直接輸出頂點色（明暗已經烘在頂點色裡）。
// 用來把裝備與道具的模型畫成面板上的小圖示。
Shader "Jianghu/Icon"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite On
            Cull Off
            Fog { Mode Off }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 col : COLOR;
            };

            v2f vert (float4 vertex : POSITION, fixed4 color : COLOR)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.col = color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(i.col.rgb, 1);
            }
            ENDCG
        }
    }
}
