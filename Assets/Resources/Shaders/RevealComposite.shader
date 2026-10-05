// Draws the lit painting stack (rendered by the lit camera) over the gloom stack, using the
// light map as the blend amount: lerp(gloom, lit, light).
Shader "Lamplighter/RevealComposite"
{
    Properties
    {
        _LitTex ("Lit world", 2D) = "black" {}
        _LightMap ("Light map", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _LitTex;
            sampler2D _LightMap;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 lit = tex2D(_LitTex, i.uv).rgb;
                fixed light = tex2D(_LightMap, i.uv).r;
                return fixed4(lit, light);
            }
            ENDCG
        }
    }
}
