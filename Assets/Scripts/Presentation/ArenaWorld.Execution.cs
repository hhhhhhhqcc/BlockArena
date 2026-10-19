using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        readonly Transform[] executionLocks=new Transform[4],executionBinds=new Transform[4],executionSouls=new Transform[4];
        readonly Transform[] executionBeams=new Transform[4],executionBursts=new Transform[4],executionFlashes=new Transform[4],executionWaves=new Transform[4],executionHalos=new Transform[4];
        readonly Transform[,] executionMotes=new Transform[4,6];
        readonly Transform[] executionDebris=new Transform[4];
        readonly Transform[,] executionChunks=new Transform[4,32];
        readonly bool[] executionConnected=new bool[4];
        readonly Light[] executionLights=new Light[4];
        int executionCount,plannedExecutionCount,executionLockCount,executionSoulCount;
        int executionBeamCount,executionBurstCount,executionFocusSlot=-1;
        float executionCameraWeight;
        internal const float ExecutionShatterDelay=.18f,ExecutionSoulStart=.40f,ExecutionSoulEnd=CommanderRitualTimeline.SoulEndSeconds;
        const float ExecutionBeamTravel=.22f,ExecutionSealDelay=.28f;
        string highlightedSkillId,highlightedOwnerId;
        int skillStateRevision,highlightedRevision=-1,skillHighlightCount;
        readonly HashSet<string> skillMatchingKinds=new HashSet<string>();

        public int ExecutionCount { get { return executionCount; } }
        public int PlannedExecutionCount { get { return plannedExecutionCount; } }
        public int NormalCommanderImpactCount { get { return ritualImpactCount-executionCount; } }
        public bool ExecutionActive { get { return executionBeamCount>0||executionLockCount>0||executionBurstCount>0||executionSoulCount>0; } }
        internal int ExecutionLockCount { get { return executionLockCount; } }
        internal int ExecutionSoulCount { get { return executionSoulCount; } }
        internal int SkillHighlightCount { get { return skillHighlightCount; } }
        internal int ExecutionFocusSlot { get { return executionFocusSlot; } }
        internal float ExecutionCameraWeight { get { return executionCameraWeight; } }
        internal bool ExecutionBeamConnected(int slot) { return slot>=0&&slot<4&&executionConnected[slot]&&Visible(executionBeams[slot]); }
        internal bool ExecutionSealed(int slot) { return slot>=0&&slot<4&&Visible(executionLocks[slot]); }
        internal bool ExecutionExploding(int slot) { return slot>=0&&slot<4&&Visible(executionBursts[slot]); }
        internal bool ExecutionSoulVisible(int slot) { return slot>=0&&slot<4&&Visible(executionSouls[slot]); }
        internal bool ExecutionShattered(int slot)
        {
            foreach(CommanderImpact impact in commanderImpacts){
                VoxelCreature steve;
                if(impact.slot==slot&&impact.lethal&&impact.shattered&&creatures.TryGetValue(impact.key,out steve))return !steve.CommanderFigureVisible&&Visible(executionDebris[slot]);
            }
            return false;
        }
        static bool Visible(Transform visual) { return visual!=null&&visual.gameObject.activeInHierarchy; }

        void EnsureExecutionGeometry(int slot)
        {
            if(slot<0||slot>=executionLocks.Length||executionLocks[slot]!=null)return;
            executionBeams[slot]=NewRoot("Crystal soul tether "+slot,ritualRoot);
            VoxelAssets.Part(executionBeams[slot],"Continuous white-hot ray",VoxelAssets.Cached("execution/connected-ray",b=>{
                b.Box(new Vector3(0,0,.5f),new Vector3(.075f,.075f,1),VoxelAssets.Hex("#FFF0DF"));
                for(int side=0;side<4;side++){
                    Quaternion q=Quaternion.Euler(0,0,side*90);
                    b.Box(q*new Vector3(.095f,0,.5f),new Vector3(.03f,.03f,1),VoxelAssets.Hex("#E69DEB"));
                }
            }),Vector3.zero,VoxelAssets.Glow,false);
            for(int i=0;i<6;i++)executionMotes[slot,i]=VoxelAssets.Part(executionBeams[slot],"Flowing tether spark "+i,
                VoxelAssets.Block(VoxelAssets.Hex(i%2==0?"#FFD9A8":"#ED9DD9")),Vector3.zero,VoxelAssets.Glow,false);
            Transform burst=NewRoot("Execution detonation "+slot,ritualRoot);executionBursts[slot]=burst;
            executionFlashes[slot]=VoxelAssets.Part(burst,"Voxel star detonation",VoxelAssets.Cached("execution/blast-star",b=>{
                Color white=VoxelAssets.Hex("#FFF3CB"),pink=VoxelAssets.Hex("#FFB4DB");
                b.Box(Vector3.zero,Vector3.one*.7f,white,Quaternion.Euler(25,35,45));
                for(int axis=0;axis<3;axis++){
                    Quaternion q=Quaternion.Euler(axis==0?90:0,0,axis==1?90:0);
                    b.Box(Vector3.zero,new Vector3(.16f,1.8f,.16f),pink,q);
                }
            }),Vector3.up*1.15f,VoxelAssets.Glow,false);
            executionWaves[slot]=VoxelAssets.Part(burst,"Expanding ground shockwave",VoxelAssets.Cached("execution/blast-wave",b=>{
                b.Ring(1,.94f,VoxelAssets.Hex("#FFC0DC"));b.Ring(.83f,.8f,VoxelAssets.Hex("#FFE9B1"));
                for(int i=0;i<12;i++){
                    Quaternion q=Quaternion.Euler(0,i*30,0);
                    b.Box(q*new Vector3(0,0,1.07f),new Vector3(.07f,.04f,.15f),VoxelAssets.Hex("#D29DE7"),q);
                }
            }),Vector3.up*.12f,VoxelAssets.Glow,false);
            executionHalos[slot]=VoxelAssets.Part(burst,"Expanding aerial shock ring",VoxelAssets.Cached("execution/blast-halo",b=>{
                b.Ring(1,.965f,VoxelAssets.Hex("#FFE6CF"));
            }),Vector3.up*1.15f,VoxelAssets.Glow,false);
            executionDebris[slot]=NewRoot("Shattered Steve voxels "+slot,ritualRoot);
            for(int i=0;i<32;i++){
                string color=i<8?((i&2)!=0?"#493629":"#CE9E7B"):i<16?((i&1)==0?"#46B2BA":"#2B8A98"):i<24?((i&2)==0?"#CE9E7B":"#46B2BA"):((i&2)==0?"#39434E":"#6774AE");
                executionChunks[slot,i]=VoxelAssets.Part(executionDebris[slot],"Steve voxel "+i,VoxelAssets.Block(VoxelAssets.Hex(color)),Vector3.zero,null,false);
            }
            Transform root=NewRoot("Execution target seal "+slot,ritualRoot);executionLocks[slot]=root;
            VoxelAssets.Part(root,"Gold and crimson binding sigil",VoxelAssets.Cached("execution/target-sigil",b=>{
                Color gold=VoxelAssets.Hex("#FFD684"),pink=VoxelAssets.Hex("#FF729F");
                b.Ring(1.17f,1.1f,pink);b.Ring(.96f,.91f,gold);
                for(int i=0;i<8;i++){
                    Quaternion q=Quaternion.Euler(0,i*45,0);
                    b.Box(q*new Vector3(0,0,1.32f),new Vector3(.11f,.055f,.22f),gold,q);
                    b.Box(q*new Vector3(0,0,1.48f),new Vector3(.25f,.055f,.06f),pink,q);
                }
            }),new Vector3(0,.08f,0),VoxelAssets.Glow,false);
            executionBinds[slot]=VoxelAssets.Part(root,"Four rising soul bindings",VoxelAssets.Cached("execution/target-bindings",b=>{
                Color c=VoxelAssets.Hex("#FFC7D5");
                for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2){
                    b.Box(new Vector3(x*.73f,1.15f,z*.73f),new Vector3(.04f,1.9f,.04f),c);
                    b.Box(new Vector3(x*.57f,2.1f,z*.73f),new Vector3(.35f,.05f,.05f),c);
                    b.Box(new Vector3(x*.73f,2.1f,z*.57f),new Vector3(.05f,.05f,.35f),c);
                }
                b.Box(new Vector3(0,2.6f,0),new Vector3(.22f,.22f,.22f),VoxelAssets.Hex("#FFE6A7"),Quaternion.Euler(0,0,45));
            }),Vector3.zero,VoxelAssets.Glow,false);
            executionSouls[slot]=VoxelAssets.Part(ritualRoot,"Ascending commander soul "+slot,VoxelAssets.Cached("execution/commander-soul",b=>{
                Color pale=VoxelAssets.Hex("#B9F8ED"),core=VoxelAssets.Hex("#F4FFF0");
                b.Box(new Vector3(0,.42f,0),new Vector3(.43f,.63f,.24f),pale);
                b.Box(new Vector3(0,.99f,0),new Vector3(.46f,.46f,.4f),core);
                b.Box(new Vector3(-.35f,.51f,0),new Vector3(.16f,.45f,.18f),pale,Quaternion.Euler(0,0,-23));
                b.Box(new Vector3(.35f,.51f,0),new Vector3(.16f,.45f,.18f),pale,Quaternion.Euler(0,0,23));
                b.Box(new Vector3(0,.02f,0),new Vector3(.22f,.28f,.18f),pale,Quaternion.Euler(0,0,25));
                b.Box(new Vector3(0,.49f,-.14f),new Vector3(.12f,.18f,.035f),core);
            }),Vector3.zero,VoxelAssets.Glow,false);
            GameObject lamp=new GameObject("Execution target glow "+slot);lamp.transform.SetParent(root,false);lamp.transform.localPosition=Vector3.up;
            executionLights[slot]=lamp.AddComponent<Light>();executionLights[slot].type=LightType.Point;executionLights[slot].color=VoxelAssets.Hex("#FF87B1");
            executionLights[slot].range=4.5f;executionLights[slot].shadows=LightShadows.None;
            root.gameObject.SetActive(false);executionSouls[slot].gameObject.SetActive(false);
            executionBeams[slot].gameObject.SetActive(false);burst.gameObject.SetActive(false);executionDebris[slot].gameObject.SetActive(false);
        }

        void UpdateExecutionVisuals()
        {
            executionBeamCount=executionLockCount=executionBurstCount=executionSoulCount=0;
            for(int i=0;i<4;i++){
                executionConnected[i]=false;
                if(executionBeams[i]!=null)executionBeams[i].gameObject.SetActive(false);
                if(executionBursts[i]!=null)executionBursts[i].gameObject.SetActive(false);
                if(executionDebris[i]!=null)executionDebris[i].gameObject.SetActive(false);
                if(executionLocks[i]!=null)executionLocks[i].gameObject.SetActive(false);
                if(executionSouls[i]!=null)executionSouls[i].gameObject.SetActive(false);
            }
            for(int i=0;i<commanderImpacts.Count;i++){
                CommanderImpact impact=commanderImpacts[i];VoxelCreature steve;int slot=impact.slot;
                if(!impact.lethal||slot<0||slot>=4||!creatures.TryGetValue(impact.key,out steve))continue;
                float lockStart=impact.launchAt+ExecutionSealDelay,after=ritualAge-impact.hitAt;
                if(ritualAge>=impact.launchAt&&after<.06f){
                    executionBeamCount++;Transform beam=executionBeams[slot];beam.gameObject.SetActive(true);
                    Vector3 from=ritualCrystal.position,to=steve.Target+Vector3.up*1.15f,direction=to-from;
                    float progress=Mathf.Clamp01((ritualAge-impact.launchAt)/ExecutionBeamTravel);
                    float length=Mathf.Max(.001f,direction.magnitude*progress),pulse=1+Mathf.Sin(ritualAge*38)*.1f;
                    beam.position=from;beam.rotation=Quaternion.LookRotation(direction)*Quaternion.Euler(0,0,ritualAge*38);
                    beam.localScale=new Vector3(pulse,pulse,length);
                    executionConnected[slot]=progress>=1;
                    for(int m=0;m<6;m++){
                        float travel=Mathf.Repeat((ritualAge-impact.launchAt)*1.7f+m/6f,1),angle=travel*18+ritualAge*8;
                        Transform mote=executionMotes[slot,m];
                        mote.localPosition=new Vector3(Mathf.Cos(angle)*.16f,Mathf.Sin(angle)*.16f,travel);
                        mote.localRotation=Quaternion.Euler(0,0,angle*Mathf.Rad2Deg);
                        mote.localScale=new Vector3(.1f,.1f,.1f/length);
                    }
                }
                if(ritualAge>=lockStart&&after<.11f){
                    executionLockCount++;Transform seal=executionLocks[slot];seal.gameObject.SetActive(true);seal.position=steve.Target;
                    float rise=Mathf.SmoothStep(0,1,Mathf.Clamp01((ritualAge-lockStart)/.32f)),pulse=1+Mathf.Sin(ritualAge*18)*.045f;
                    seal.localScale=new Vector3(pulse*(.75f+rise*.25f),1,pulse*(.75f+rise*.25f));
                    executionBinds[slot].localScale=new Vector3(1,.25f+rise*.75f,1);
                    executionBinds[slot].localRotation=Quaternion.Euler(0,(1-rise)*36+Mathf.Sin(ritualAge*5)*4,0);
                    executionLights[slot].intensity=.65f+rise*1.3f;
                }
                if(impact.struck&&after>=0&&after<.52f){
                    executionBurstCount++;Transform burst=executionBursts[slot];burst.gameObject.SetActive(true);burst.position=steve.Target;
                    Transform flash=executionFlashes[slot];flash.gameObject.SetActive(after<ExecutionShatterDelay);
                    float flashLife=Mathf.Clamp01(after/ExecutionShatterDelay);
                    flash.localScale=Vector3.one*(.3f+Mathf.Sin(flashLife*Mathf.PI)*1.55f);
                    flash.localRotation=Quaternion.Euler(after*210,after*180,45);
                    float waveLife=Mathf.Clamp01(after/.52f),waveSize=.35f+Mathf.Sqrt(waveLife)*2.7f;
                    executionWaves[slot].localScale=new Vector3(waveSize,1,waveSize);
                    executionWaves[slot].localRotation=Quaternion.Euler(0,after*65,0);
                    executionHalos[slot].localScale=Vector3.one*(.2f+waveLife*2.6f);
                    executionHalos[slot].rotation=viewCamera.transform.rotation*Quaternion.Euler(-90,0,0);
                }
                if(impact.shattered&&after<ExecutionShatterDelay+.95f)UpdateExecutionFragments(slot,steve.Target,after-ExecutionShatterDelay);
                if(impact.struck&&after>=ExecutionSoulStart&&after<ExecutionSoulEnd){
                    executionSoulCount++;Transform soul=executionSouls[slot];soul.gameObject.SetActive(true);
                    float life=Mathf.Clamp01((after-ExecutionSoulStart)/(ExecutionSoulEnd-ExecutionSoulStart)),size=Mathf.Min(1,life*8)*Mathf.Clamp01((1-life)*3.8f);
                    soul.position=steve.Target+Vector3.up*(.8f+life*2.55f)+new Vector3(Mathf.Sin(life*6)*.13f,0,0);
                    soul.localScale=new Vector3(size*.84f,size*(1+life*.35f),size*.84f);
                    soul.rotation=Quaternion.Euler(0,slot*-90+life*95,Mathf.Sin(life*11)*5);
                }
            }
        }

        void ApplyExecutionCamera(ref Vector3 focus,ref float size,float aspect)
        {
            executionCameraWeight=0;executionFocusSlot=-1;
            if(!ritualActive||plannedExecutionCount==0)return;
            CommanderImpact selected=null;
            for(int i=0;i<commanderImpacts.Count;i++){
                CommanderImpact impact=commanderImpacts[i];
                if(impact.lethal&&ritualAge>=impact.timing.cameraInAt&&ritualAge<impact.timing.cameraWideAt){selected=impact;break;}
            }
            VoxelCreature target;if(selected==null||!creatures.TryGetValue(selected.key,out target)){executionFocusSlot=-1;return;}
            executionFocusSlot=selected.slot;
            float enter=Mathf.SmoothStep(0,1,Mathf.Clamp01((ritualAge-selected.timing.cameraInAt)/CommanderRitualTimeline.CameraInSeconds));
            float leave=1-Mathf.SmoothStep(0,1,Mathf.Clamp01((ritualAge-selected.timing.cameraOutAt)/CommanderRitualTimeline.CameraOutSeconds));
            executionCameraWeight=enter*leave;
            // Frame the firing platform, connecting ray and victim together instead of cropping to Steve.
            Quaternion rotation=Quaternion.LookRotation(cameraYaw*new Vector3(0,-31,-35),Vector3.up);
            Vector3 right=rotation*Vector3.right,up=rotation*Vector3.up;
            Vector2 minimum=new Vector2(float.MaxValue,float.MaxValue),maximum=new Vector2(float.MinValue,float.MinValue);
            IncludeExecutionBounds(new Bounds(new Vector3(0,.12f,0),new Vector3(6,.3f,6)),right,up,ref minimum,ref maximum);
            IncludeExecutionBounds(new Bounds(new Vector3(0,3.6f,0),new Vector3(3.4f,7.2f,3.4f)),right,up,ref minimum,ref maximum);
            IncludeExecutionBounds(new Bounds(target.Target+Vector3.up*2.1f,new Vector3(6,4.8f,6)),right,up,ref minimum,ref maximum);
            Vector2 center=(minimum+maximum)*.5f,span=maximum-minimum;
            float framedSize=Mathf.Max(size*.56f,Mathf.Max(span.y/(2*.78f),span.x/(2*aspect*.8f)));
            Vector3 framedFocus=(target.Target+Vector3.up*2.1f)*.5f;
            framedFocus+=right*(center.x-Vector3.Dot(right,framedFocus))+up*(center.y-Vector3.Dot(up,framedFocus));
            // Leave breathing room toward the right-side player HUD and the bottom status strip.
            framedFocus+=right*(framedSize*aspect*.10f)+up*(framedSize*.04f);
            focus=Vector3.Lerp(focus,framedFocus,executionCameraWeight);
            size=Mathf.Lerp(size,framedSize,executionCameraWeight);
        }

        static void IncludeExecutionBounds(Bounds bounds,Vector3 right,Vector3 up,ref Vector2 minimum,ref Vector2 maximum)
        {
            for(int i=0;i<8;i++){
                Vector3 point=bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                Vector2 projected=new Vector2(Vector3.Dot(right,point),Vector3.Dot(up,point));
                minimum=Vector2.Min(minimum,projected);maximum=Vector2.Max(maximum,projected);
            }
        }

        void UpdateExecutionFragments(int slot,Vector3 at,float age)
        {
            Transform debris=executionDebris[slot];debris.gameObject.SetActive(true);debris.position=at;
            // Reconstruct fragments directly from scheduled time; no fresh physics burst after a stalled frame.
            for(int i=0;i<32;i++){
                int cell=i%8;float x=(cell&1)==0?-1:1,z=(cell&4)==0?-1:1,y=(cell&2)==0?-1:1;
                Vector3 origin=i<8?new Vector3(x*.14f,1.59f+y*.14f,z*.14f):i<16?new Vector3(x*.14f,1.03f+y*.16f,z*.09f):i<24?new Vector3(x*.42f,1.03f+y*.19f,z*.09f):new Vector3(x*.16f,.34f+y*.17f,z*.09f);
                float angle=i*2.39996f,speed=1.4f+(i%5)*.35f;
                Vector3 velocity=new Vector3(Mathf.Cos(angle)*speed,1.5f+(i%4)*.32f,Mathf.Sin(angle)*speed);
                Transform chunk=executionChunks[slot,i];
                Vector3 position=origin+velocity*age+Vector3.down*(4.9f*age*age);position.y=Mathf.Max(.09f,position.y);
                chunk.localPosition=position;chunk.localRotation=Quaternion.Euler(age*(125+i*13),age*(170+i*9),age*(90+i*7));
                float fade=Mathf.Clamp01((.95f-age)/.26f),size=i<8?.24f:i<16?.21f:.16f;
                chunk.localScale=new Vector3(size,size*(i>=16?1.35f:1),size)*fade;
            }
        }

        void ResetExecutionVisuals(bool resetCounters)
        {
            executionBeamCount=executionLockCount=executionBurstCount=executionSoulCount=0;executionFocusSlot=-1;executionCameraWeight=0;
            if(resetCounters)executionCount=plannedExecutionCount=0;
            for(int i=0;i<4;i++){
                executionConnected[i]=false;
                if(executionBeams[i]!=null)executionBeams[i].gameObject.SetActive(false);
                if(executionBursts[i]!=null)executionBursts[i].gameObject.SetActive(false);
                if(executionDebris[i]!=null)executionDebris[i].gameObject.SetActive(false);
                if(executionLocks[i]!=null)executionLocks[i].gameObject.SetActive(false);
                if(executionSouls[i]!=null)executionSouls[i].gameObject.SetActive(false);
            }
        }

        public void SetSkillHighlight(string skillId,string ownerId)
        {
            if(state==null||string.IsNullOrEmpty(skillId)||ownerId!=playerId){ClearSkillHighlight();return;}
            if(highlightedSkillId!=skillId||highlightedOwnerId!=ownerId||highlightedRevision!=skillStateRevision){
                highlightedSkillId=skillId;highlightedOwnerId=ownerId;highlightedRevision=skillStateRevision;skillMatchingKinds.Clear();
                TradeDefinition skill=null;
                // Hover ids come from UI, but invalid or equipment ids must safely clear the preview.
                for(int i=0;i<TradeCatalog.Ids.Length;i++)if(TradeCatalog.Ids[i]==skillId){skill=TradeCatalog.Get(skillId);break;}
                PlayerState owner=state.players.Find(p=>p.id==ownerId);
                if(skill!=null&&skill.type=="skill"&&owner!=null)
                    foreach(string kind in CreatureCatalog.Kinds)if(TradeCatalog.Applies(skill,CreatureCatalog.Get(kind)))skillMatchingKinds.Add(kind);
            }
            skillHighlightCount=0;
            foreach(VoxelCreature creature in creatures.Values){
                // Summons can reuse their caster's unitId but have different skill eligibility.
                bool match=creature.Kind!="steve"&&!creature.Dead&&creature.Slot==localSlot&&skillMatchingKinds.Contains(creature.Kind);
                creature.SetSkillHighlighted(match);if(match)skillHighlightCount++;
            }
        }
        void ClearSkillHighlight()
        {
            if(highlightedSkillId==null&&skillHighlightCount==0)return;
            highlightedSkillId=highlightedOwnerId=null;highlightedRevision=-1;skillHighlightCount=0;skillMatchingKinds.Clear();
            foreach(VoxelCreature creature in creatures.Values)creature.SetSkillHighlighted(false);
        }
    }
}
