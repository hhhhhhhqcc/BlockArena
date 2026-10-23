Shader "BlockArena/ConcealedVoxel"
{
    Properties {
        _Glossiness ("Smoothness",Range(0,1)) = 0.12
        _Glow ("Emission",Range(0,2)) = 0
        _Visibility ("Voxel visibility",Range(0,1)) = 1
    }
    SubShader {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert addshadow
        #pragma target 3.0
        #pragma multi_compile_instancing
        struct Input { float4 color : COLOR; float4 screenPos; };
        half _Glossiness, _Glow, _Visibility;
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o);o.color=v.color; }
        void surf(Input IN,inout SurfaceOutputStandard o) {
            // Four ordered voxel cells remove only one quarter of the friendly body.
            // Depth writes, the original palette and all solid team markers are retained.
            float2 cell=floor(IN.screenPos.xy/max(.0001,IN.screenPos.w)*_ScreenParams.xy*.5);
            float x=fmod(cell.x,2),y=fmod(cell.y,2);
            float threshold=(x<1?(y<1?.125:.875):(y<1?.625:.375));
            clip(_Visibility-threshold);
            o.Albedo=GammaToLinearSpace(IN.color.rgb)*lerp(.93,1,_Visibility);
            o.Metallic=0;o.Smoothness=_Glossiness;o.Occlusion=1;o.Alpha=1;
            o.Emission=GammaToLinearSpace(IN.color.rgb)*_Glow;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
