Shader "Hidden/BlockArena/VisionMask"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            Blend One One
            BlendOp Max
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float feather:TEXCOORD1; };
            v2f vert(appdata v) {
                v2f o; o.vertex=float4(v.vertex.xy*2-1,0,1);
                #if UNITY_UV_STARTS_AT_TOP
                    o.vertex.y=-o.vertex.y;
                #endif
                o.uv=v.uv; o.feather=v.color.r; return o;
            }
            fixed4 frag(v2f i):SV_Target {
                float sight=1-smoothstep(1-i.feather,1,length(i.uv));
                return float4(sight,sight,sight,1);
            }
            ENDCG
        }
    }
}
