// Paints one light into the screen-space light map. BlendOp Max means overlapping lights
// keep the brightest value instead of adding up, matching Reveal.LightAt on the CPU.
Shader "Lamplighter/LightBrush"
{
    Properties { _MainTex ("Brush", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off ZWrite Off ZTest Always Lighting Off
        BlendOp Max
        Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; o.color = v.color; return o; }
            fixed4 frag (v2f i) : SV_Target { fixed a = tex2D(_MainTex, i.uv).a * i.color.a; return fixed4(a, a, a, a); }
            ENDCG
        }
    }
}
