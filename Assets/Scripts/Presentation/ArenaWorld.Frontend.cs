using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        // Four retained dioramas share the game's voxel meshes/materials. None of
        // their geometry is registered with gameplay or with the picking layers.
        sealed class FrontScene
        {
            internal Transform root;
            internal readonly List<FrontMotion> motions=new List<FrontMotion>();
            internal readonly List<FrontMote> motes=new List<FrontMote>();
            internal Light accent;
            internal Vector3 eye,focus,subject;
            internal Vector2 viewportTarget=new Vector2(.74f,.50f);
            internal Color sky,fog,ambient;
            internal float fov;
        }
        sealed class FrontMotion
        {
            internal Transform root;internal Vector3 at,scale;internal Quaternion rotation;
            internal float phase,speed,bob,spin;internal int kind;
        }
        sealed class FrontMote { internal Transform root;internal Vector3 at;internal float phase,speed; }
        readonly Dictionary<string,FrontScene> frontScenes=new Dictionary<string,FrontScene>();
        Transform frontendRoot;
        Light frontendSun,gameplaySun;
        FrontScene frontScene;
        string frontendMode;
        bool frontSaved,frontSceneryActive,frontCreaturesActive,frontEffectsActive,frontSunEnabled;
        bool frontFog,frontCameraOrthographic;
        FogMode frontFogMode;
        AmbientMode frontAmbientMode;
        Color frontSky,frontEquator,frontGround,frontFogColor,frontCameraBackground;
        float frontFogStart,frontFogEnd,frontCameraFov,frontCameraSize;
        Vector3 frontCameraPosition;
        Quaternion frontCameraRotation;
        // App owns this opt-in while its full settings page covers a live match.
        // Direct world consumers keep the original SetState transition behavior.
        public bool KeepFrontendForStateUpdates { get; set; }
        public string FrontendMode { get { return frontendMode; } }
        public bool FrontendVisible { get { return frontendMode!=null&&frontendRoot!=null&&frontendRoot.gameObject.activeInHierarchy; } }
        public int FrontendRootCount { get { return frontendRoot==null?0:1; } }
        public int FrontendSceneCount { get { return frontScenes.Count; } }
        public int FrontendActiveSceneCount
        {
            get { int count=0;foreach(var pair in frontScenes)if(pair.Value.root.gameObject.activeInHierarchy)count++;return count; }
        }
        public int FrontendMeshObjectCount { get { return frontendRoot==null?0:frontendRoot.GetComponentsInChildren<MeshFilter>(true).Length; } }
        public bool GameplayWorldVisible { get { return scenery!=null&&scenery.gameObject.activeInHierarchy; } }
        public Vector2 FrontendSubjectViewportTarget { get { return frontScene==null?new Vector2(.74f,.50f):frontScene.viewportTarget; } }
        public Vector3 FrontendSubjectWorld { get { return frontScene==null?Vector3.zero:frontScene.root.TransformPoint(frontScene.subject); } }
        public Vector3 FrontendSubjectViewport { get { return frontScene==null||viewCamera==null?Vector3.zero:viewCamera.WorldToViewportPoint(FrontendSubjectWorld); } }

        public void SetFrontend(string mode)
        {
            if(mode=="lobby_host")mode="host";else if(mode=="lobby_guest")mode="join";
            if(mode!="main"&&mode!="host"&&mode!="join"&&mode!="settings")mode=null;
            if(!initialized)Initialize();
            // The dedicated server never creates a world. This also makes an
            // explicitly created graphics-null world avoid menu GPU resources.
            if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null)mode=null;
            if(mode==frontendMode)return;
            if(mode==null){
                if(frontendRoot!=null)frontendRoot.gameObject.SetActive(false);
                frontendMode=null;frontScene=null;
                if(frontSaved){
                    scenery.gameObject.SetActive(frontSceneryActive);creatureRoot.gameObject.SetActive(frontCreaturesActive);effectRoot.gameObject.SetActive(frontEffectsActive);
                    if(gameplaySun!=null)gameplaySun.enabled=frontSunEnabled;
                    RestoreFrontendGameplayRendering();
                    frontSaved=false;firstCamera=true;UpdateCamera(.016f,true);
                }
                return;
            }
            EnsureFrontend();
            if(!frontSaved){
                frontSceneryActive=scenery.gameObject.activeSelf;frontCreaturesActive=creatureRoot.gameObject.activeSelf;frontEffectsActive=effectRoot.gameObject.activeSelf;
                Transform sun=transform.Find("Warm afternoon sun");gameplaySun=sun==null?null:sun.GetComponent<Light>();frontSunEnabled=gameplaySun!=null&&gameplaySun.enabled;
                SaveFrontendGameplayRendering();frontSaved=true;
            }
            scenery.gameObject.SetActive(false);creatureRoot.gameObject.SetActive(false);effectRoot.gameObject.SetActive(false);if(gameplaySun!=null)gameplaySun.enabled=false;
            SetHovered(null);ResetTargetingPresentation();
            frontendMode=mode;frontScene=frontScenes[mode];frontendRoot.gameObject.SetActive(true);
            foreach(var pair in frontScenes)pair.Value.root.gameObject.SetActive(pair.Key==mode);
            ApplyFrontendRendering();
            UpdateFrontend(Time.unscaledTime);
        }
        void SaveFrontendGameplayRendering()
        {
            frontAmbientMode=RenderSettings.ambientMode;frontSky=RenderSettings.ambientSkyColor;frontEquator=RenderSettings.ambientEquatorColor;frontGround=RenderSettings.ambientGroundColor;
            frontFog=RenderSettings.fog;frontFogMode=RenderSettings.fogMode;frontFogColor=RenderSettings.fogColor;frontFogStart=RenderSettings.fogStartDistance;frontFogEnd=RenderSettings.fogEndDistance;
            frontCameraOrthographic=viewCamera.orthographic;frontCameraFov=viewCamera.fieldOfView;frontCameraSize=viewCamera.orthographicSize;frontCameraBackground=viewCamera.backgroundColor;
            frontCameraPosition=viewCamera.transform.position;frontCameraRotation=viewCamera.transform.rotation;
        }
        void RestoreFrontendGameplayRendering()
        {
            RenderSettings.ambientMode=frontAmbientMode;RenderSettings.ambientSkyColor=frontSky;RenderSettings.ambientEquatorColor=frontEquator;RenderSettings.ambientGroundColor=frontGround;
            RenderSettings.fog=frontFog;RenderSettings.fogMode=frontFogMode;RenderSettings.fogColor=frontFogColor;RenderSettings.fogStartDistance=frontFogStart;RenderSettings.fogEndDistance=frontFogEnd;
            viewCamera.orthographic=frontCameraOrthographic;viewCamera.fieldOfView=frontCameraFov;viewCamera.orthographicSize=frontCameraSize;viewCamera.backgroundColor=frontCameraBackground;
            viewCamera.transform.SetPositionAndRotation(frontCameraPosition,frontCameraRotation);
        }
        void ApplyFrontendRendering()
        {
            if(frontScene==null)return;
            RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=frontScene.ambient;
            RenderSettings.ambientEquatorColor=frontScene.ambient*.61f;RenderSettings.ambientGroundColor=frontScene.ambient*.29f;
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;RenderSettings.fogColor=frontScene.fog;RenderSettings.fogStartDistance=28;RenderSettings.fogEndDistance=82;
            frontendSun.color=frontendMode=="host"||frontendMode=="settings"?VoxelAssets.Hex("#FFE1AD"):VoxelAssets.Hex("#BCD7F0");
            frontendSun.intensity=frontendMode=="host"?.83f:frontendMode=="settings"?.76f:frontendMode=="main"?.82f:.68f;
            viewCamera.orthographic=false;viewCamera.backgroundColor=frontScene.sky;
        }
        bool BeginSettingsGameplayUpdate()
        {
            // Keep live weather, effects and the absolute judgment timeline moving
            // under settings without activating their roots or changing page mode.
            if(frontendMode!="settings"||!frontSaved)return false;
            RestoreFrontendGameplayRendering();return true;
        }
        void EndSettingsGameplayUpdate()
        {
            if(frontendMode!="settings"||!frontSaved)return;
            SaveFrontendGameplayRendering();ApplyFrontendRendering();UpdateFrontend(Time.unscaledTime);
        }
        void EnsureFrontend()
        {
            if(frontendRoot!=null)return;
            frontendRoot=NewRoot("Block Arena dedicated front-end dioramas",transform);
            GameObject lamp=new GameObject("Front-end moon and camp key light");lamp.transform.SetParent(frontendRoot,false);
            frontendSun=lamp.AddComponent<Light>();frontendSun.type=LightType.Directional;frontendSun.shadows=LightShadows.Soft;frontendSun.shadowStrength=.6f;
            frontendSun.shadowBias=.035f;frontendSun.shadowNormalBias=.2f;lamp.transform.localRotation=Quaternion.Euler(43,-38,0);
            BuildFrontMain();BuildFrontHost();BuildFrontJoin();BuildFrontSettings();frontendRoot.gameObject.SetActive(false);
        }
        FrontScene NewFrontScene(string mode,Color sky,Color ambient,Vector3 eye,Vector3 focus,float fov)
        {
            var scene=new FrontScene{root=NewRoot(mode+" dedicated diorama",frontendRoot),sky=sky,fog=sky,ambient=ambient,eye=eye,focus=focus,fov=fov};
            frontScenes.Add(mode,scene);scene.root.gameObject.SetActive(false);return scene;
        }
        Transform FrontPart(Transform parent,string key,System.Action<VoxelMesh> build,Vector3 at,bool glow=false,bool shadow=true)
        {return VoxelAssets.Part(parent,key,VoxelAssets.Cached("front094/"+key,build),at,glow?VoxelAssets.Glow:null,shadow);}
        void FrontMove(FrontScene scene,Transform root,float bob,float spin,float phase=0,int kind=0)
        {scene.motions.Add(new FrontMotion{root=root,at=root.localPosition,scale=root.localScale,rotation=root.localRotation,bob=bob,spin=spin,speed=.75f,phase=phase,kind=kind});}
        Light FrontLight(Transform parent,string name,Vector3 at,Color color,float intensity,float range)
        {
            GameObject g=new GameObject(name);g.transform.SetParent(parent,false);g.transform.localPosition=at;
            Light light=g.AddComponent<Light>();light.type=LightType.Point;light.color=color;light.intensity=intensity;light.range=range;light.shadows=LightShadows.None;return light;
        }
        void FrontStars(FrontScene scene,string key,Color tint)
        {
            Transform stars=FrontPart(scene.root,key,b=>{
                var random=new System.Random(94+key.Length);
                for(int i=0;i<56;i++){
                    Vector3 at=new Vector3((float)random.NextDouble()*42-8,(float)random.NextDouble()*22+2,-12-(float)random.NextDouble()*34);
                    float size=.035f+(float)random.NextDouble()*.055f;b.Box(at,Vector3.one*size,tint);
                }
            },Vector3.zero,true,false);
            FrontMove(scene,stars,.025f,.08f);
            FrontPart(scene.root,key+" distant floating islands",b=>{
                for(int i=0;i<5;i++){
                    Vector3 at=new Vector3(3+i*5,-5-i%2*3,-18-i*7);Color rock=VoxelAssets.Hex("#213C43");
                    b.Box(at,new Vector3(4+i%2*2,.7f,3),rock);b.Box(at+Vector3.down*.8f,new Vector3(2.8f,1.2f,2.1f),rock*.75f);
                    b.Box(at+new Vector3(.2f,1.2f,-.3f),new Vector3(.6f,2.2f,.6f),rock*1.12f);
                }
            },Vector3.zero);
        }
        void FrontMotes(FrontScene scene,int count,Color color)
        {
            Mesh mesh=VoxelAssets.Block(color);
            for(int i=0;i<count;i++){
                Vector3 at=new Vector3(4+(i*13%17)*.36f,.9f+(i%5)*.4f,(i*7%13)*.6f-3);
                Transform mote=VoxelAssets.Part(scene.root,"Retained ambient mote "+i,mesh,at,VoxelAssets.Glow,false);
                mote.localScale=Vector3.one*(.035f+(i%3)*.015f);
                scene.motes.Add(new FrontMote{root=mote,at=at,phase=i*.618f,speed=.11f+i%4*.018f});
            }
        }

        void BuildFrontMain()
        {
            FrontScene scene=NewFrontScene("main",VoxelAssets.Hex("#101F2B"),VoxelAssets.Hex("#A6C4C8"),new Vector3(14,12.4f,25),new Vector3(-4,2.4f,0),38);
            scene.subject=new Vector3(7,1.8f,0);
            FrontStars(scene,"Ancient arena star veil",VoxelAssets.Hex("#91BDC6"));
            Transform island=NewRoot("Ancient floating dueling sanctuary",scene.root);island.localPosition=new Vector3(7,0,0);
            FrontPart(island,"Floating sanctuary layered foundation",b=>{
                b.Disc(7.7f,-.75f,-.2f,8,VoxelAssets.Hex("#455C61"),VoxelAssets.Hex("#293C46"));
                b.Disc(7.1f,-.2f,.3f,8,VoxelAssets.Hex("#71817A"),VoxelAssets.Hex("#46575C"));
                b.Disc(6.65f,.3f,.58f,8,VoxelAssets.Hex("#87938A"),VoxelAssets.Hex("#5D706B"));
                for(int i=0;i<22;i++){
                    float a=i*Mathf.PI*2/22,r=4.3f+(i%3)*1.05f;Vector3 at=new Vector3(Mathf.Cos(a)*r,-1.1f-(i%4)*.3f,Mathf.Sin(a)*r);
                    b.Box(at,new Vector3(1.15f,1.7f+(i%3)*.45f,1.25f),VoxelAssets.Hex(i%2==0?"#344D55":"#3E5860"),Quaternion.Euler(0,i*31,0));
                }
                for(int i=0;i<30;i++){
                    float a=i*Mathf.PI/15;Vector3 at=new Vector3(Mathf.Cos(a)*6.65f,.87f,Mathf.Sin(a)*6.65f);
                    b.Box(at,new Vector3(.62f,i%4==0?1.03f:.43f,.66f),VoxelAssets.Hex(i%3==0?"#98A194":"#63797A"),Quaternion.Euler(0,-a*Mathf.Rad2Deg,0));
                }
                for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++){
                    if(x*x+z*z>21)continue;
                    b.Box(new Vector3(x*1.1f,.605f,z*1.1f),new Vector3(1.04f,.065f,1.04f),VoxelAssets.Hex((x+z)%3==0?"#81968B":"#748C84"));
                }
                b.Disc(2.15f,.63f,.8f,8,VoxelAssets.Hex("#3C4B58"),VoxelAssets.Hex("#283340"));
                b.Disc(1.55f,.8f,1.12f,8,VoxelAssets.Hex("#455264"),VoxelAssets.Hex("#29313F"));
            },Vector3.zero);
            FrontPart(island,"Sanctuary floor runes",b=>{
                b.Ring(2.45f,2.4f,VoxelAssets.Hex("#7CE3CB"));
                for(int i=0;i<12;i++){
                    float a=i*Mathf.PI/6;Vector3 at=new Vector3(Mathf.Cos(a)*3.6f,0,Mathf.Sin(a)*3.6f);
                    b.Box(at,new Vector3(.12f,.035f,.5f),VoxelAssets.Hex(i%3==0?"#C4A4F3":"#8CBEB6"),Quaternion.Euler(0,-a*Mathf.Rad2Deg,0));
                }
            },new Vector3(0,.715f,0),true,false);
            FrontPart(island,"Broken ceremonial rear arch",b=>{
                for(int side=-1;side<=1;side+=2){
                    Vector3 at=new Vector3(side*4.55f,1.2f,-3.5f);
                    b.Box(at,new Vector3(1.3f,.55f,1.3f),VoxelAssets.Hex("#96A497"));
                    b.Box(at+Vector3.up*1.7f,new Vector3(.65f,3.2f,.75f),VoxelAssets.Hex("#627A7B"));
                    b.Box(at+Vector3.up*3.4f,new Vector3(1.25f,.45f,1.1f),VoxelAssets.Hex("#8E9D93"));
                    b.Box(at+new Vector3(-side*.7f,3.7f,0),new Vector3(1.15f,.55f,.9f),VoxelAssets.Hex("#627A7B"),Quaternion.Euler(0,0,side*12));
                }
            },Vector3.zero);
            FrontStatue(island,"Sentinel of iron",new Vector3(-4.7f,.72f,-.8f),1.2f,0);
            FrontStatue(island,"Sentinel of bone",new Vector3(4.8f,.72f,-.7f),1.2f,1);
            FrontStatue(island,"Sentinel of the wild",new Vector3(-3.3f,.72f,4.1f),.75f,2);
            FrontStatue(island,"Sentinel of shadows",new Vector3(3.4f,.72f,4.1f),.75f,3);
            Transform crystal=FrontPart(island,"Floating caged end crystal",b=>{
                Color core=VoxelAssets.Hex("#C5A1F5"),edge=VoxelAssets.Hex("#66578B");
                b.Box(Vector3.zero,Vector3.one*1.18f,core,Quaternion.Euler(35,45,15));
                b.Box(Vector3.zero,Vector3.one*.64f,VoxelAssets.Hex("#F2E1FC"),Quaternion.Euler(35,45,15));
                for(int i=0;i<4;i++){
                    Quaternion q=Quaternion.Euler(0,i*90,0);b.Box(q*new Vector3(.86f,0,.86f),new Vector3(.1f,2.45f,.1f),edge);
                }
                b.Box(new Vector3(0,1.2f,0),new Vector3(1.9f,.095f,1.9f),edge,Quaternion.Euler(0,45,0));
                b.Box(new Vector3(0,-1.2f,0),new Vector3(1.9f,.095f,1.9f),edge,Quaternion.Euler(0,45,0));
            },new Vector3(0,3.45f,0),true);
            FrontMove(scene,crystal,.19f,10);
            Transform satellites=FrontPart(island,"Orbiting crystal fragments",b=>{
                for(int i=0;i<8;i++){float a=i*Mathf.PI/4;b.Box(new Vector3(Mathf.Cos(a)*2.2f,(i%2)*.2f,Mathf.Sin(a)*2.2f),Vector3.one*.17f,VoxelAssets.Hex(i%2==0?"#83D9C9":"#C2A4F0"),Quaternion.Euler(30,i*45,25));}
            },new Vector3(0,2.2f,0),true,false);FrontMove(scene,satellites,.08f,-8,1);
            scene.accent=FrontLight(island,"Crystal violet bounce",new Vector3(0,3,0),VoxelAssets.Hex("#BA9CEB"),2.2f,9);
            FrontMotes(scene,12,VoxelAssets.Hex("#A5D5D5"));
        }
        void FrontStatue(Transform parent,string name,Vector3 at,float scale,int kind)
        {
            Transform statue=FrontPart(parent,name,b=>{
                Color stone=VoxelAssets.Hex(kind%2==0?"#748780":"#62777D"),edge=VoxelAssets.Hex("#A0AB9B");
                b.Box(new Vector3(0,.15f,0),new Vector3(1.55f,.3f,1.45f),edge);b.Box(new Vector3(0,.45f,0),new Vector3(1.1f,.34f,1.05f),stone*.75f);
                for(int side=-1;side<=1;side+=2){b.Box(new Vector3(side*.24f,1.1f,0),new Vector3(.31f,1.15f,.39f),stone);b.Box(new Vector3(side*.23f,.65f,.09f),new Vector3(.42f,.22f,.6f),stone*.82f);}
                b.Box(new Vector3(0,1.85f,0),new Vector3(kind==0?1.15f:.77f,1.03f,.6f),stone);
                b.Box(new Vector3(0,2.65f,0),new Vector3(kind==3?.9f:.75f,.72f,.72f),edge);
                for(int side=-1;side<=1;side+=2){b.Box(new Vector3(side*(kind==0?.77f:.56f),1.68f,.02f),new Vector3(.31f,1.3f,.37f),stone);b.Box(new Vector3(side*.19f,2.72f,.374f),new Vector3(.13f,.09f,.035f),VoxelAssets.Hex("#394A50"));}
                if(kind==1){for(int i=0;i<3;i++)b.Box(new Vector3(0,1.6f+i*.22f,.32f),new Vector3(.85f,.08f,.09f),edge);b.Box(new Vector3(.79f,1.75f,.15f),new Vector3(.11f,1.75f,.12f),stone*.65f,Quaternion.Euler(0,0,-14));}
                if(kind==2){b.Box(new Vector3(0,2.99f,.03f),new Vector3(1.1f,.11f,.82f),stone*.8f);b.Box(new Vector3(.88f,1.6f,.1f),new Vector3(.13f,1.25f,.12f),edge);}
                if(kind==3){b.Box(new Vector3(0,1.92f,-.15f),new Vector3(1.25f,.95f,.37f),stone*.65f);b.Box(new Vector3(0,2.45f,.39f),new Vector3(.2f,.23f,.045f),stone*.55f);}
            },at);statue.localScale=Vector3.one*scale;statue.localRotation=Quaternion.Euler(0,-8,0);
        }

        void BuildFrontHost()
        {
            FrontScene scene=NewFrontScene("host",VoxelAssets.Hex("#172522"),VoxelAssets.Hex("#C6C2A1"),new Vector3(15,11,25),new Vector3(-4,2,0),40);
            scene.subject=new Vector3(7,1.8f,0);
            FrontStars(scene,"Camp amber firefly canopy",VoxelAssets.Hex("#D6C08D"));
            Transform camp=NewRoot("Warm four-seat waiting ritual camp",scene.root);camp.localPosition=new Vector3(7,0,0);
            FrontPart(camp,"Camp suspended stone and timber terrace",b=>{
                b.Box(new Vector3(0,-.2f,0),new Vector3(13,.8f,11),VoxelAssets.Hex("#435B4D"));b.Box(new Vector3(0,.2f,0),new Vector3(12.6f,.25f,10.6f),VoxelAssets.Hex("#728064"));
                for(int i=-5;i<=5;i++)b.Box(new Vector3(i*1.1f,.36f,0),new Vector3(1.02f,.12f,10),VoxelAssets.Hex(i%3==0?"#8A8062":"#736F53"));
                for(int sign=-1;sign<=1;sign+=2)for(int i=-2;i<=2;i++){
                    b.Box(new Vector3(sign*6.1f,.8f,i*2),new Vector3(.3f,1.3f,.3f),VoxelAssets.Hex("#4C5141"));
                    b.Box(new Vector3(sign*6.1f,1.2f,i*2),new Vector3(.2f,.23f,2.1f),VoxelAssets.Hex("#677059"));
                }
                for(int i=0;i<12;i++){float a=i*Mathf.PI/6;b.Box(new Vector3(Mathf.Cos(a)*1.35f,.63f,Mathf.Sin(a)*1.35f),new Vector3(.58f,.47f,.62f),VoxelAssets.Hex(i%2==0?"#8D8870":"#677464"),Quaternion.Euler(0,i*30,0));}
                b.Box(new Vector3(0,.66f,0),new Vector3(1.6f,.28f,.36f),VoxelAssets.Hex("#5E4636"),Quaternion.Euler(0,35,0));
                b.Box(new Vector3(0,.74f,0),new Vector3(1.6f,.28f,.36f),VoxelAssets.Hex("#66503B"),Quaternion.Euler(0,-45,0));
            },Vector3.zero);
            Transform fire=FrontPart(camp,"Living campfire voxel flames",b=>{
                b.Box(new Vector3(0,.4f,0),new Vector3(.8f,.75f,.72f),VoxelAssets.Hex("#DE883D"),Quaternion.Euler(0,20,0));
                b.Box(new Vector3(.12f,.82f,.05f),new Vector3(.47f,.8f,.42f),VoxelAssets.Hex("#FFD079"),Quaternion.Euler(0,-20,0));
                b.Box(new Vector3(-.19f,.65f,-.1f),new Vector3(.25f,.69f,.27f),VoxelAssets.Hex("#FFF0B3"));
            },new Vector3(0,.8f,0),true,false);FrontMove(scene,fire,0,0,0,1);
            scene.accent=FrontLight(camp,"Campfire warm bounce",new Vector3(0,2,0),VoxelAssets.Hex("#FFD08B"),2.5f,11);
            for(int i=0;i<4;i++){
                int seat=i;Vector3 at=new Vector3(i%2==0?-3.6f:3.6f,.43f,i<2?-2.8f:2.8f);
                FrontPart(camp,"Waiting ritual seat "+i,b=>{
                    Color wood=VoxelAssets.Hex("#65513D");b.Box(new Vector3(0,.36f,0),new Vector3(1.35f,.37f,1.25f),wood);b.Box(new Vector3(0,1.05f,-.48f),new Vector3(1.35f,1.12f,.27f),wood);
                    b.Box(new Vector3(0,1.25f,-.31f),new Vector3(.72f,.58f,.045f),Teams[seat]*.6f);
                    for(int side=-1;side<=1;side+=2)b.Box(new Vector3(side*.75f,.7f,0),new Vector3(.17f,.28f,1.12f),wood*.85f);
                    b.Box(new Vector3(1.1f,1.35f,-.9f),new Vector3(.13f,2.7f,.13f),VoxelAssets.Hex("#575947"));
                    b.Box(new Vector3(1.55f,2.33f,-.9f),new Vector3(.9f,.67f,.09f),Teams[seat]*.8f);
                },at);
                Transform rune=FrontPart(camp,"Four-seat summoning rune "+i,b=>{b.Ring(.8f,.73f,Teams[seat]);b.Box(new Vector3(0,.1f,0),Vector3.one*.17f,Teams[seat],Quaternion.Euler(20,45,20));},at+new Vector3(0,.02f,1.2f),true,false);
                FrontMove(scene,rune,.035f,6,i*.8f);
            }
            FrontPart(camp,"Camp ceremonial rear wall and pines",b=>{
                for(int i=-3;i<=3;i++){
                    float x=i*2.2f,h=3.8f+(i%2)*.9f;b.Box(new Vector3(x,h*.4f,-6.2f),new Vector3(.35f,h,.35f),VoxelAssets.Hex("#514F3C"));
                    for(int tier=0;tier<3;tier++)b.Box(new Vector3(x,h*.7f+tier*.75f,-6.2f),new Vector3(2.3f-tier*.45f,.95f,2.1f-tier*.4f),VoxelAssets.Hex(tier%2==0?"#3E634D":"#52765A"));
                }
                b.Box(new Vector3(0,3.3f,-4.7f),new Vector3(4.8f,.47f,.55f),VoxelAssets.Hex("#8B8A68"));
                for(int side=-1;side<=1;side+=2)b.Box(new Vector3(side*2.2f,1.9f,-4.7f),new Vector3(.5f,3.4f,.5f),VoxelAssets.Hex("#586E58"));
            },Vector3.zero);
            FrontMotes(scene,14,VoxelAssets.Hex("#FFD18C"));
        }

        void BuildFrontJoin()
        {
            FrontScene scene=NewFrontScene("join",VoxelAssets.Hex("#101C2C"),VoxelAssets.Hex("#9CAECC"),new Vector3(13,10,24),new Vector3(-4,2,-2),39);
            scene.subject=new Vector3(7,3,-4.5f);
            FrontStars(scene,"Portal midnight star corridor",VoxelAssets.Hex("#AEC9EE"));
            Transform bridge=NewRoot("Cold portal arrival causeway",scene.root);bridge.localPosition=new Vector3(7,0,-3);
            FrontPart(bridge,"Suspended approach stone bridge",b=>{
                b.Box(new Vector3(0,-.26f,6.2f),new Vector3(4.6f,.65f,18),VoxelAssets.Hex("#303D51"));
                for(int z=0;z<14;z++)for(int x=-1;x<=1;x++)b.Box(new Vector3(x*1.38f,.15f,-2+z*1.24f),new Vector3(1.31f,.19f,1.17f),VoxelAssets.Hex((x+z)%3==0?"#6D7B8C":"#586A80"));
                b.Box(new Vector3(0,.1f,-1),new Vector3(8,.55f,5.5f),VoxelAssets.Hex("#4D5D75"));
                for(int side=-1;side<=1;side+=2)for(int z=0;z<6;z++){
                    b.Box(new Vector3(side*2.35f,.74f,1+z*2.4f),new Vector3(.33f,1.42f,.34f),VoxelAssets.Hex("#48566F"));
                    b.Box(new Vector3(side*2.35f,1.22f,1+z*2.4f),new Vector3(.15f,.15f,2.48f),VoxelAssets.Hex("#677891"));
                }
                Color obsidian=VoxelAssets.Hex("#292C43");
                for(int i=0;i<8;i++)for(int side=-1;side<=1;side+=2){
                    b.Box(new Vector3(side*3.05f,.57f+i*.88f,-1.5f),new Vector3(.95f,.82f,1.1f),i%2==0?obsidian:obsidian*1.18f);
                    if(i%2==0)b.Box(new Vector3(side*3.05f,.92f+i*.88f,-1.48f),new Vector3(1.13f,.16f,1.27f),VoxelAssets.Hex("#465370"));
                }
                for(int x=-3;x<=3;x++)b.Box(new Vector3(x*.95f,7.52f,-1.5f),new Vector3(.91f,.82f,1.17f),x%2==0?obsidian:obsidian*1.18f);
                b.Box(new Vector3(0,8.03f,-1.5f),new Vector3(2.4f,.31f,1.3f),VoxelAssets.Hex("#6D7291"));
            },Vector3.zero);
            FrontPart(bridge,"Portal frame ice-blue runes",b=>{
                for(int side=-1;side<=1;side+=2)for(int i=0;i<5;i++){
                    b.Box(new Vector3(side*3.05f,1.3f+i*1.08f,-.91f),new Vector3(.27f,.1f,.035f),VoxelAssets.Hex("#84BDDC"));
                    b.Box(new Vector3(side*3.12f,1.45f+i*1.08f,-.91f),new Vector3(.08f,.32f,.035f),VoxelAssets.Hex("#A5B9EB"));
                }
                for(int z=0;z<12;z++)b.Box(new Vector3(0,.264f,-1+z*1.22f),new Vector3(.11f,.02f,.35f),VoxelAssets.Hex("#91D4DD"));
            },Vector3.zero,true,false);
            EnsureCombatVfx();
            Transform veil=FrontPart(bridge,"Layered breathing portal veil",b=>{
                b.Box(new Vector3(0,3.87f,-1.47f),new Vector3(5.12f,6.4f,.045f),VoxelAssets.Hex("#5275A7"));
                for(int i=0;i<14;i++)b.Box(new Vector3((i%3-1)*1.25f,1+(i*7%13)*.43f,-1.39f),new Vector3(.7f+(i%2)*.7f,.15f,.04f),VoxelAssets.Hex(i%2==0?"#9C90DD":"#81BED5"));
            },Vector3.zero,false,false);
            Renderer portal=veil.GetComponent<Renderer>();portal.sharedMaterial=combatMaterial;
            var tint=new MaterialPropertyBlock();tint.SetColor(CombatTintId,new Color(1,1,1,.72f));portal.SetPropertyBlock(tint);
            Transform core=FrontPart(bridge,"Portal suspended compass crystal",b=>{
                b.Box(Vector3.zero,Vector3.one*.52f,VoxelAssets.Hex("#C6D5F7"),Quaternion.Euler(35,45,15));
                for(int i=0;i<4;i++)b.Box(Quaternion.Euler(0,0,i*90)*new Vector3(.85f,0,0),new Vector3(.24f,.16f,.13f),VoxelAssets.Hex("#A696DC"),Quaternion.Euler(0,0,i*90));
            },new Vector3(0,4,-1.15f),true,false);FrontMove(scene,core,.18f,12,.6f);
            scene.accent=FrontLight(bridge,"Portal cold spill",new Vector3(0,3,0),VoxelAssets.Hex("#8BB9FF"),2.8f,12);
            FrontMotes(scene,18,VoxelAssets.Hex("#A7C8F1"));
        }
        void BuildFrontSettings()
        {
            FrontScene scene=NewFrontScene("settings",VoxelAssets.Hex("#18272C"),VoxelAssets.Hex("#B1BDB4"),new Vector3(17,10,27),new Vector3(7,2.5f,0),37);
            scene.subject=new Vector3(7,3.1f,0);scene.viewportTarget=new Vector2(.86f,.50f);
            FrontStars(scene,"Workshop suspended copper dust",VoxelAssets.Hex("#91C5BF"));
            Transform workshop=NewRoot("Crystal forge and redstone atelier",scene.root);workshop.localPosition=new Vector3(7,0,0);
            FrontPart(workshop,"Workshop carved foundation and tiled floor",b=>{
                Color dark=VoxelAssets.Hex("#35494B"),stone=VoxelAssets.Hex("#677970"),copper=VoxelAssets.Hex("#B18359");
                b.Disc(6.7f,-.9f,-.35f,8,dark,dark*.68f);b.Disc(6.2f,-.35f,.13f,8,stone,dark);
                for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++){
                    if(x*x+z*z>21)continue;
                    b.Box(new Vector3(x*1.12f,.19f,z*1.12f),new Vector3(1.05f,.12f,1.05f),VoxelAssets.Hex((x+z)%3==0?"#75867A":"#586D65"));
                }
                for(int i=0;i<16;i++){
                    float a=i*Mathf.PI/8;Vector3 at=new Vector3(Mathf.Cos(a)*5.9f,.32f,Mathf.Sin(a)*5.9f);
                    b.Box(at,new Vector3(.44f,.22f,.44f),copper,Quaternion.Euler(0,-a*Mathf.Rad2Deg,0));
                    b.Box(at+Vector3.down*.98f,new Vector3(.32f,1.8f,.32f),dark*.83f);
                }
                for(int side=-1;side<=1;side+=2){
                    b.Box(new Vector3(side*3.5f,2.75f,-2.25f),new Vector3(.64f,5.1f,.78f),dark);
                    for(int i=0;i<4;i++)b.Box(new Vector3(side*3.5f,1+i*1.25f,-2.25f),new Vector3(.88f,.19f,.99f),copper*.87f);
                    b.Box(new Vector3(side*2.4f,5.3f,-2.25f),new Vector3(2.4f,.3f,.57f),copper,Quaternion.Euler(0,0,side*16));
                    b.Box(new Vector3(side*3.85f,.85f,1.8f),new Vector3(.8f,1.3f,.8f),dark);
                    b.Box(new Vector3(side*3.85f,1.56f,1.8f),new Vector3(.98f,.2f,.98f),copper);
                }
                b.Box(new Vector3(0,.9f,2.4f),new Vector3(4.8f,1.1f,.34f),dark);
                b.Box(new Vector3(0,1.5f,2.4f),new Vector3(5.2f,.23f,1.75f),VoxelAssets.Hex("#A28D69"));
                for(int side=-1;side<=1;side+=2){
                    b.Box(new Vector3(side*1.9f,1.7f,2.3f),new Vector3(.68f,.22f,.8f),VoxelAssets.Hex(side<0?"#667F8C":"#9C695B"));
                    b.Box(new Vector3(side*1.9f,1.84f,2.3f),new Vector3(.62f,.07f,.74f),VoxelAssets.Hex("#D2C6A0"));
                }
                b.Box(new Vector3(0,.62f,-.7f),new Vector3(2.65f,.65f,2.6f),dark);b.Box(new Vector3(0,1.02f,-.7f),new Vector3(2.92f,.18f,2.82f),copper);
            },Vector3.zero);
            FrontPart(workshop,"Workshop glowing redstone circuits and calibration lamps",b=>{
                Color red=VoxelAssets.Hex("#CA7255"),cyan=VoxelAssets.Hex("#82D7CE"),gold=VoxelAssets.Hex("#E1BF7F");
                for(int side=-1;side<=1;side+=2){
                    b.Box(new Vector3(side*2.5f,.28f,.4f),new Vector3(.075f,.025f,5.9f),red);
                    b.Box(new Vector3(side*1.55f,.28f,-2.5f),new Vector3(1.95f,.025f,.075f),red);
                    b.Box(new Vector3(side*3.85f,1.75f,1.8f),new Vector3(.46f,.28f,.46f),gold);
                    for(int i=0;i<4;i++)b.Box(new Vector3(side*3.5f,1.1f+i*.94f,-1.84f),new Vector3(.12f,.28f,.025f),cyan);
                }
                b.Ring(1.34f,1.27f,cyan);
                for(int i=0;i<7;i++)b.Box(new Vector3(-1.2f+i*.4f,1.66f,2.72f),new Vector3(.23f,.035f,.12f),i<4?cyan:red);
            },new Vector3(0,.02f,0),true,false);
            FrontWorkshopGear(scene,workshop,"Large aged copper drive gear",new Vector3(-1.7f,3.35f,-2.3f),2.15f,14,-7);
            FrontWorkshopGear(scene,workshop,"Counter rotating bronze balance gear",new Vector3(1.65f,5.25f,-2.3f),1.24f,10,12);
            Transform crystal=FrontPart(workshop,"Workshop suspended cut crystal",b=>{
                Color bright=VoxelAssets.Hex("#B6EEE0"),facet=VoxelAssets.Hex("#76BFAF");
                b.Box(Vector3.zero,new Vector3(.86f,1.72f,.86f),facet,Quaternion.Euler(0,45,0));
                b.Box(new Vector3(0,.68f,0),Vector3.one*.62f,bright,Quaternion.Euler(35,45,35));
                b.Box(new Vector3(0,-.68f,0),Vector3.one*.62f,bright,Quaternion.Euler(35,45,35));
                b.Box(Vector3.zero,new Vector3(.36f,1.8f,.36f),VoxelAssets.Hex("#E0F7D9"),Quaternion.Euler(0,45,0));
            },new Vector3(0,2.8f,-.65f),true,false);FrontMove(scene,crystal,.15f,15,.7f);
            Transform cradle=FrontPart(workshop,"Crystal copper orbital cradle",b=>{
                Color metal=VoxelAssets.Hex("#B89A70");b.Ring(1.58f,1.48f,metal);
                for(int i=0;i<6;i++){
                    float a=i*Mathf.PI/3;Vector3 at=new Vector3(Mathf.Cos(a)*1.5f,0,Mathf.Sin(a)*1.5f);
                    b.Box(at,new Vector3(.13f,.45f,.13f),metal);b.Box(at+Vector3.up*.28f,Vector3.one*.16f,VoxelAssets.Hex("#AFE6CF"),Quaternion.Euler(0,45,0));
                }
            },new Vector3(0,2.7f,-.65f));FrontMove(scene,cradle,.045f,-9,1.8f);
            FrontPart(scene.root,"Workshop distant paired capacitor and copper conduits",b=>{
                Color stone=VoxelAssets.Hex("#40585A"),metal=VoxelAssets.Hex("#7D735D"),glass=VoxelAssets.Hex("#426E6A");
                Vector3 opposite=new Vector3(32,0,-9);
                b.Box(opposite+Vector3.down*.5f,new Vector3(9,.9f,8),stone);
                b.Box(opposite+Vector3.down*1.2f,new Vector3(7.4f,.7f,6.6f),stone*.7f);
                for(int side=-1;side<=1;side+=2){
                    Vector3 at=opposite+new Vector3(side*2.3f,0,0);
                    b.Box(at+Vector3.up*2.4f,new Vector3(1.4f,4.5f,1.4f),glass);
                    for(int i=0;i<6;i++)b.Box(at+Vector3.up*(.35f+i*.8f),new Vector3(1.75f,.19f,1.75f),metal);
                    b.Box(at+Vector3.up*5.1f,new Vector3(.45f,1.05f,.45f),metal);
                }
                Vector3 conduit=new Vector3(22,.15f,-4.6f);Quaternion angle=Quaternion.Euler(0,20,0);
                for(int side=-1;side<=1;side+=2)b.Box(conduit+Vector3.forward*side*.38f,new Vector3(25,.22f,.2f),metal,angle);
                for(int i=0;i<7;i++)b.Box(conduit+angle*new Vector3(i*3.5f-10.5f,-.62f,0),new Vector3(.46f,1.4f,1.4f),stone);
            },Vector3.zero);
            FrontPart(scene.root,"Workshop quiet auxiliary capacitor glow",b=>{
                for(int side=-1;side<=1;side+=2)for(int i=0;i<4;i++)
                    b.Box(new Vector3(32+side*2.3f,1.15f+i*.8f,-8.28f),new Vector3(.62f,.12f,.035f),VoxelAssets.Hex("#7DB5A3"));
            },Vector3.zero,true,false);
            scene.accent=FrontLight(workshop,"Workshop crystal mint spill",new Vector3(0,3.4f,.8f),VoxelAssets.Hex("#91E1CE"),2.4f,9);
            FrontMotes(scene,14,VoxelAssets.Hex("#DDC18F"));
        }
        void FrontWorkshopGear(FrontScene scene,Transform parent,string name,Vector3 at,float radius,int teeth,float speed)
        {
            Transform gear=FrontPart(parent,name,b=>{
                Color face=VoxelAssets.Hex("#B48C60"),edge=VoxelAssets.Hex("#6F705D"),rim=VoxelAssets.Hex("#D0AF7D");
                for(int i=0;i<teeth;i++){
                    Quaternion q=Quaternion.Euler(0,0,i*360f/teeth);Vector3 radial=q*Vector3.right;
                    b.Box(radial*radius,new Vector3(radius*.26f,radius*.31f,.4f),face,q);
                    b.Box(radial*(radius*.8f),new Vector3(radius*.2f,radius*.38f,.32f),edge,q);
                    b.Box(radial*(radius*.97f)+Vector3.forward*.215f,new Vector3(radius*.08f,radius*.18f,.035f),rim,q);
                }
                for(int i=0;i<6;i++)b.Box(Vector3.zero,new Vector3(radius*1.56f,.17f,.24f),face,Quaternion.Euler(0,0,i*60));
                b.Box(Vector3.zero,Vector3.one*(radius*.43f),edge);b.Box(Vector3.forward*.24f,new Vector3(radius*.24f,radius*.24f,.12f),rim,Quaternion.Euler(0,0,45));
            },at);FrontMove(scene,gear,0,speed,0,2);
        }
        void UpdateFrontend(float time)
        {
            if(frontScene==null)return;
            float drift=Mathf.Sin(time*.075f)*.16f;
            Vector3 eye=frontScene.eye+new Vector3(drift,Mathf.Sin(time*.11f)*.045f,0);
            viewCamera.fieldOfView=frontScene.fov;viewCamera.transform.position=eye;viewCamera.transform.rotation=Quaternion.LookRotation(frontScene.focus-eye,Vector3.up);
            // Frame from the camera's actual projection, rather than a world-X
            // offset: this view faces negative Z, so world-X has the opposite sign.
            // Translate in the camera plane after aiming; aiming again would undo it.
            Vector3 subjectViewport=FrontendSubjectViewport;
            if(subjectViewport.z>viewCamera.nearClipPlane){
                Vector2 target=FrontendSubjectViewportTarget;
                float height=2*subjectViewport.z*Mathf.Tan(frontScene.fov*.5f*Mathf.Deg2Rad);
                viewCamera.transform.position+=viewCamera.transform.right*((subjectViewport.x-target.x)*height*viewCamera.aspect)
                    +viewCamera.transform.up*((subjectViewport.y-target.y)*height);
            }
            foreach(FrontMotion motion in frontScene.motions){
                if(motion.kind==1){float pulse=1+Mathf.Sin(time*7.2f)*.055f+Mathf.Sin(time*11.3f)*.025f;motion.root.localScale=Vector3.Scale(motion.scale,new Vector3(1/pulse,pulse,1/pulse));}
                else{motion.root.localPosition=motion.at+Vector3.up*(Mathf.Sin(time*motion.speed+motion.phase)*motion.bob);motion.root.localRotation=motion.rotation*(motion.kind==2?Quaternion.Euler(0,0,time*motion.spin):Quaternion.Euler(0,time*motion.spin,0));}
            }
            foreach(FrontMote mote in frontScene.motes){
                float cycle=Mathf.Repeat(time*mote.speed+mote.phase,1);
                if(frontendMode=="host")mote.root.localPosition=new Vector3(7+Mathf.Sin(cycle*5+mote.phase)*(.2f+cycle*.65f),.9f+cycle*3.8f,Mathf.Cos(mote.phase*3)*(.2f+cycle*.8f));
                else if(frontendMode=="join")mote.root.localPosition=new Vector3(7+Mathf.Sin(mote.phase*13)*1.6f,.75f+Mathf.Sin(mote.phase*7)*.35f+cycle*3.3f,10-cycle*14.4f);
                else if(frontendMode=="settings")mote.root.localPosition=new Vector3(7+Mathf.Cos(cycle*Mathf.PI*2+mote.phase)*(2.1f+(mote.phase%2)*.55f),1.2f+cycle*3.8f,-.65f+Mathf.Sin(cycle*Mathf.PI*2+mote.phase)*1.9f);
                else mote.root.localPosition=mote.at+new Vector3(Mathf.Sin(time*.35f+mote.phase)*.3f,cycle*2.6f,Mathf.Cos(time*.2f+mote.phase)*.28f);
            }
            if(frontScene.accent!=null)frontScene.accent.intensity=(frontendMode=="host"?2.5f:frontendMode=="join"?2.8f:frontendMode=="settings"?2.4f:2.2f)*(1+Mathf.Sin(time*(frontendMode=="host"?7.1f:1.4f))*.055f);
        }
    }
}
