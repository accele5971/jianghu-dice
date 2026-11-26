// 角色用的受光著色器：純色、收陰影、投陰影，並在模板緩衝寫入 1。
// 寫入模板是為了讓「透視剪影」知道哪些像素已經畫出角色本體，不要再蓋一層。
// 同樣套用戰爭迷霧（見 Terrain.shader 的說明）。
Shader "Jianghu/UnitLit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Flash ("Flash", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
        }

        CGPROGRAM
        #pragma surface surf Lambert addshadow fullforwardshadows finalcolor:fogOfWar

        fixed4 _Color;
        fixed _Flash;
        sampler2D _JhFog;
        float4 _JhFogSize;
        fixed4 _JhMist;
        float _JhFogOn;

        struct Input
        {
            float3 worldPos;
            fixed4 color : COLOR; // 合併後的角色零件把各自的顏色寫在頂點上；沒有頂點色的網格會得到白色
        };

        void surf (Input IN, inout SurfaceOutput o)
        {
            o.Albedo = lerp(_Color.rgb * IN.color.rgb, fixed3(1, 1, 1), _Flash); // _Flash：受擊閃白
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
    FallBack "Legacy Shaders/VertexLit"
}
