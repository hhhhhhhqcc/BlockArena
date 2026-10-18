using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using BlockArena.Core;

namespace BlockArena.Presentation
{
    /// <summary>Native 3D arena. Gameplay coordinates are authoritative and mapped to XZ at 1/25 scale.</summary>
    public sealed class ArenaWorld : MonoBehaviour
    {
        const float WorldScale=1f/25f;
        static readonly Color[] Teams={VoxelAssets.Hex("#7FE2BE"),VoxelAssets.Hex("#EDBC72"),VoxelAssets.Hex("#B49AF0"),VoxelAssets.Hex("#F38E9C")};
        readonly Dictionary<string,VoxelCreature> creatures=new Dictionary<string,VoxelCreature>();
        readonly Dictionary<VoxelCreature,FighterState> fighterViews=new Dictionary<VoxelCreature,FighterState>();
        readonly Dictionary<string,Stack<VoxelCreature>> pools=new Dictionary<string,Stack<VoxelCreature>>();
        readonly List<string> stale=new List<string>();
        readonly Dictionary<string,float> seenEffects=new Dictionary<string,float>();
        readonly List<FlyingEffect> movingEffects=new List<FlyingEffect>();
        readonly Stack<FlyingEffect> effectPool=new Stack<FlyingEffect>();
        readonly RaycastHit[] pickHits=new RaycastHit[24];
        readonly Transform[] preparations=new Transform[4];
        Transform scenery,battleScenery,creatureRoot,effectRoot;Camera viewCamera;ParticleSystem sparks;
        GameState state;string playerId,selectedId,dragId;Point2? dragPosition;
        VoxelCreature hovered;
        bool initialized,solo,firstCamera=true;int localSlot;float nextEffectPrune;
        Vector3 cameraFocus,focusVelocity;float cameraSize,sizeVelocity;Quaternion cameraYaw=Quaternion.identity;
        float battleZoom=1,preparationZoom=1;
        Vector3 panOffset;
        const float CreatureSize=1.4f;
        public Camera ViewCamera { get { return viewCamera; } }
        public Func<string,Point2?> PlacementPosition;
        public VoxelCreature HoveredCreature { get { return hovered; } }
        sealed class FlyingEffect { public Transform transform;public MeshFilter filter;public Vector3 start,end;public float age,duration;public bool number; }

