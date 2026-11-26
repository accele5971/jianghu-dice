// 地形與場景物件用的受光著色器：調色盤貼圖 × 頂點色，並套用戰爭迷霧。
// 迷霧資料是一張「每格一個像素」的貼圖（_JhFog）：R = 此刻看得到、G = 曾經看過。
//   沒看過      → 整片霧色
//   看過但看不到 → 變暗、褪色（陰影層）
//   看得到      → 原色
Shader "Jianghu/Terrain"
{
    Properties
    {
        _MainTex ("Palette", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert addshadow fullforwardshadows finalcolor:fogOfWar

        sampler2D _MainTex;
        sampler2D _JhFog;
        float4 _JhFogSize;
        fixed4 _JhMist;
        float _JhFogOn;

        struct Input
        {
            float2 uv_MainTex;
            fixed4 color : COLOR;
            float3 worldPos;
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * IN.color.rgb;
            o.Alpha = 1;
        }

        void fogOfWar (Input IN, SurfaceOutput o, inout fixed4 color)
        {
            if (_JhFogOn > 0.5)
            {
                fixed2 f = tex2D(_JhFog, IN.worldPos.xz / _JhFogSize.xy).rg;
                fixed grey = dot(color.rgb, fixed3(0.30, 0.59, 0.11));
                fixed3 shade = lerp(fixed3(grey, grey, grey), color.rgb, 0.35) * 0.52;
                color.rgb = lerp(_JhMist.rgb, lerp(shade, color.rgb, f.r), f.g);
            }
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Diffuse"
}
