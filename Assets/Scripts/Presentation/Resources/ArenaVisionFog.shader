Shader "Hidden/BlockArena/VisionFog"
{
    Properties { _MainTex("Scene",2D)="white"{} _SightMask("Sight",2D)="black"{} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _SightMask;
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 _MainTex_TexelSize;
            float4x4 _InverseViewProjection;
            float _MaskExtent, _ArenaRadius, _Opacity;
            fixed4 frag(v2f_img i):SV_Target {
                fixed4 scene=tex2D(_MainTex,i.uv);
                float2 depthUV=i.uv;
                #if UNITY_UV_STARTS_AT_TOP
                    if(_MainTex_TexelSize.y<0)depthUV.y=1-depthUV.y;
                #endif
                float depth=SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,depthUV);
                #if !defined(UNITY_REVERSED_Z)
                    depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
                #endif
                float2 clipXY=depthUV*2-1;
                #if UNITY_UV_STARTS_AT_TOP
                    clipXY.y=-clipXY.y;
                #endif
                float4 world=mul(_InverseViewProjection,float4(clipXY,depth,1));
                world.xyz/=world.w;
                float2 maskUV=(world.xz+_MaskExtent)/(2*_MaskExtent);
                float visibility=tex2D(_SightMask,maskUV).r;
                float boundary=1-smoothstep(_ArenaRadius-.8,_ArenaRadius+.2,length(world.xz));
                float amount=(1-visibility)*boundary*_Opacity;
                scene.rgb=lerp(scene.rgb,scene.rgb*.12+float3(.0015,.0025,.004),amount);
                return scene;
            }
            ENDCG
        }
    }
}
