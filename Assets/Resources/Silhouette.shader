// 透視剪影：角色被地形擋住的部分，以半透明的陣營色畫出來。
// ZTest Greater：只畫「前面有東西擋著」的像素。
// Stencil NotEqual 1：角色本體看得到的像素不畫，避免身體各部位互相疊色。
Shader "Jianghu/Silhouette"
{
    Properties
    {
        _Color ("Color", Color) = (0.3,0.6,1,0.5)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+20" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

            float4 vert (float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag () : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
