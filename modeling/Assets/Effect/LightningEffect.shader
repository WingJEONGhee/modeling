Shader "Custom/LightningEffect"
{
    Properties
    {
        _MainTex("Noise Texture", 2D) = "white" {}
        _Color("Lightning Color", Color) = (1,1,1,1)
        _Speed("Scroll Speed", Float) = 1.0
        _Intensity("Glow Intensity", Float) = 2.0
        _Tiling("UV Tiling", Vector) = (1, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _Speed;
            float _Intensity;
            float4 _Tiling;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformObjectToHClip(v.vertex.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uv *= _Tiling.xy;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float time = _Time.y * _Speed;
                float2 uv = i.uv;
                uv.y += time;

                float noise = tex2D(_MainTex, uv).r;

                // π‡±‚ ∞≠¡∂ (¿œ∑∑¿”)
                float brightness = saturate(noise * _Intensity);
                float4 col = _Color * brightness;
                col.a = brightness;

                return col;
            }
            ENDHLSL
        }
    }
}
