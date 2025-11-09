Shader "Custom/WireframeTransparent"
{
    Properties
    {
        _LineColor("Line Color", Color) = (0,1,0,1)
        _Thickness("Thickness", Range(0.001, 0.5)) = 0.02
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 bary : TEXCOORD1; // 从 UV1 读取重心坐标
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0;
            };

            fixed4 _LineColor;
            float _Thickness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.bary = v.bary;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float minBary = min(min(i.bary.x, i.bary.y), i.bary.z);

                if (minBary < _Thickness)
                    return _LineColor;   // 线框

                return fixed4(0,0,0,0);  // 透明
            }
            ENDCG
        }
    }
}
