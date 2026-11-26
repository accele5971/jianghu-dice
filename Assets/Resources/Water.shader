// 水面：不受光的半透明色，套用戰爭迷霧（沒看過的水面也要隱在霧裡）。
Shader "Jianghu/Water"
{
    Properties
    {
        _Color ("Color", Color) = (0.28, 0.52, 0.53, 0.72)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            sampler2D _JhFog;
            float4 _JhFogSize;
            fixed4 _JhMist;
            float _JhFogOn;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
            };

            v2f vert (float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.world = mul(unity_ObjectToWorld, vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = _Color;
                if (_JhFogOn > 0.5)
                {
                    fixed2 f = tex2D(_JhFog, i.world.xz / _JhFogSize.xy).rg;
                    fixed grey = dot(c.rgb, fixed3(0.30, 0.59, 0.11));
                    fixed3 shade = lerp(fixed3(grey, grey, grey), c.rgb, 0.35) * 0.52;
                    c.rgb = lerp(_JhMist.rgb, lerp(shade, c.rgb, f.r), f.g);
                    c.a = lerp(1.0, c.a, f.g); // 沒看過的地方完全不透明，看不到水底
                }
                return c;
            }
            ENDCG
        }
    }
}
