Shader "BlockArena/ArenaVoxelLit"
{
    Properties { _Glossiness ("Smoothness",Range(0,1)) = 0.12 _Glow ("Emission",Range(0,2)) = 0 }
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        struct Input { float4 color : COLOR; };
        half _Glossiness; half _Glow;
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o);o.color=v.color; }
        void surf(Input IN,inout SurfaceOutputStandard o) { o.Albedo=GammaToLinearSpace(IN.color.rgb);o.Metallic=0;o.Smoothness=_Glossiness;o.Occlusion=1;o.Alpha=1;o.Emission=GammaToLinearSpace(IN.color.rgb)*_Glow; }
        ENDCG
    }
    FallBack "Diffuse"
}
