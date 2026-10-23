using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        // The foundation and armies arrive together, before any tall scenery is
        // revealed. All subsequent layers keep their authored, full-size geometry.
        const float FieldFoundationSeconds=1.25f,FieldGroundDepth=3.6f;

        float FieldEmergenceAge
        {
            get {
                if(fieldPhase!="reveal")return ArenaGame.RevealTerrainSeconds;
                if(fieldRevealStage=="arena"||fieldRevealStage=="map")return 0;
                if(fieldRevealStage!="terrain")return ArenaGame.RevealTerrainSeconds;
                return Mathf.Clamp(fieldStageAge+Mathf.Max(0,Time.unscaledTime-fieldSnapshotTime),0,ArenaGame.RevealTerrainSeconds);
            }
        }
        static float FieldLayerProgress(float age,float start,float end)
        {return Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,end,age));}
        float FieldGroundRise {get{return FieldLayerProgress(FieldEmergenceAge,FieldFoundationSeconds,2.65f);}}
        float FieldGroundOffset {get{return -FieldGroundDepth*(1-FieldGroundRise);}}
        float FieldEnvironmentRise {get{return FieldLayerProgress(FieldEmergenceAge,2.35f,ArenaGame.RevealTerrainSeconds);}}

        float TerrainEmergenceProgress(string kind)
        {
            float age=FieldEmergenceAge;
            if(kind=="water"||kind=="wetland")return FieldLayerProgress(age,1.8f,3.25f);
            if(kind=="forest")return FieldLayerProgress(age,2.15f,3.65f);
            if(kind=="ruins")return FieldLayerProgress(age,2,3.4f);
            return FieldGroundRise;
        }
        float TerrainEmergenceOffset(string kind)
        {
            float depth=kind=="forest"?4.5f:kind=="ruins"?3.8f:FieldGroundDepth;
            return -depth*(1-TerrainEmergenceProgress(kind));
        }
        void ApplyFieldEmergence()
        {
            if(fieldMap==null)return;
            bool available=fieldPhase!="reveal"||(fieldRevealStage!="arena"&&fieldRevealStage!="map");
            fieldMap.root.gameObject.SetActive(available);
            if(!available)return;
            float age=FieldEmergenceAge;
            // The new perimeter advances radially while existing inner land keeps
            // its world coordinates; only the stone foundation is horizontally scaled.
            float spread=ExpansionScale/Mathf.Max(1,fieldMap.scale);
            fieldMap.floor.localScale=Vector3.one;UpdateLandscapeExpansion(spread);
            if(fieldMap.environment!=null){
                float p=FieldEnvironmentRise;
                fieldMap.environment.gameObject.SetActive(p>.00001f);
                fieldMap.environment.localScale=Vector3.one;
                fieldMap.environment.localPosition=Vector3.down*(12*(1-p));
            }
            foreach(var view in fieldMap.terrain){
                float p=TerrainEmergenceProgress(view.kind);
                view.root.gameObject.SetActive(p>.00001f&&view.state!=null);
                view.root.localScale=Vector3.one;
                view.root.localPosition=FieldAt(view.x,view.y,TerrainEmergenceOffset(view.kind));
            }
            float objectives=FieldLayerProgress(age,3.05f,ArenaGame.RevealTerrainSeconds);
            foreach(var view in fieldMap.objectives){
                view.root.gameObject.SetActive(objectives>.00001f&&view.state!=null);
                view.root.localScale=Vector3.one;
                view.root.localPosition=FieldAt(view.x,view.y,BattlefieldVisualHeight(view.x,view.y)+.11f-5*(1-objectives));
            }
            float foundation=FieldLayerProgress(age,0,FieldFoundationSeconds);
            fieldMap.revealRing.gameObject.SetActive(age>0&&age<ArenaGame.RevealTerrainSeconds);
            if(fieldMap.revealRing.gameObject.activeSelf){
                float radius=LandscapeRadius(fieldMap.scale)*spread;
                fieldMap.revealRing.localScale=new Vector3(radius,1,radius);
                Color rune=FieldColor(fieldMap.id=="frost"?"#B6ECF5":fieldMap.id=="bog"?"#A7D394":"#EBD19A");
                rune.a=(.2f+Mathf.Sin(foundation*Mathf.PI)*.45f)*(1-FieldEnvironmentRise);
                TintField(fieldMap.revealRenderer,rune);
            }
        }
    }
}