        public void Initialize()
        {
            if(initialized)return;initialized=true;
            QualitySettings.antiAliasing=4;QualitySettings.shadows=ShadowQuality.All;QualitySettings.shadowResolution=ShadowResolution.High;
            QualitySettings.shadowDistance=85;QualitySettings.shadowCascades=2;QualitySettings.pixelLightCount=3;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=VoxelAssets.Hex("#B6D4C1");RenderSettings.ambientEquatorColor=VoxelAssets.Hex("#89A88D");RenderSettings.ambientGroundColor=VoxelAssets.Hex("#4D6654");
            RenderSettings.fog=true;RenderSettings.fogColor=VoxelAssets.Hex("#263C32");RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogStartDistance=58;RenderSettings.fogEndDistance=125;
            GameObject cameraObject=new GameObject("Arena orthographic camera");cameraObject.transform.SetParent(transform,false);viewCamera=cameraObject.AddComponent<Camera>();viewCamera.orthographic=true;viewCamera.nearClipPlane=.1f;viewCamera.farClipPlane=180;viewCamera.clearFlags=CameraClearFlags.SolidColor;viewCamera.backgroundColor=VoxelAssets.Hex("#263C32");viewCamera.allowMSAA=true;viewCamera.allowHDR=false;cameraObject.tag="MainCamera";
            if(FindObjectOfType<AudioListener>()==null)cameraObject.AddComponent<AudioListener>();
            GameObject sunObject=new GameObject("Warm afternoon sun");sunObject.transform.SetParent(transform,false);Light sun=sunObject.AddComponent<Light>();sun.type=LightType.Directional;sun.color=VoxelAssets.Hex("#FFF1CB");sun.intensity=1.05f;sun.shadows=LightShadows.Soft;sun.shadowStrength=.62f;sun.shadowBias=.035f;sun.shadowNormalBias=.25f;sunObject.transform.rotation=Quaternion.Euler(48,-34,0);
            scenery=NewRoot("Arena scenery",transform);battleScenery=NewRoot("Octagonal arena",scenery);creatureRoot=NewRoot("Pooled creatures",transform);effectRoot=NewRoot("Pooled effects",transform);
            BuildLandscape();BuildArena();for(int slot=0;slot<4;slot++)BuildPreparation(slot);BuildParticles();BuildDemo();UpdateCamera(.016f,true);
        }
        static Transform NewRoot(string name,Transform parent){Transform t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        static Vector3 Position(float x,float y){return new Vector3(x*WorldScale,.06f,y*WorldScale);}
        static Vector3 Rotate(int slot,Vector3 local){return Quaternion.Euler(0,-90*slot,0)*local;}
        Transform MeshObject(string name,Transform parent,Action<VoxelMesh> build){VoxelMesh b=new VoxelMesh();build(b);return VoxelAssets.Part(parent,name,b.Finish(name),Vector3.zero);}

        void BuildLandscape()
        {
            MeshObject("Forest floor",scenery,b=>b.Box(new Vector3(0,-1.25f,0),new Vector3(180,.5f,180),VoxelAssets.Hex("#344C37")));
            MeshObject("Distant block pines",scenery,b=>{
                System.Random rng=new System.Random(719);
                for(int i=0;i<66;i++){
                    float angle=(float)rng.NextDouble()*Mathf.PI*2,radius=32+(float)rng.NextDouble()*30;
                    Vector3 p=new Vector3(Mathf.Cos(angle)*radius,-1,Mathf.Sin(angle)*radius);float h=2.6f+(float)rng.NextDouble()*3.8f;
                    b.Box(p+Vector3.up*(h*.35f),new Vector3(.38f,h*.7f,.38f),VoxelAssets.Hex("#66513B"));
                    for(int tier=0;tier<3;tier++){float size=h*(.54f-tier*.11f);b.Box(p+Vector3.up*(h*.55f+tier*h*.2f),new Vector3(size,h*.31f,size),VoxelAssets.Hex(tier==0?"#355C40":tier==1?"#46704A":"#598056"));}
                }
            });
        }
        void BuildArena()
        {
            MeshObject("Foundation and tiered octagonal stone",battleScenery,b=>{
                b.Disc(27,-1.1f,-.76f,8,VoxelAssets.Hex("#69766C"),VoxelAssets.Hex("#46564E"));
                b.Disc(25.5f,-.75f,-.38f,8,VoxelAssets.Hex("#818D7B"),VoxelAssets.Hex("#59695B"));
                b.Disc(24.1f,-.38f,-.05f,8,VoxelAssets.Hex("#647C50"),VoxelAssets.Hex("#465943"));
            });
            MeshObject("Meadow and pale stone crossing",battleScenery,b=>{
                System.Random rng=new System.Random(36);
                for(float x=-21;x<=21;x+=1.65f)for(float z=-21;z<=21;z+=1.65f){if(Mathf.Abs(x)+Mathf.Abs(z)>31.5f||Mathf.Abs(x)>22||Mathf.Abs(z)>22)continue;float tint=.89f+(float)rng.NextDouble()*.16f;Color c=VoxelAssets.Hex("#72905B")*tint;
                    b.Box(new Vector3(x,-.025f,z),new Vector3(1.65f,.05f,1.65f),c);
                }
                for(int i=-11;i<=11;i++){
                    if(Math.Abs(i)<2)continue;Color c=VoxelAssets.Hex(i%2==0?"#A4AA8B":"#969E82");
                    b.Box(new Vector3(i*1.7f,.012f,0),new Vector3(1.55f,.04f,1.4f),c);b.Box(new Vector3(0,.012f,i*1.7f),new Vector3(1.4f,.04f,1.55f),c);
                }
                b.Disc(2.6f,0,.035f,8,VoxelAssets.Hex("#9BA487"),VoxelAssets.Hex("#7A896E"));
            });
            MeshObject("Arena parapets",battleScenery,b=>{
                for(int i=0;i<72;i++){
                    float a=i*Mathf.PI*2/72;float cardinal=Mathf.Abs(Mathf.Sin(a*2));if(cardinal<.24f)continue;
                    Vector3 p=new Vector3(Mathf.Cos(a)*24.5f,.12f,Mathf.Sin(a)*24.5f);Quaternion q=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);
                    b.Box(p,new Vector3(.95f,.75f,.85f),VoxelAssets.Hex(i%3==0?"#7F8B7A":"#687664"),q);
                    if(i%3==0)b.Box(p+Vector3.up*.5f,new Vector3(1.04f,.28f,.94f),VoxelAssets.Hex("#A0A58B"),q);
                }
            });
        }
        void BuildPreparation(int slot)
        {
            Transform root=NewRoot("Team "+slot+" independent preparation terrace",scenery);preparations[slot]=root;root.localRotation=Quaternion.Euler(0,-slot*90,0);
            MeshObject("Terrace stone and grass",root,b=>{
                b.Box(new Vector3(0,-.41f,11.35f),new Vector3(17.4f,.72f,10.8f),VoxelAssets.Hex("#6D7C66"));
                b.Box(new Vector3(0,-.08f,11.35f),new Vector3(16.9f,.18f,10.3f),VoxelAssets.Hex("#91A779"));
                for(int x=-4;x<=4;x++)for(int z=0;z<5;z++){Color grass=VoxelAssets.Hex((x+z)%3==0?"#8DA871":"#809D64");b.Box(new Vector3(x*1.81f,.017f,7.45f+z*1.9f),new Vector3(1.81f,.05f,1.9f),grass);}
                b.Box(new Vector3(0,.048f,6.28f),new Vector3(16.75f,.075f,.21f),Teams[slot]);
                b.Box(new Vector3(-8.5f,.15f,11.4f),new Vector3(.3f,.45f,10.65f),VoxelAssets.Hex("#A5B08D"));b.Box(new Vector3(8.5f,.15f,11.4f),new Vector3(.3f,.45f,10.65f),VoxelAssets.Hex("#A5B08D"));
                b.Box(new Vector3(0,.12f,16.72f),new Vector3(17.2f,.4f,.25f),VoxelAssets.Hex("#ABB493"));
                for(int i=-3;i<=3;i++)b.Box(new Vector3(i*.9f,.065f,15.55f),new Vector3(.85f,.08f,1.25f),VoxelAssets.Hex("#AFB493"));
            });
            MeshObject("Team banners",root,b=>{
                for(int i=-1;i<=1;i+=2){float x=i*7.65f;b.Box(new Vector3(x,.87f,15.7f),new Vector3(.14f,1.75f,.14f),VoxelAssets.Hex("#705840"));b.Box(new Vector3(x+.32f,1.45f,15.7f),new Vector3(.7f,.59f,.075f),Teams[slot]);b.Box(new Vector3(x+.2f,1.14f,15.7f),new Vector3(.46f,.13f,.075f),Teams[slot]*.8f);}
            });
            Torch(root,new Vector3(-8.05f,0,6.5f));Torch(root,new Vector3(8.05f,0,6.5f));
        }
        void Torch(Transform parent,Vector3 at)
        {
            Transform root=NewRoot("Torch",parent);root.localPosition=at;
            MeshObject("Stone torch pedestal",root,b=>{b.Box(new Vector3(0,.15f,0),new Vector3(.6f,.3f,.6f),VoxelAssets.Hex("#A2A58C"));b.Box(new Vector3(0,.7f,0),new Vector3(.19f,1.1f,.19f),VoxelAssets.Hex("#624D37"));b.Box(new Vector3(0,1.25f,0),new Vector3(.38f,.18f,.38f),VoxelAssets.Hex("#524B3C"));});
            VoxelMesh flame=new VoxelMesh();flame.Box(new Vector3(0,1.48f,0),new Vector3(.25f,.4f,.25f),VoxelAssets.Hex("#F5B85B"));flame.Box(new Vector3(.035f,1.65f,0),new Vector3(.14f,.24f,.14f),VoxelAssets.Hex("#FFE4A0"));VoxelAssets.Part(root,"Embers",flame.Finish("Torch ember"),Vector3.zero,VoxelAssets.Glow,false);
            GameObject lamp=new GameObject("Torch glow");lamp.transform.SetParent(root,false);lamp.transform.localPosition=new Vector3(0,1.7f,0);Light point=lamp.AddComponent<Light>();point.type=LightType.Point;point.color=VoxelAssets.Hex("#FFC47A");point.range=4;point.intensity=.65f;point.shadows=LightShadows.None;
        }
        void BuildParticles()
        {
            GameObject g=new GameObject("Shared voxel particles");g.transform.SetParent(effectRoot,false);sparks=g.AddComponent<ParticleSystem>();ParticleSystem.MainModule main=sparks.main;main.loop=false;main.playOnAwake=false;main.startLifetime=.5f;main.startSize=.11f;main.startSpeed=0;main.gravityModifier=1.3f;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=600;
            ParticleSystem.EmissionModule emission=sparks.emission;emission.enabled=false;ParticleSystem.ShapeModule shape=sparks.shape;shape.enabled=false;
            ParticleSystemRenderer renderer=g.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;renderer.mesh=VoxelAssets.Block(Color.white);renderer.sharedMaterial=VoxelAssets.Glow;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        void BuildDemo()
        {
            string[] kinds={"zombie","skeleton","spider","creeper","slime","golem"};
            for(int slot=0;slot<4;slot++){
                for(int i=0;i<6;i++){Vector3 at=Rotate(slot,new Vector3((i%3-1)*2.3f,.06f,9.3f+i/3*2.6f));VoxelCreature v=Acquire("demo-"+slot+"-"+i,kinds[i],slot,at);v.Battle=false;}
                Acquire("demo-steve-"+slot,"steve",slot,Rotate(slot,new Vector3(0,.06f,15.7f)));
            }
        }
        public void SetState(GameState next,string viewerId)
        {
            if(!initialized)Initialize();state=next;playerId=viewerId;
            if(next==null){Clear();return;}
            PlayerState local=null;for(int i=0;i<next.players.Count;i++)if(next.players[i].id==viewerId){local=next.players[i];break;}
            if(local!=null)localSlot=local.slot;
            bool nextSolo=local!=null&&!local.eliminated&&(next.phase=="prep"||next.phase=="recruit");
            bool battle=next.phase=="battle"||next.phase=="results";
            if(nextSolo!=solo){ReleaseAll();ClearEffects();panOffset=Vector3.zero;}solo=nextSolo;
            battleScenery.gameObject.SetActive(!solo);for(int i=0;i<4;i++)preparations[i].gameObject.SetActive(!solo||i==localSlot);
            fighterViews.Clear();
            foreach(VoxelCreature v in creatures.Values)v.Seen=false;
            for(int i=0;i<next.players.Count;i++){
                PlayerState p=next.players[i];if(solo&&p.id!=viewerId)continue;
                VoxelCreature steve=Get("steve:"+p.id,"steve",p.slot,Position(p.steve.x,p.steve.y));steve.Battle=false;steve.Seen=true;steve.Dead=false;steve.Size=1;
                if(!battle){for(int u=0;u<p.units.Count;u++){UnitState unit=p.units[u];VoxelCreature v=Get("unit:"+unit.id,unit.kind,p.slot,Position(unit.x,unit.y));v.UnitId=unit.id;v.Battle=false;v.Dead=false;v.Size=CreatureSize*CreatureCatalog.Get(unit.kind).visualScale;v.Seen=true;}}
            }
            if(battle){for(int i=0;i<next.fighters.Count;i++){FighterState f=next.fighters[i];VoxelCreature v=Get("fighter:"+f.id,f.kind,f.slot,Position(f.x,f.y));v.UnitId=f.unitId;v.Battle=true;v.Health=f.hp;v.MaximumHealth=f.maxHp;v.Attack=f.attackTime;v.Size=Mathf.Max(.3f,f.scale)*CreatureSize*CreatureCatalog.Get(f.kind).visualScale;v.Dead=f.dead;v.Seen=true;fighterViews[v]=f;}}
            stale.Clear();foreach(KeyValuePair<string,VoxelCreature> item in creatures)if(!item.Value.Seen)stale.Add(item.Key);for(int i=0;i<stale.Count;i++)Release(stale[i]);
            if(next.effects!=null)for(int i=0;i<next.effects.Count;i++)ProcessEffect(next.effects[i]);
        }
        VoxelCreature Get(string key,string kind,int slot,Vector3 at){VoxelCreature v;if(!creatures.TryGetValue(key,out v))v=Acquire(key,kind,slot,at);v.Target=at;v.Seen=true;return v;}
        VoxelCreature Acquire(string key,string kind,int slot,Vector3 at)
        {
            Stack<VoxelCreature> pool;if(!pools.TryGetValue(kind,out pool)){pool=new Stack<VoxelCreature>();pools.Add(kind,pool);}VoxelCreature v;
            if(pool.Count>0)v=pool.Pop();else{GameObject g=new GameObject(kind);g.transform.SetParent(creatureRoot,false);v=g.AddComponent<VoxelCreature>();v.Build(kind);}
            v.ResetVisual(key,slot,at);creatures[key]=v;return v;
        }
        void Release(string key){VoxelCreature v;if(!creatures.TryGetValue(key,out v))return;if(hovered==v)SetHovered(null);creatures.Remove(key);fighterViews.Remove(v);v.gameObject.SetActive(false);pools[v.Kind].Push(v);}
        void ReleaseAll(){SetHovered(null);foreach(VoxelCreature v in creatures.Values){v.gameObject.SetActive(false);pools[v.Kind].Push(v);}creatures.Clear();fighterViews.Clear();}
        public void Clear(){if(!initialized)return;ReleaseAll();ClearEffects();state=null;selectedId=dragId=null;dragPosition=null;solo=false;localSlot=0;battleScenery.gameObject.SetActive(true);for(int i=0;i<4;i++)preparations[i].gameObject.SetActive(true);BuildDemo();}
        public bool TryPickUnit(Vector2 screen,out string unitId)
        {
            unitId=null;if(viewCamera==null||state==null)return false;Ray ray=viewCamera.ScreenPointToRay(screen);int count=Physics.RaycastNonAlloc(ray,pickHits,180,1<<8);float nearest=float.MaxValue;
            for(int i=0;i<count;i++){VoxelCreature v=pickHits[i].collider.GetComponent<VoxelCreature>();if(v!=null&&v.Slot==localSlot&&v.Kind!="steve"&&!v.Dead&&pickHits[i].distance<nearest){unitId=v.UnitId;nearest=pickHits[i].distance;}}
            return unitId!=null;
        }
        public bool TryGround(Vector2 screen,out Point2 world)
        {
            world=new Point2();if(viewCamera==null)return false;Ray ray=viewCamera.ScreenPointToRay(screen);float distance;if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out distance))return false;Vector3 p=ray.GetPoint(distance);world=new Point2(p.x/WorldScale,p.z/WorldScale);return true;
        }
        public bool TryGetUnitPosition(string unitId,out Point2 position)
        {
            VoxelCreature visual;
            if(creatures.TryGetValue("unit:"+unitId,out visual)) {position=new Point2(visual.transform.position.x/WorldScale,visual.transform.position.z/WorldScale);return true;}
            position=new Point2();return false;
        }
        public bool TryInspectCreature(Vector2 screen,out VoxelCreature visual,out FighterState fighter)
        {
            visual=null;fighter=null;if(viewCamera==null||state==null)return false;
            int count=Physics.RaycastNonAlloc(viewCamera.ScreenPointToRay(screen),pickHits,180,1<<8);float nearest=float.MaxValue;
            for(int i=0;i<count;i++){
                VoxelCreature v=pickHits[i].collider.GetComponent<VoxelCreature>();
                if(v!=null&&v.Kind!="steve"&&!v.Dead&&pickHits[i].distance<nearest){visual=v;nearest=pickHits[i].distance;}
            }
            if(visual==null)return false;fighterViews.TryGetValue(visual,out fighter);return true;
        }
        public void SetHovered(VoxelCreature visual)
        {
            if(hovered==visual)return;if(hovered!=null)hovered.SetHovered(false);hovered=visual;if(hovered!=null)hovered.SetHovered(true);
        }
        public void SetSelectedUnit(string unitId){selectedId=unitId;}
        public void SetDrag(string unitId,Point2? world){dragId=unitId;dragPosition=world;}
        public Vector2 LocalMovementToWorld(Vector2 movement){Vector3 v=Rotate(localSlot,new Vector3(movement.x,0,movement.y));return new Vector2(v.x,v.z);}
        public float ZoomFactor { get { return solo?preparationZoom:battleZoom; } }
        public void Zoom(float wheel,Vector2 screen)
        {
            if(viewCamera==null||Mathf.Abs(wheel)<.001f)return;
            float before=ZoomFactor,after=Mathf.Clamp(before*Mathf.Exp(-wheel*.12f),.48f,1.35f);
            Point2 anchor;if(TryGround(screen,out anchor))panOffset+=(Position(anchor.x,anchor.y)-cameraFocus)*(1-after/before);
            if(solo)preparationZoom=after;else battleZoom=after;
            ClampPan();
        }
        public void Pan(Vector2 delta)
        {
            if(viewCamera==null)return;
            Vector3 up=viewCamera.transform.up;up.y=0;up.Normalize();
            panOffset-= (viewCamera.transform.right*delta.x+up*delta.y)*cameraSize*.035f;
            ClampPan();
        }
        void ClampPan(){float limit=solo?6:17;panOffset.x=Mathf.Clamp(panOffset.x,-limit,limit);panOffset.z=Mathf.Clamp(panOffset.z,-limit,limit);panOffset.y=0;}
        public void ResetView(){battleZoom=preparationZoom=1;panOffset=Vector3.zero;}
        void Update()
        {
            if(!initialized)return;float dt=Mathf.Clamp(Time.unscaledDeltaTime,.001f,.08f),time=Time.unscaledTime;UpdateCamera(dt,false);
            foreach(VoxelCreature v in creatures.Values){
                Vector3? drag=null;
                if(!v.Battle&&v.Kind!="steve"){
                    Point2? preview=PlacementPosition==null?null:PlacementPosition(v.UnitId);
                    if(preview.HasValue)drag=Position(preview.Value.x,preview.Value.y);
                    if(v.UnitId==dragId&&dragPosition.HasValue)drag=Position(dragPosition.Value.x,dragPosition.Value.y);
                }
                v.Step(dt,time,viewCamera,v.UnitId==selectedId,drag);
            }
            for(int i=movingEffects.Count-1;i>=0;i--){FlyingEffect effect=movingEffects[i];effect.age+=dt;float t=effect.age/effect.duration;if(t>=1){effect.transform.gameObject.SetActive(false);effectPool.Push(effect);movingEffects.RemoveAt(i);continue;}
                if(effect.number){effect.transform.position=effect.start+Vector3.up*(t*1.25f);effect.transform.rotation=viewCamera.transform.rotation;effect.transform.localScale=Vector3.one*(1+Mathf.Sin(t*Mathf.PI)*.1f);}
                else{effect.transform.position=Vector3.Lerp(effect.start,effect.end,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*.4f;Vector3 direction=effect.end-effect.start;if(direction.sqrMagnitude>.001f)effect.transform.rotation=Quaternion.LookRotation(direction);}
            }
            if(time>nextEffectPrune){nextEffectPrune=time+4;stale.Clear();foreach(KeyValuePair<string,float> item in seenEffects)if(time-item.Value>8)stale.Add(item.Key);for(int i=0;i<stale.Count;i++)seenEffects.Remove(stale[i]);}
        }
        void UpdateCamera(float dt,bool immediate)
        {
            if(viewCamera==null)return;float aspect=Mathf.Max(.35f,(float)Mathf.Max(1,Screen.width)/Mathf.Max(1,Screen.height));
            Vector3 desiredFocus=(solo?Rotate(localSlot,new Vector3(0,.35f,11.4f)):new Vector3(0,.2f,0))+panOffset;
            float padding=Mathf.Clamp((float)Screen.height/Mathf.Max(260,Screen.height-145),1.08f,1.32f);
            float desiredSize=(solo?Mathf.Max(5.8f,9.5f/aspect):Mathf.Max(16f,24f/aspect))*padding*ZoomFactor;
            Quaternion desiredYaw=Quaternion.Euler(0,-90*localSlot,0);
            if(firstCamera||immediate){cameraFocus=desiredFocus;cameraSize=desiredSize;cameraYaw=desiredYaw;firstCamera=false;}
            else{cameraFocus=Vector3.SmoothDamp(cameraFocus,desiredFocus,ref focusVelocity,.34f,120,dt);cameraSize=Mathf.SmoothDamp(cameraSize,desiredSize,ref sizeVelocity,.34f,100,dt);cameraYaw=Quaternion.Slerp(cameraYaw,desiredYaw,1-Mathf.Exp(-dt*5));}
            viewCamera.orthographicSize=cameraSize;Vector3 offset=cameraYaw*new Vector3(0,31,35);viewCamera.transform.position=cameraFocus+offset;viewCamera.transform.rotation=Quaternion.LookRotation(cameraFocus-viewCamera.transform.position,Vector3.up);
        }
        void ProcessEffect(EffectState effect)
        {
            if(effect==null||string.IsNullOrEmpty(effect.id)||effect.age>.65f||seenEffects.ContainsKey(effect.id))return;seenEffects[effect.id]=Time.unscaledTime;
            Vector3 at=Position(effect.x,effect.y)+Vector3.up*.6f;
            if(effect.type=="arrow"){FlyingEffect flying=EffectObject();flying.start=at+Vector3.up*.3f;flying.end=Position(effect.targetX,effect.targetY)+Vector3.up*.65f;flying.duration=.19f;flying.number=false;flying.filter.sharedMesh=VoxelAssets.Cached("arrow",b=>{b.Box(Vector3.zero,new Vector3(.045f,.045f,.65f),VoxelAssets.Hex("#DCC797"));b.Box(new Vector3(0,0,.34f),new Vector3(.12f,.1f,.13f),VoxelAssets.Hex("#B8C7C5"));});flying.transform.localScale=Vector3.one;return;}
            int count=effect.type=="explode"?34:effect.type=="death"?14:effect.type=="heal"?6:5;
            Color color=effect.type=="explode"?VoxelAssets.Hex("#F7BF72"):effect.type=="heal"?VoxelAssets.Hex("#A3E7A3"):effect.type=="death"?Teams[Mathf.Clamp(effect.slot,0,3)]:VoxelAssets.Hex("#E8D8AB");
            for(int i=0;i<count;i++){ParticleSystem.EmitParams emit=new ParticleSystem.EmitParams();emit.position=at;Vector3 velocity=UnityEngine.Random.insideUnitSphere*(effect.type=="explode"?5:2);velocity.y=Mathf.Abs(velocity.y)+1;emit.velocity=velocity;emit.startColor=color;emit.startSize=effect.type=="explode"? 0.19f: 0.1f;emit.startLifetime=effect.type=="explode"? 0.65f: 0.35f;sparks.Emit(emit,1);}
            if(effect.type=="hit"&&effect.value>0){FlyingEffect number=EffectObject();number.start=at+Vector3.up*.6f;number.end=number.start;number.duration=.72f;number.number=true;number.filter.sharedMesh=VoxelAssets.DamageNumber(Mathf.RoundToInt(effect.value));number.transform.rotation=viewCamera.transform.rotation;number.transform.localScale=Vector3.one;}
        }
        FlyingEffect EffectObject()
        {
            FlyingEffect effect;if(effectPool.Count>0)effect=effectPool.Pop();else{GameObject g=new GameObject("Pooled combat effect");g.transform.SetParent(effectRoot,false);effect=new FlyingEffect{transform=g.transform,filter=g.AddComponent<MeshFilter>()};MeshRenderer r=g.AddComponent<MeshRenderer>();r.sharedMaterial=VoxelAssets.Glow;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;}
            effect.age=0;effect.transform.gameObject.SetActive(true);movingEffects.Add(effect);return effect;
        }
        void ClearEffects(){seenEffects.Clear();for(int i=0;i<movingEffects.Count;i++){movingEffects[i].transform.gameObject.SetActive(false);effectPool.Push(movingEffects[i]);}movingEffects.Clear();if(sparks!=null)sparks.Clear();}
    }
}
