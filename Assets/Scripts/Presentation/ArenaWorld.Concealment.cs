using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        const int ConcealmentRegionBudget=16,DiscoveryPulseBudget=8,SmokePartsPerRegion=9;
        sealed class ConcealmentRegion {
            internal Transform root;internal Transform[] parts;internal Renderer[] renderers;
            internal bool smoke;internal float radius=1,remaining=1,syncedAt;
        }
        sealed class EnemyConcealmentObservation {
            internal bool present,concealed;
            internal float lastFeedbackAt=-10;
        }
        sealed class DiscoveryPulse {
            internal Transform root,ring,eye;internal Renderer ringRenderer;
            internal string fighterId;
            internal float at=-10,height;
        }
        readonly Dictionary<string,EnemyConcealmentObservation> concealmentEnemies=new Dictionary<string,EnemyConcealmentObservation>();
        readonly HashSet<string> concealmentSeen=new HashSet<string>();
        readonly List<ConcealmentRegion> concealmentRegions=new List<ConcealmentRegion>(ConcealmentRegionBudget);
        readonly List<DiscoveryPulse> discoveryPulses=new List<DiscoveryPulse>(DiscoveryPulseBudget);
        readonly MaterialPropertyBlock concealmentTint=new MaterialPropertyBlock();
        Material concealmentRegionMaterial;
        Transform concealmentRoot;
        string concealmentViewer;
        int concealmentRound=-1,concealmentRegionCount,discoveryFeedbackCount;
        bool concealmentBaseline;
        internal int DiscoveryFeedbackCount { get { return discoveryFeedbackCount; } }
        internal int ConcealmentRegionCount { get { return concealmentRegionCount; } }
        internal int ActiveDiscoveryPulseCount {
            get {int count=0;foreach(var pulse in discoveryPulses)if(pulse.root.gameObject.activeInHierarchy)count++;return count;}
        }
        internal int ConcealmentSmokePartCount {
            get {int count=0;for(int i=0;i<concealmentRegionCount;i++)if(concealmentRegions[i].smoke)count+=SmokePartsPerRegion;return count;}
        }
        void EnsureConcealmentPresentation()
        {
            if(concealmentRoot!=null)return;
            concealmentRoot=NewRoot("Local concealment cues",effectRoot);
            concealmentRegionMaterial=new Material(Resources.Load<Shader>("ArenaCombat")){name="Shared low smoke and woodland hints"};
        }
        void ObserveConcealmentSnapshot(GameState next,string viewer)
        {
            if(next==null||next.phase!="battle"){
                concealmentEnemies.Clear();concealmentBaseline=false;
                if(next==null||next.phase=="lobby")discoveryFeedbackCount=0;
            }
            bool reset=next!=null&&(concealmentViewer!=viewer||concealmentRound!=next.round);
            if(reset){concealmentEnemies.Clear();concealmentBaseline=false;discoveryFeedbackCount=0;HideDiscoveryPulses();}
            concealmentViewer=viewer;concealmentRound=next==null?-1:next.round;
            concealmentSeen.Clear();
            if(next!=null)foreach(var pair in fighterViews){
                FighterState f=pair.Value;pair.Key.SetConcealmentState(next.phase=="battle"&&f.hidden,f.ownerId==viewer);
                if(next.phase!="battle"||f.dead||f.neutral||f.ownerId==viewer)continue;
                concealmentSeen.Add(f.id);EnemyConcealmentObservation observation;
                bool known=concealmentEnemies.TryGetValue(f.id,out observation);
                if(!known){observation=new EnemyConcealmentObservation();concealmentEnemies.Add(f.id,observation);}
                bool concealed=f.hidden||f.concealmentSources!=0;
                // The first snapshot/reconnect establishes a baseline. Normal enemies,
                // summons and ordinary births never generate a discovery announcement.
                bool discovered=concealmentBaseline&&!f.summoned&&Time.unscaledTime-observation.lastFeedbackAt>1.2f&&
                    (!known&&concealed||known&&!observation.present);
                if(discovered){
                    SpawnDiscoveryPulse(f.id,pair.Key.transform.position,(pair.Key.Height+.3f)*pair.Key.Size);
                    observation.lastFeedbackAt=Time.unscaledTime;
                }
                observation.present=true;observation.concealed=concealed;
            }
            foreach(var pair in concealmentEnemies)if(!concealmentSeen.Contains(pair.Key))pair.Value.present=false;
            foreach(var pulse in discoveryPulses)if(!concealmentSeen.Contains(pulse.fighterId)){
                pulse.at=-10;pulse.fighterId=null;pulse.root.gameObject.SetActive(false);
            }
            if(next!=null&&next.phase=="battle")concealmentBaseline=true;
            SyncConcealmentRegions(next);
        }
        void SyncConcealmentRegions(GameState next)
        {
            concealmentRegionCount=0;
            bool terrain=next!=null&&next.battlefield!=null&&(next.phase=="tactics"||next.phase=="battle");
            if(terrain){
                // Gameplay smoke always takes the budget before decorative woodland hints.
                if(next.phase=="battle")foreach(var zone in next.battlefield.zones){
                    if(zone.kind!="smoke"||zone.remaining<=0||concealmentRegionCount>=ConcealmentRegionBudget)continue;
                    ConcealmentRegion region=GetConcealmentRegion(concealmentRegionCount++);region.smoke=true;
                    region.radius=Mathf.Max(.5f,zone.radius*WorldScale);region.remaining=zone.remaining;region.syncedAt=Time.unscaledTime;
                    region.root.position=WorldSurfacePoint(zone.x,zone.y)+Vector3.up*.08f;region.root.rotation=Quaternion.identity;
                    region.root.localScale=Vector3.one;
                    foreach(var part in region.parts)part.gameObject.SetActive(true);
                }
                foreach(var area in next.battlefield.terrain){
                    if(area.kind!="forest"||concealmentRegionCount>=ConcealmentRegionBudget)continue;
                    ConcealmentRegion region=GetConcealmentRegion(concealmentRegionCount++);region.smoke=false;
                    region.root.position=WorldSurfacePoint(area.x,area.y)+Vector3.up*.065f;
                    region.root.rotation=Quaternion.Euler(0,-area.rotation,0);region.root.localScale=Vector3.one;
                    float x=Mathf.Max(25,area.halfWidth>0?area.halfWidth:area.radius)*WorldScale*.52f;
                    float z=Mathf.Max(25,area.halfHeight>0?area.halfHeight:area.radius)*WorldScale*.52f;
                    for(int i=0;i<region.parts.Length;i++){
                        region.parts[i].gameObject.SetActive(i<4);
                        if(i>=4)continue;
                        region.parts[i].localPosition=new Vector3(i==0?-x:i==1?x:0,.03f,i==2?-z:i==3?z:0);
                        region.parts[i].localRotation=Quaternion.Euler(90,i*90,0);region.parts[i].localScale=new Vector3(.65f,.42f,.12f);
                    }
                }
            }
            for(int i=concealmentRegionCount;i<concealmentRegions.Count;i++)concealmentRegions[i].root.gameObject.SetActive(false);
        }
        ConcealmentRegion GetConcealmentRegion(int index)
        {
            EnsureConcealmentPresentation();
            if(index<concealmentRegions.Count)return concealmentRegions[index];
            var region=new ConcealmentRegion{root=NewRoot("Pooled ground concealment region",concealmentRoot),parts=new Transform[SmokePartsPerRegion],renderers=new Renderer[SmokePartsPerRegion]};
            Mesh cloud=VoxelAssets.Cached("local-low-voxel-smoke",b=>{
                b.Box(Vector3.zero,new Vector3(1,.28f,.7f),Color.white);
                b.Box(new Vector3(-.25f,.06f,.08f),new Vector3(.5f,.31f,.55f),Color.white);
                b.Box(new Vector3(.32f,.025f,-.12f),new Vector3(.48f,.24f,.46f),Color.white);
            });
            for(int i=0;i<region.parts.Length;i++){
                region.parts[i]=VoxelAssets.Part(region.root,"Low drifting voxel cloud",cloud,Vector3.zero,concealmentRegionMaterial,false);
                region.renderers[i]=region.parts[i].GetComponent<Renderer>();
            }
            region.root.gameObject.SetActive(false);concealmentRegions.Add(region);return region;
        }
        void SpawnDiscoveryPulse(string fighterId,Vector3 at,float height)
        {
            EnsureConcealmentPresentation();DiscoveryPulse pulse=null;
            foreach(var candidate in discoveryPulses)if(Time.unscaledTime-candidate.at>=.85f){pulse=candidate;break;}
            if(pulse==null&&discoveryPulses.Count<DiscoveryPulseBudget){
                pulse=new DiscoveryPulse{root=NewRoot("Pooled discovered concealed enemy",concealmentRoot)};
                pulse.ring=VoxelAssets.Part(pulse.root,"Brief discovery footprint",VoxelAssets.Cached("discovery-footprint",b=>b.Ring(.78f,.72f,Color.white)),Vector3.zero,concealmentRegionMaterial,false);
                pulse.ringRenderer=pulse.ring.GetComponent<Renderer>();
                pulse.eye=VoxelAssets.Part(pulse.root,"Brief discovery eye",VoxelAssets.Cached("discovery-open-eye",b=>{
                    Color c=VoxelAssets.Hex("#F1DA98");
                    for(int i=-2;i<=2;i++){
                        float y=(2-Mathf.Abs(i))*.055f;
                        b.Box(new Vector3(i*.075f,y,0),new Vector3(.075f,.042f,.025f),c);
                        b.Box(new Vector3(i*.075f,-y,0),new Vector3(.075f,.042f,.025f),c);
                    }
                    b.Box(Vector3.zero,new Vector3(.055f,.105f,.035f),c);
                }),Vector3.zero,VoxelAssets.Glow,false);discoveryPulses.Add(pulse);
            }
            if(pulse==null){pulse=discoveryPulses[0];foreach(var candidate in discoveryPulses)if(candidate.at<pulse.at)pulse=candidate;}
            pulse.fighterId=fighterId;pulse.at=Time.unscaledTime;pulse.height=height;pulse.root.position=at+Vector3.up*.06f;pulse.root.gameObject.SetActive(true);discoveryFeedbackCount++;
        }
        void TickConcealmentPresentation(float time)
        {
            bool visible=FrontendMode==null&&state!=null&&(state.phase=="tactics"||state.phase=="battle");
            for(int i=0;i<concealmentRegionCount;i++){
                ConcealmentRegion region=concealmentRegions[i];float left=region.remaining-(time-region.syncedAt);
                region.root.gameObject.SetActive(visible&&(!region.smoke||left>0));if(!visible)continue;
                for(int n=0;n<region.parts.Length;n++){
                    if(!region.parts[n].gameObject.activeSelf)continue;
                    if(region.smoke){
                        float angle=n*2.399963f+time*.035f,rad=Mathf.Sqrt((n+.5f)/SmokePartsPerRegion)*region.radius*.78f;
                        region.parts[n].localPosition=new Vector3(Mathf.Cos(angle)*rad,.13f+Mathf.Sin(time*.65f+n)*.06f,Mathf.Sin(angle)*rad);
                        region.parts[n].localRotation=Quaternion.Euler(0,n*47+time*2,0);
                        region.parts[n].localScale=new Vector3(region.radius*.48f,.65f,region.radius*.34f);
                    }
                    Color color=region.smoke?new Color(.72f,.82f,.79f,.10f*Mathf.Clamp01(left/.6f)):new Color(.59f,.78f,.58f,.13f);
                    concealmentTint.SetColor("_Tint",color);region.renderers[n].SetPropertyBlock(concealmentTint);
                }
            }
            foreach(var pulse in discoveryPulses){
                float age=time-pulse.at;bool active=visible&&state.phase=="battle"&&age<.85f;
                pulse.root.gameObject.SetActive(active);if(!active)continue;
                float t=Mathf.Clamp01(age/.85f);pulse.ring.localScale=Vector3.one*(.8f+t*.9f);
                concealmentTint.SetColor("_Tint",new Color(.94f,.82f,.48f,(1-t)*.43f));pulse.ringRenderer.SetPropertyBlock(concealmentTint);
                pulse.eye.localPosition=Vector3.up*(pulse.height+Mathf.Sin(t*Mathf.PI)*.18f);
                pulse.eye.rotation=viewCamera.transform.rotation;pulse.eye.localScale=Vector3.one*(Mathf.Sin(t*Mathf.PI)*.25f+.85f);
            }
        }
        void HideDiscoveryPulses() {foreach(var pulse in discoveryPulses){pulse.at=-10;pulse.fighterId=null;pulse.root.gameObject.SetActive(false);}}
        void ResetConcealmentPresentation()
        {
            concealmentEnemies.Clear();concealmentSeen.Clear();concealmentBaseline=false;concealmentViewer=null;concealmentRound=-1;discoveryFeedbackCount=0;
            concealmentRegionCount=0;foreach(var region in concealmentRegions)region.root.gameObject.SetActive(false);HideDiscoveryPulses();
        }
    }
}
