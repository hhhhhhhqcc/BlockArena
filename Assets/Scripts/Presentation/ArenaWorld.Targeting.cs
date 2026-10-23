using System;
using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        // These three retained meshes conform to the actual surface. They are not
        // screen-space ellipses, so ramps, camera rotation and zoom cannot offset them.
        sealed class SurfaceCircle
        {
            public Transform root;
            public Mesh mesh;
            public MeshRenderer renderer;
            public readonly Vector3[] vertices=new Vector3[64*4];
        }
        readonly HashSet<string> tacticalSelection=new HashSet<string>();
        readonly List<SurfaceCircle> tacticalCircles=new List<SurfaceCircle>();
        readonly MaterialPropertyBlock targetingTint=new MaterialPropertyBlock();
        SurfaceCircle attackCircle,castCircle,commandCircle;
        Transform commandReticle;
        MeshRenderer commandReticleRenderer;
        VoxelCreature pinnedCreature;
        string commandPreviewSkill;
        Point2? commandPreviewTarget;
        bool commandTargeting;
        public bool ShowCommanderRange { get; set; } = true;
        public bool ShowCreatureRange { get; set; } = true;
        internal bool CommanderRangeVisible { get { return castCircle!=null&&castCircle.root.gameObject.activeInHierarchy; } }
        internal bool CommandAreaVisible { get { return commandCircle!=null&&commandCircle.root.gameObject.activeInHierarchy; } }
        internal bool CreatureRangeVisible { get { return attackCircle!=null&&attackCircle.root.gameObject.activeInHierarchy; } }
        internal int TacticalCircleCount { get; private set; }
        internal int TacticalSelectionCount
        {
            get {
                int count=0;if(state==null||state.phase!="tactics")return 0;
                foreach(VoxelCreature view in creatures.Values)
                    if(view.Slot==localSlot&&view.Kind!="steve"&&!view.Dead&&view.gameObject.activeInHierarchy&&tacticalSelection.Contains(view.UnitId))count++;
                return count;
            }
        }

        public void SetPinnedCreature(VoxelCreature creature){pinnedCreature=creature;}
        public void SetPinnedCreature(string id)
        {
            pinnedCreature=null;if(string.IsNullOrEmpty(id))return;
            VoxelCreature found;
            if(creatures.TryGetValue(id,out found)||creatures.TryGetValue("unit:"+id,out found)||creatures.TryGetValue("fighter:"+id,out found))
            {pinnedCreature=found;return;}
            foreach(VoxelCreature view in creatures.Values)if(view.UnitId==id){pinnedCreature=view;return;}
        }
        public void SetTacticalSelection(IEnumerable<string> ids)
        {tacticalSelection.Clear();if(ids!=null)foreach(string id in ids)if(!string.IsNullOrEmpty(id))tacticalSelection.Add(id);}
        public void SetCommandPreview(string skill,Point2? target,bool targeting)
        {commandPreviewSkill=skill;commandPreviewTarget=target;commandTargeting=targeting;}

        SurfaceCircle BuildSurfaceCircle(string name,bool dashed)
        {
            EnsureCombatVfx();SurfaceCircle c=new SurfaceCircle();
            c.root=NewRoot(name,effectRoot);c.mesh=new Mesh{name=name+" surface mesh"};c.mesh.MarkDynamic();
            int[] indices=new int[64*6];Color[] colors=new Color[64*4];
            for(int n=0;n<64;n++){
                int v=n*4,t=n*6;
                indices[t]=v;indices[t+1]=v+1;indices[t+2]=v+2;
                indices[t+3]=v;indices[t+4]=v+2;indices[t+5]=v+3;
                Color color=dashed&&n%4==3?Color.clear:Color.white;
                for(int i=0;i<4;i++)colors[v+i]=color;
            }
            c.mesh.vertices=c.vertices;c.mesh.colors=colors;c.mesh.triangles=indices;
            c.root.gameObject.AddComponent<MeshFilter>().sharedMesh=c.mesh;
            c.renderer=c.root.gameObject.AddComponent<MeshRenderer>();c.renderer.sharedMaterial=combatMaterial;
            c.renderer.shadowCastingMode=ShadowCastingMode.Off;c.renderer.receiveShadows=false;
            c.root.gameObject.SetActive(false);return c;
        }
        void UpdateSurfaceCircle(SurfaceCircle c,float x,float y,float radius,Color color,float width=.055f)
        {
            float r=radius*WorldScale,inner=Mathf.Max(.03f,r-width);
            for(int n=0;n<64;n++){
                float a=n*Mathf.PI/32,b=(n+1)*Mathf.PI/32;int i=n*4;
                c.vertices[i]=SurfaceRingPoint(x,y,Mathf.Cos(a)*r,Mathf.Sin(a)*r);
                c.vertices[i+1]=SurfaceRingPoint(x,y,Mathf.Cos(b)*r,Mathf.Sin(b)*r);
                c.vertices[i+2]=SurfaceRingPoint(x,y,Mathf.Cos(b)*inner,Mathf.Sin(b)*inner);
                c.vertices[i+3]=SurfaceRingPoint(x,y,Mathf.Cos(a)*inner,Mathf.Sin(a)*inner);
            }
            c.mesh.vertices=c.vertices;c.mesh.RecalculateBounds();
            targetingTint.Clear();targetingTint.SetColor(CombatTintId,color);c.renderer.SetPropertyBlock(targetingTint);
            c.root.gameObject.SetActive(true);
        }
        Vector3 SurfaceRingPoint(float x,float y,float dx,float dz)
        {return WorldSurfacePoint(x+dx/WorldScale,y+dz/WorldScale)+Vector3.up*.075f;}
        static void HideCircle(SurfaceCircle c){if(c!=null)c.root.gameObject.SetActive(false);}
        void UpdateTacticalCircles()
        {
            int count=0;
            if(state!=null&&state.phase=="tactics")foreach(VoxelCreature view in creatures.Values){
                if(view.Slot!=localSlot||view.Kind=="steve"||view.Dead||!view.gameObject.activeInHierarchy||!tacticalSelection.Contains(view.UnitId))continue;
                if(count==tacticalCircles.Count)tacticalCircles.Add(BuildSurfaceCircle("Selected squad surface ring "+count,false));
                Bounds body;float footprint=TryGetBodyBounds(view,out body)?Mathf.Max(body.extents.x,body.extents.z):.65f*view.Size;
                float radius=Mathf.Clamp(footprint*1.12f,.35f,4f)/WorldScale;
                Vector3 at=view.transform.position;Color c=VoxelAssets.Hex("#F3D88E");c.a=.9f;
                UpdateSurfaceCircle(tacticalCircles[count++],at.x/WorldScale,at.z/WorldScale,radius,c,.065f);
            }
            for(int i=count;i<tacticalCircles.Count;i++)HideCircle(tacticalCircles[i]);
            TacticalCircleCount=count;
        }
        void UpdateTargetingPresentation(float time)
        {
            UpdateTacticalCircles();
            bool playing=state!=null&&state.phase!="lobby";
            VoxelCreature inspected=hovered!=null?hovered:pinnedCreature;
            if(ShowCreatureRange&&playing&&inspected!=null&&inspected.gameObject.activeInHierarchy&&!inspected.Dead&&inspected.Kind!="steve"){
                if(attackCircle==null)attackCircle=BuildSurfaceCircle("Inspected creature attack reach",true);
                FighterState fighter;float range=fighterViews.TryGetValue(inspected,out fighter)?fighter.range:CreatureCatalog.Get(inspected.Kind).range;
                Vector3 at=inspected.transform.position;
                Color c=inspected.Slot==localSlot?VoxelAssets.Hex("#8BE5D8"):VoxelAssets.Hex("#F2A397");c.a=.62f;
                UpdateSurfaceCircle(attackCircle,at.x/WorldScale,at.z/WorldScale,Mathf.Max(8,range),c,.045f);
            }else HideCircle(attackCircle);
            PlayerState local=state==null?null:state.players.Find(p=>p.id==playerId);
            bool targeting=state!=null&&state.phase=="battle"&&local!=null&&!local.eliminated&&commandTargeting&&!string.IsNullOrEmpty(commandPreviewSkill);
            if(!targeting){HideCircle(castCircle);HideCircle(commandCircle);if(commandReticle!=null)commandReticle.gameObject.SetActive(false);return;}
            VoxelCreature commander;Point2 from=local.steve;
            if(creatures.TryGetValue("steve:"+local.id,out commander))from=new Point2(commander.transform.position.x/WorldScale,commander.transform.position.z/WorldScale);
            if(ShowCommanderRange){
                if(castCircle==null)castCircle=BuildSurfaceCircle("Steve cast distance only while aiming",true);
                Color c=Teams[Mathf.Clamp(local.slot,0,3)];c.a=.52f;
                UpdateSurfaceCircle(castCircle,from.x,from.y,200,c,.045f);
            }else HideCircle(castCircle);
            if(!commandPreviewTarget.HasValue){HideCircle(commandCircle);if(commandReticle!=null)commandReticle.gameObject.SetActive(false);return;}
            Point2 target=commandPreviewTarget.Value;
            CommanderDefinition skill=BattlefieldCatalog.GetCommander(commandPreviewSkill);
            float dx=target.x-from.x,dy=target.y-from.y;
            float edge=BattlefieldCatalog.Extent(state.battlefield);
            bool valid=dx*dx+dy*dy<=200*200&&Mathf.Abs(target.x)<=edge&&Mathf.Abs(target.y)<=edge;
            if(skill.id=="rift"||skill.id=="trap"||skill.id=="decoy")valid&=BattlefieldCatalog.IsPassable(state.battlefield,target.x,target.y);
            Color area=VoxelAssets.Hex(valid?(skill.id=="rift"?"#CAADFF":skill.id=="focus"?"#FFB993":"#A2EDDE"):"#FA807D");
            area.a=.78f;
            if(commandCircle==null)commandCircle=BuildSurfaceCircle("Command target area",false);
            // Decoy's radius is its taunt, and rift's radius is the departure squad.
            // Show the departure radius around Steve and a separate arrival reticle.
            Point2 center=skill.id=="rift"?from:target;
            UpdateSurfaceCircle(commandCircle,center.x,center.y,skill.radius>0?skill.radius:22,area,.07f);
            if(commandReticle==null){
                EnsureCombatVfx();commandReticle=VoxelAssets.Part(effectRoot,"Command arrival reticle",VoxelAssets.Cached("command-aim-reticle",b=>{
                    for(int i=0;i<4;i++){
                        Quaternion q=Quaternion.Euler(0,i*90,0);
                        b.Box(q*new Vector3(.44f,0,.32f),new Vector3(.24f,.02f,.05f),Color.white,q);
                        b.Box(q*new Vector3(.54f,0,.23f),new Vector3(.05f,.02f,.22f),Color.white,q);
                    }
                }),Vector3.zero,combatMaterial,false);commandReticleRenderer=commandReticle.GetComponent<MeshRenderer>();
            }
            commandReticle.gameObject.SetActive(true);commandReticle.position=WorldSurfacePoint(target.x,target.y)+Vector3.up*.12f;
            commandReticle.localScale=Vector3.one*(.94f+Mathf.Sin(time*6)*.045f);
            targetingTint.Clear();targetingTint.SetColor(CombatTintId,area);commandReticleRenderer.SetPropertyBlock(targetingTint);
        }
        void ResetTargetingPresentation()
        {
            tacticalSelection.Clear();commandTargeting=false;pinnedCreature=null;
            foreach(var circle in tacticalCircles)HideCircle(circle);TacticalCircleCount=0;
            HideCircle(attackCircle);HideCircle(castCircle);HideCircle(commandCircle);
            if(commandReticle!=null)commandReticle.gameObject.SetActive(false);
        }
        void DisposeTargetingPresentation()
        {
            foreach(var circle in tacticalCircles)Destroy(circle.mesh);
            if(attackCircle!=null)Destroy(attackCircle.mesh);
            if(castCircle!=null)Destroy(castCircle.mesh);
            if(commandCircle!=null)Destroy(commandCircle.mesh);
        }
    }
}
