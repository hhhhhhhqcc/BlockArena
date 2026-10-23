using System.Collections.Generic;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class VoxelCreature
    {
        static readonly Dictionary<Material,Material> concealedMaterials=new Dictionary<Material,Material>();
        static readonly int ConcealVisibilityId=Shader.PropertyToID("_Visibility");
        static Shader concealmentShader;
        Renderer[] concealmentParts;
        Material[] unconcealedMaterials;
        MaterialPropertyBlock concealmentProperties;
        Transform concealmentIcon;
        bool concealedFriendly,concealmentMaterialApplied;
        float concealmentVisibility=1;
        internal bool FriendlyConcealed { get { return concealedFriendly&&!Dead; } }
        internal bool ConcealmentIconVisible { get { return concealmentIcon!=null&&concealmentIcon.gameObject.activeInHierarchy; } }
        internal bool TeamFootprintVisible { get { return teamRing!=null&&teamRing.gameObject.activeInHierarchy; } }
        internal float ConcealmentVisibility { get { return concealmentVisibility; } }

        void BuildConcealmentFeedback()
        {
            if(Kind=="steve"||concealmentParts!=null)return;
            concealmentParts=figure.GetComponentsInChildren<Renderer>(true);
            unconcealedMaterials=new Material[concealmentParts.Length];
            for(int i=0;i<concealmentParts.Length;i++){
                unconcealedMaterials[i]=concealmentParts[i].sharedMaterial;
                ConcealedMaterial(unconcealedMaterials[i]);
            }
            concealmentProperties=new MaterialPropertyBlock();
            concealmentIcon=VoxelAssets.Part(transform,"Friendly concealment eye",VoxelAssets.Cached("concealment-eye-closed",mesh=>{
                Color ink=VoxelAssets.Hex("#163633"),mint=VoxelAssets.Hex("#B9EAD5");
                mesh.Box(Vector3.zero,new Vector3(.52f,.31f,.045f),ink);
                for(int i=-2;i<=2;i++)mesh.Box(new Vector3(i*.075f,-.015f+Mathf.Abs(i)*.025f,-.035f),new Vector3(.078f,.042f,.022f),mint);
                mesh.Box(new Vector3(-.13f,-.08f,-.035f),new Vector3(.032f,.07f,.022f),mint);
                mesh.Box(new Vector3(.13f,-.08f,-.035f),new Vector3(.032f,.07f,.022f),mint);
            }),Vector3.zero,VoxelAssets.Glow,false);
            concealmentIcon.gameObject.SetActive(false);
        }
        static Material ConcealedMaterial(Material original)
        {
            if(original==null)return null;
            Material material;if(concealedMaterials.TryGetValue(original,out material))return material;
            if(concealmentShader==null)concealmentShader=Resources.Load<Shader>("ArenaConcealedVoxel");
            if(concealmentShader==null||!concealmentShader.isSupported)return original;
            material=new Material(original){name="Shared friendly concealed voxel",shader=concealmentShader,enableInstancing=true};
            concealedMaterials.Add(original,material);return material;
        }
        internal void SetConcealmentState(bool hidden,bool friendly)
        {
            concealedFriendly=Battle&&!Dead&&Kind!="steve"&&friendly&&hidden;
            if(concealedFriendly&&!concealmentMaterialApplied){
                BuildConcealmentFeedback();
                for(int i=0;i<concealmentParts.Length;i++)concealmentParts[i].sharedMaterial=ConcealedMaterial(unconcealedMaterials[i]);
                concealmentMaterialApplied=true;
            }
        }
        void ResetConcealmentFeedback()
        {
            concealedFriendly=false;concealmentVisibility=1;
            RestoreConcealmentMaterials();if(concealmentIcon!=null)concealmentIcon.gameObject.SetActive(false);
        }
        void RestoreConcealmentMaterials()
        {
            if(!concealmentMaterialApplied||concealmentParts==null)return;
            for(int i=0;i<concealmentParts.Length;i++){
                concealmentParts[i].sharedMaterial=unconcealedMaterials[i];concealmentParts[i].SetPropertyBlock(null);
            }
            concealmentMaterialApplied=false;
        }
        void StepConcealmentFeedback(float dt,Camera camera)
        {
            bool active=concealedFriendly&&Battle&&!Dead;
            float previous=concealmentVisibility;
            concealmentVisibility=Mathf.MoveTowards(concealmentVisibility,active?.78f:1,dt*1.6f);
            if(concealmentMaterialApplied&&Mathf.Abs(previous-concealmentVisibility)>.00001f){
                concealmentProperties.SetFloat(ConcealVisibilityId,concealmentVisibility);
                for(int i=0;i<concealmentParts.Length;i++)concealmentParts[i].SetPropertyBlock(concealmentProperties);
                if(!active&&concealmentVisibility>=.999f)RestoreConcealmentMaterials();
            }
            if(concealmentIcon==null)return;
            concealmentIcon.gameObject.SetActive(active);
            if(active&&camera!=null){
                concealmentIcon.position=transform.position+Vector3.up*((Height+modelHover)*Size+(hovered?.65f:.34f));
                concealmentIcon.rotation=camera.transform.rotation;
                concealmentIcon.localScale=Vector3.one*(.9f/Mathf.Max(.12f,Size));
            }
        }
    }
}
