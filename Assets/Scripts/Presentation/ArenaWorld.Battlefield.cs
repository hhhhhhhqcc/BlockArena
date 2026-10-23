using System;
using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        // Retained map geometry is built once per map. Weather and tactical markers have
        // separate, fixed budgets so a storm never displaces a creature's combat effect.
        const int FieldMarkerBudget=64, FieldWeatherBudget=1100;
        sealed class FieldTerrainView
        {
            public string id,kind;
            public TerrainState state;
            public Transform root,water,ice,iceCrystals,standing,rubble,fire;
            public MeshRenderer waterRenderer,iceRenderer,fireRenderer;
            public Mesh iceMesh;
            public Vector3[] iceVertices;
            public Color[] iceColors;
            public readonly List<int> iceTriangles=new List<int>(5600);
            public float iceFraction=-1;
            public string iceCycle;
            public float x,y;
        }
        sealed class FieldObjectiveView
        {
            public string id;
            public ObjectiveState state;
            public Transform root,icon,ring,progress,contents;
            public MeshRenderer ringRenderer,iconRenderer,progressRenderer;
            public float x,y;
        }
        sealed class FieldMapView
        {
            public string id,geometryKey;
            public float scale=1;
            public Transform root,floor,revealRing,environment;
            public MeshRenderer revealRenderer;
            public Mesh floorMesh,revealFloorMesh;
            public Vector3[] floorVertices,revealFloorVertices;
            public float floorSpread=-1;
            public readonly List<FieldTerrainView> terrain=new List<FieldTerrainView>();
            public readonly List<FieldObjectiveView> objectives=new List<FieldObjectiveView>();
        }
        sealed class FieldMarker
        {
            public Transform root,ring,fill,beam;
            public MeshRenderer ringRenderer,fillRenderer,beamRenderer;
            public string kind;
            public Color color;
            public float x,y,radius,delay,remaining;
            public bool triggered,zone;
        }
        readonly Dictionary<string,FieldMapView> fieldMaps=new Dictionary<string,FieldMapView>();
        readonly List<FieldMarker> fieldMarkers=new List<FieldMarker>(FieldMarkerBudget);
        readonly MaterialPropertyBlock fieldTint=new MaterialPropertyBlock();
        static readonly Dictionary<string,Mesh> fieldMeshes=new Dictionary<string,Mesh>();
        static readonly int FieldTintId=Shader.PropertyToID("_Tint");
        Transform fieldRoot,fieldMarkerRoot;
        Material fieldMaterial;
        ParticleSystem fieldWeather;
        FieldMapView fieldMap;
        BattlefieldState fieldState;
        Light fieldSun;
        bool fieldVisible,fieldLightingSaved;
        string fieldWeatherId,fieldPhase,fieldRevealStage,fieldRiseSoundKey,fieldSettleSoundKey,fieldClimateSoundKey,fieldRevealArmedKey;
        float fieldRevealAge,fieldSnapshotTime,fieldRevealProgress=1,fieldWeatherEmission,fieldStageAge,fieldSettleAt=-10;
        int fieldMarkerCount,fieldGeometryCount,fieldRevealSoundCount;
        Color fieldOriginalSky,fieldOriginalEquator,fieldOriginalGround,fieldOriginalFog,fieldOriginalBackground,fieldOriginalSun;
        float fieldOriginalFogStart,fieldOriginalFogEnd,fieldOriginalSunIntensity;
        internal int FieldGeometryCount { get { return fieldGeometryCount; } }
        internal int FieldTerrainCount { get { return fieldMap==null?0:fieldMap.terrain.Count; } }
        internal int FieldObjectiveCount { get { return fieldMap==null?0:fieldMap.objectives.Count; } }
        internal int WeatherParticleCount { get { return fieldWeather==null?0:fieldWeather.particleCount; } }
        internal float RevealProgress { get { return fieldRevealProgress; } }
        internal int MapRevealSoundCount { get { return fieldRevealSoundCount; } }
        internal float FieldGenerationShakeStrength
        {
            get {
                if(!fieldVisible||fieldPhase!="reveal")return 0;
                float rumble=fieldRevealStage=="terrain"?Mathf.Sin(fieldRevealProgress*Mathf.PI)*.034f:0;
                float settle=Mathf.Clamp01(1-(Time.unscaledTime-fieldSettleAt)/.42f)*.055f;
                return Mathf.Max(rumble,settle);
            }
        }

        void EnsureBattlefieldPresentation()
        {
            if(fieldRoot!=null)return;
            fieldRoot=NewRoot("Emerging battlefields",scenery);
            fieldMarkerRoot=NewRoot("Budgeted terrain warnings and command zones",fieldRoot);
            Shader shader=Resources.Load<Shader>("ArenaCombat");
            fieldMaterial=new Material(shader){name="Shared battlefield water, weather and telegraph material"};
            GameObject g=new GameObject("Budgeted battlefield weather");g.transform.SetParent(fieldRoot,false);
            fieldWeather=g.AddComponent<ParticleSystem>();
            var main=fieldWeather.main;main.loop=true;main.playOnAwake=false;main.startSpeed=0;
            main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=FieldWeatherBudget;
            main.startLifetime=2;main.startSize3D=true;
            var emission=fieldWeather.emission;emission.enabled=false;
            var shape=fieldWeather.shape;shape.enabled=false;
            var fade=fieldWeather.colorOverLifetime;fade.enabled=true;
            Gradient gradient=new Gradient();gradient.SetKeys(
                new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.08f),new GradientAlphaKey(.75f,.8f),new GradientAlphaKey(0,1)});
            fade.color=new ParticleSystem.MinMaxGradient(gradient);
            var renderer=g.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
            renderer.mesh=VoxelAssets.Block(Color.white);renderer.sharedMaterial=fieldMaterial;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        }

        void SyncBattlefieldPresentation(GameState next,string viewerId)
        {
            if(next!=null&&next.phase=="reveal"&&(next.revealStage=="arena"||next.revealStage=="map"))
                fieldRevealArmedKey=viewerId+":"+next.round;
            else if(next==null||next.phase=="lobby")fieldRevealArmedKey=null;
            bool visible=next!=null&&next.battlefield!=null&&(next.phase=="reveal"||next.phase=="tactics"||next.phase=="battle"||next.phase=="results"||next.phase=="finished");
            if(!visible){ResetBattlefieldPresentation();return;}
            EnsureBattlefieldPresentation();SaveFieldLighting();
            fieldVisible=true;fieldRoot.gameObject.SetActive(true);if(!fieldWeather.isPlaying)fieldWeather.Play();fieldState=next.battlefield;fieldPhase=next.phase;
            fieldRevealAge=fieldState.revealAge;fieldStageAge=next.revealStageAge;fieldRevealStage=next.revealStage;fieldSnapshotTime=Time.unscaledTime;
            fieldRevealProgress=FieldRevealProgress(0);
            FieldMapView map;
            string mapKey=fieldState.mapId+":"+fieldState.scale.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
            if(!fieldMaps.TryGetValue(mapKey,out map)){
                map=BuildFieldMap(fieldState);fieldMaps.Add(mapKey,map);
            }
            if(fieldMap!=map){
                if(fieldMap!=null)fieldMap.root.gameObject.SetActive(false);
                fieldMap=map;fieldMap.root.gameObject.SetActive(true);fieldWeather.Clear();
            }
            // Map identity is drawn before its geometry. Keep the ordinary arena
            // completely visible until the host advances into terrain emergence.
            map.root.gameObject.SetActive(next.phase!="reveal"||fieldRevealStage!="map");
            for(int i=0;i<map.terrain.Count;i++){
                FieldTerrainView view=map.terrain[i];view.state=null;
                for(int j=0;j<fieldState.terrain.Count;j++)if(fieldState.terrain[j].id==view.id){view.state=fieldState.terrain[j];break;}
            }
            for(int i=0;i<map.objectives.Count;i++){
                FieldObjectiveView view=map.objectives[i];view.state=null;
                for(int j=0;j<fieldState.objectives.Count;j++)if(fieldState.objectives[j].id==view.id){view.state=fieldState.objectives[j];break;}
            }
            ApplyFieldEmergence();
            if(fieldWeatherId!=fieldState.weather){fieldWeatherId=fieldState.weather;fieldWeather.Clear();fieldWeatherEmission=0;}
            SyncFieldIceCycle(next.round);
            string revealKey=FieldRevealKey();bool armed=fieldRevealArmedKey==viewerId+":"+next.round;
            if(armed&&next.phase=="reveal"&&fieldRevealStage=="terrain"&&fieldStageAge<.6f&&fieldRiseSoundKey!=revealKey){
                fieldRiseSoundKey=revealKey;
                PlayFieldRevealSound("map_rise_"+(map.id=="bog"?"bog":map.id=="frost"?"frost":"stone"),.85f);
            }
            if(armed&&next.phase=="reveal"&&fieldRevealStage=="climate"&&fieldStageAge<.6f&&fieldClimateSoundKey!=revealKey){
                fieldClimateSoundKey=revealKey;PlayFieldRevealSound("weather_reveal",.7f);
            }
            SyncFieldMarkers();
        }
        string FieldRevealKey(){return state==null||fieldState==null?null:playerId+":"+state.round+":"+fieldState.mapId+":"+fieldState.seed;}

        float FieldRevealProgress(float since)
        {
            if(fieldPhase!="reveal")return 1;
            if(fieldRevealStage=="map"||fieldRevealStage=="arena")return 0;
            if(fieldRevealStage=="weather"||fieldRevealStage=="climate")return 1;
            float age=fieldRevealStage=="terrain"?fieldStageAge+since:fieldRevealAge+since;
            return Mathf.SmoothStep(0,1,Mathf.Clamp01(age/ArenaGame.RevealTerrainSeconds));
        }
        void PlayFieldRevealSound(string cue,float intensity)
        {if(SoundCue!=null){SoundCue(cue,Vector3.zero,intensity);fieldRevealSoundCount++;}}

        float BattlefieldVisualHeight(float x,float y)
        {return fieldVisible&&fieldState!=null?Mathf.Max(0,BattlefieldCatalog.HeightAt(fieldState,x,y)+FieldGroundOffset):0;}

        void ResetBattlefieldPresentation()
        {
            ResetFieldEnvironment();
            if(!fieldVisible)return;
            fieldVisible=false;fieldState=null;fieldPhase=fieldRevealStage=null;fieldRevealProgress=1;fieldMarkerCount=0;fieldSettleAt=-10;
            if(fieldRoot!=null)fieldRoot.gameObject.SetActive(false);
            if(fieldWeather!=null)fieldWeather.Clear();
            RestoreFieldLighting();
        }

        // Called by the existing ArenaWorld OnDestroy, alongside combat material cleanup.
        void DisposeBattlefieldPresentation()
        {RestoreFieldLighting();foreach(var map in fieldMaps.Values){if(map.revealFloorMesh!=null)Destroy(map.revealFloorMesh);foreach(var terrain in map.terrain)if(terrain.iceMesh!=null)Destroy(terrain.iceMesh);}if(fieldMaterial!=null)Destroy(fieldMaterial);}

        Transform FieldPart(string name,Transform parent,Mesh mesh,Vector3 at,bool transparent=false)
        {
            fieldGeometryCount++;
            return VoxelAssets.Part(parent,name,mesh,at,transparent?fieldMaterial:null,!transparent);
        }
        Transform FieldMesh(string name,Transform parent,string key,Action<VoxelMesh> make,Vector3 at,bool transparent=false)
        {return FieldPart(name,parent,VoxelAssets.Cached("field-"+key,make),at,transparent);}
        static Color FieldColor(string hex){return VoxelAssets.Hex(hex);}
        static Vector3 FieldAt(float x,float y,float height=0){return new Vector3(x*WorldScale,height,y*WorldScale);}
        void TintField(MeshRenderer renderer,Color color)
        {if(renderer==null)return;fieldTint.Clear();fieldTint.SetColor(FieldTintId,color);renderer.SetPropertyBlock(fieldTint);}

        FieldMapView BuildFieldMap(BattlefieldState source)
        {
            FieldMapView map=new FieldMapView{id=source.mapId,scale=Mathf.Max(1,source.scale),root=NewRoot("Map "+source.mapId,fieldRoot)};
            map.geometryKey=map.id+"-"+map.scale.ToString("0.###",System.Globalization.CultureInfo.InvariantCulture);
            bool frost=source.mapId=="frost",bog=source.mapId=="bog";
            Color floor=FieldColor(frost?"#90A7A4":bog?"#63715A":"#8A9073");
            map.floor=BuildLandscapeFloor(map,source);
            map.revealRing=FieldMesh("Expanding map emergence front",map.root,"map-reveal-ring",b=>b.Ring(1,.98f,Color.white),new Vector3(0,.12f,0),true);
            map.revealRenderer=map.revealRing.GetComponent<MeshRenderer>();
            for(int i=0;i<source.terrain.Count;i++)map.terrain.Add(BuildFieldTerrain(map,source,source.terrain[i]));
            for(int i=0;i<source.objectives.Count;i++)map.objectives.Add(BuildFieldObjective(map,source.objectives[i]));
            BuildFieldEnvironment(map,source);
            map.root.gameObject.SetActive(false);return map;
        }

        FieldTerrainView BuildFieldTerrain(FieldMapView map,BattlefieldState source,TerrainState t)
        {
            FieldTerrainView v=new FieldTerrainView{id=t.id,kind=t.kind,state=t,x=t.x,y=t.y};
            v.root=NewRoot(t.kind+" "+t.id,map.root);v.root.localPosition=FieldAt(t.x,t.y);
            string key=map.geometryKey+"-"+t.id;bool frost=map.id=="frost";
            float width=t.halfWidth*WorldScale*2,depth=t.halfHeight*WorldScale*2;
            if(BuildNaturalFieldTerrain(map,source,t,v,key))return v;
            if(t.kind=="highland"||t.kind=="bridge"){
                FieldPart("Walkable ramp and stone layers",v.root,FieldSlopeMesh(key,t,frost),Vector3.zero);
                if(t.kind=="bridge"){
                    bool vertical=depth>width;float length=Mathf.Max(width,depth),side=Mathf.Min(width,depth)*.5f-.11f;
                    FieldMesh("Bridge edge posts",v.root,key+"-posts",b=>{
                        for(int sign=-1;sign<=1;sign+=2)for(int n=-2;n<=2;n++){
                            Vector3 p=vertical?new Vector3(sign*side,.24f,n*length*.17f):new Vector3(n*length*.17f,.24f,sign*side);
                            b.Box(p,new Vector3(.17f,.38f,.17f),FieldColor(frost?"#BACFD0":"#A9A58C"));
                        }
                    },Vector3.zero);
                }else if(map.id=="courtyards"){
                    FieldMesh("Plateau inlaid corners",v.root,key+"-inlay",b=>{
                        float w=Mathf.Max(.2f,width*.5f-1.15f),d=Mathf.Max(.2f,depth*.5f-1.15f);
                        for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2){
                            Color c=FieldColor(frost?"#D3EDF0":"#D9CC9D");
                            b.Box(new Vector3(sx*w,t.height+.018f,sz*d),new Vector3(.65f,.025f,.1f),c);
                            b.Box(new Vector3(sx*w,t.height+.018f,sz*d),new Vector3(.1f,.025f,.65f),c);
                        }
                    },Vector3.zero);
                }
            }else if(t.kind=="water"||t.kind=="wetland"){
                FieldMesh("Exposed mud and bank stones",v.root,key+"-bed",b=>{
                    b.Box(new Vector3(0,.071f,0),new Vector3(width,.025f,depth),FieldColor(frost?"#5C8188":"#455E49"));
                    for(int n=0;n<10;n++){
                        float x=(n/9f-.5f)*width*.95f,z=(n%2==0?-1:1)*depth*.45f;
                        b.Box(new Vector3(x,.11f,z),new Vector3(.35f,.13f,.24f),FieldColor(frost?"#B8CCCC":"#74816A"));
                    }
                },Vector3.zero);
                v.water=FieldMesh("Translucent shallow water",v.root,key+"-water",b=>{
                    b.Box(Vector3.zero,new Vector3(width,.02f,depth),Color.white);
                    for(int n=0;n<12;n++)b.Box(new Vector3((n/11f-.5f)*width*.85f,.015f,((n*7)%11/10f-.5f)*depth*.8f),new Vector3(.65f,.012f,.06f),FieldColor("#D5F9ED"));
                },new Vector3(0,.11f,0),true);v.waterRenderer=v.water.GetComponent<MeshRenderer>();
                BuildFieldIce(v,t,"Growing ice route");
                if(t.kind=="wetland")FieldMesh("Reed margins",v.root,key+"-reeds",b=>{
                    for(int n=0;n<24;n++){
                        float x=(n/23f-.5f)*width*.9f,z=(n%2==0?-1:1)*depth*.45f,h=.28f+(n%4)*.09f;
                        b.Box(new Vector3(x,h*.5f+.1f,z),new Vector3(.045f,h,.045f),FieldColor(n%3==0?"#B0B66B":"#7A9865"));
                    }
                },Vector3.zero);
            }else if(t.kind=="forest"){
                FieldMesh("Low forest floor",v.root,key+"-underbrush",b=>{
                    b.Box(new Vector3(0,.075f,0),new Vector3(width,.04f,depth),FieldColor(frost?"#748E83":"#4C6A46"));
                    for(int n=0;n<30;n++){
                        float x=((n*17)%29/28f-.5f)*width*.9f,z=((n*11)%31/30f-.5f)*depth*.9f;
                        b.Box(new Vector3(x,.12f,z),new Vector3(.22f,.11f,.24f),FieldColor(frost?"#BCD1C7":n%2==0?"#729359":"#5D7E4C"));
                    }
                },Vector3.zero);
                FieldMesh("Sparse edge pines keep troop silhouettes clear",v.root,key+"-trees",b=>{
                    for(int n=0;n<8;n++){
                        float x=(n/7f-.5f)*width*.9f,z=(n%2==0?-1:1)*depth*.44f,h=1.25f+(n%3)*.36f;
                        b.Box(new Vector3(x,h*.4f,z),new Vector3(.16f,h*.8f,.16f),FieldColor("#65523D"));
                        for(int tier=0;tier<3;tier++){
                            float w=.9f-tier*.2f;
                            b.Box(new Vector3(x,h*.6f+tier*.3f,z),new Vector3(w,.42f,w),FieldColor(frost?(tier==2?"#CADFD7":"#5B7C70"):(tier==2?"#83A65F":"#456D46")));
                            if(frost)b.Box(new Vector3(x,h*.6f+tier*.3f+.23f,z),new Vector3(w*.95f,.07f,w*.95f),FieldColor("#DBECE6"));
                        }
                    }
                },Vector3.zero);
                v.fire=FieldMesh("Forest fire warning embers",v.root,key+"-fire",b=>{
                    for(int n=0;n<14;n++){
                        float x=((n*7)%13/12f-.5f)*width*.85f,z=((n*5)%11/10f-.5f)*depth*.85f;
                        b.Box(new Vector3(x,.3f,z),new Vector3(.18f,.5f,.18f),FieldColor(n%2==0?"#FFC469":"#F27647"));
                    }
                },Vector3.zero,true);v.fireRenderer=v.fire.GetComponent<MeshRenderer>();v.fire.gameObject.SetActive(false);
            }else if(t.kind=="ruins"){
                v.standing=FieldMesh("Breakable low cover",v.root,key+"-walls",b=>{
                    Color stone=FieldColor(map.id=="bog"?"#7D8B71":"#A5A993");
                    for(int side=-1;side<=1;side+=2)for(int n=0;n<4;n++){
                        float x=(n/3f-.5f)*width*.8f,h=.55f+(n%3)*.19f;
                        b.Box(new Vector3(x,h*.5f+.08f,side*depth*.4f),new Vector3(width*.22f,h,.34f),stone*(.92f+n*.035f));
                        if(n%2==0)b.Box(new Vector3(x,h+.13f,side*depth*.4f),new Vector3(width*.23f,.12f,.39f),stone*1.12f);
                    }
                    b.Box(new Vector3(-width*.42f,.3f,0),new Vector3(.4f,.5f,depth*.8f),stone*.83f);
                },Vector3.zero);
                v.rubble=FieldMesh("Fallen cover rubble",v.root,key+"-rubble",b=>{
                    for(int n=0;n<15;n++){
                        float x=((n*7)%13/12f-.5f)*width,z=((n*5)%11/10f-.5f)*depth;
                        b.Box(new Vector3(x,.14f,z),new Vector3(.35f,.17f+(n%2)*.1f,.3f),FieldColor("#8E987F"),Quaternion.Euler(0,n*29,0));
                    }
                },Vector3.zero);
            }else if(t.kind=="chasm"){
                FieldPart("Continuous moat with four real bridge openings",v.root,FieldChasmSurface(key+"-chasm",t),Vector3.zero);
                FieldMesh("Moat cut stone lips",v.root,key+"-lips",b=>{
                    for(int n=0;n<64;n++){
                        float a=(n+.5f)*Mathf.PI/32;
                        foreach(float r in new[]{t.halfWidth,t.radius}){
                            float x=Mathf.Cos(a)*r,y=Mathf.Sin(a)*r;if(Mathf.Abs(x)<38||Mathf.Abs(y)<38)continue;
                            b.Box(FieldAt(x,y,.14f),new Vector3(.42f,.18f,.3f),FieldColor("#849181"),Quaternion.Euler(0,-a*Mathf.Rad2Deg,0));
                        }
                    }
                },Vector3.zero);
            }else if(t.kind=="lava"){
                v.fire=FieldMesh("Lava fissure",v.root,key+"-lava",b=>{
                    b.Box(new Vector3(0,.1f,0),new Vector3(width,.06f,depth),FieldColor("#512F2D"));
                    for(int n=0;n<20;n++)b.Box(new Vector3((n/19f-.5f)*width,.15f,Mathf.Sin(n*1.7f)*depth*.25f),new Vector3(width*.09f,.035f,.21f),FieldColor(n%3==0?"#FFD28A":"#EF7942"));
                },Vector3.zero,true);v.fireRenderer=v.fire.GetComponent<MeshRenderer>();
            }
            return v;
        }

        // Split at the actual four corner ridges and at the 25-unit plateau boundary.
        // Sampling min(edgeX,edgeZ) on an arbitrary diagonal grid cuts across those
        // ridges and shaves the corner slopes below the server's height field.
        Mesh FieldSlopeMesh(string key,TerrainState t,bool frost)
        {
            Mesh mesh;if(fieldMeshes.TryGetValue(key,out mesh))return mesh;
            var vertices=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            Color stone=FieldColor(frost?"#ADC4BE":t.id.StartsWith("mine-rise")?"#86916C":t.kind=="bridge"?"#999D84":"#A6AA91");
            float ramp=Mathf.Min(25,t.halfWidth,t.halfHeight),rise=t.height*ramp/25;
            Vector2[] outer={new Vector2(-t.halfWidth,-t.halfHeight),new Vector2(t.halfWidth,-t.halfHeight),new Vector2(t.halfWidth,t.halfHeight),new Vector2(-t.halfWidth,t.halfHeight)};
            Vector2[] inner={new Vector2(-t.halfWidth+ramp,-t.halfHeight+ramp),new Vector2(t.halfWidth-ramp,-t.halfHeight+ramp),new Vector2(t.halfWidth-ramp,t.halfHeight-ramp),new Vector2(-t.halfWidth+ramp,t.halfHeight-ramp)};
            for(int side=0;side<4;side++){
                int next=(side+1)%4;
                int nx=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(outer[side],outer[next])/12.5f)),nz=Mathf.Max(1,Mathf.CeilToInt(ramp/12.5f)),offset=vertices.Count;
                for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++){
                    float v=z/(float)nz,u=x/(float)nx;
                    Vector2 a=Vector2.Lerp(outer[side],inner[side],v),b=Vector2.Lerp(outer[next],inner[next],v),at=Vector2.Lerp(a,b,u);
                    // Match WorldSurfacePoint's 0.06 base exactly; it is also the
                    // surface used by clicking, creature feet and route projection.
                    vertices.Add(FieldAt(at.x,at.y,.06f+rise*v));colors.Add(stone*Mathf.Lerp(.84f,1.04f,v)*(.97f+Mathf.Sin(at.x*.02f+at.y*.01f)*.03f));
                    if(x<nx&&z<nz){int index=offset+z*(nx+1)+x;triangles.Add(index);triangles.Add(index+nx+1);triangles.Add(index+1);triangles.Add(index+1);triangles.Add(index+nx+1);triangles.Add(index+nx+2);}
                }
            }
            float innerWidth=t.halfWidth-ramp,innerDepth=t.halfHeight-ramp;
            if(innerWidth>.0001f&&innerDepth>.0001f){
                int nx=Mathf.Max(1,Mathf.CeilToInt(innerWidth*2/12.5f)),nz=Mathf.Max(1,Mathf.CeilToInt(innerDepth*2/12.5f)),offset=vertices.Count;
                for(int z=0;z<=nz;z++)for(int x=0;x<=nx;x++){
                    vertices.Add(FieldAt(Mathf.Lerp(-innerWidth,innerWidth,x/(float)nx),Mathf.Lerp(-innerDepth,innerDepth,z/(float)nz),.06f+rise));
                    colors.Add(stone*(1+.035f*Mathf.Sin(x*.15f+z*.21f)));
                    if(x<nx&&z<nz){int index=offset+z*(nx+1)+x;triangles.Add(index);triangles.Add(index+nx+1);triangles.Add(index+1);triangles.Add(index+1);triangles.Add(index+nx+1);triangles.Add(index+nx+2);}
                }
            }
            mesh=new Mesh{name="Walkable ramp "+key};mesh.SetVertices(vertices);mesh.SetColors(colors);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();fieldMeshes.Add(key,mesh);return mesh;
        }

        FieldObjectiveView BuildFieldObjective(FieldMapView map,ObjectiveState objective)
        {
            FieldObjectiveView v=new FieldObjectiveView{id=objective.id,state=objective,x=objective.x,y=objective.y};
            v.root=NewRoot(objective.name,map.root);v.root.localPosition=FieldAt(objective.x,objective.y,.1f);
            string key=map.geometryKey+"-objective-"+objective.id;
            FieldMesh("Objective plinth",v.root,key+"-base",b=>{
                b.Disc(.85f,0,.14f,8,FieldColor("#87968A"),FieldColor("#56685E"));
                b.Disc(.63f,.14f,.23f,8,FieldColor("#B9C7AA"),FieldColor("#819481"));
            },Vector3.zero);
            v.contents=NewRoot("Claimable structure",v.root);
            FieldMesh("Unique objective silhouette",v.contents,key+"-structure",b=>{
                Color dark=FieldColor("#59665D"),light=FieldColor("#BECDA9"),wood=FieldColor("#795D41");
                if(objective.kind=="tower"){
                    for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2)b.Box(new Vector3(sx*.38f,.95f,sz*.38f),new Vector3(.13f,1.65f,.13f),wood);
                    b.Box(new Vector3(0,1.7f,0),new Vector3(1.05f,.18f,1.05f),light);
                    b.Box(new Vector3(0,2.02f,.46f),new Vector3(1.05f,.5f,.12f),wood);b.Box(new Vector3(0,2.02f,-.46f),new Vector3(1.05f,.5f,.12f),wood);
                    b.Box(new Vector3(0,2.4f,0),new Vector3(1.3f,.16f,1.3f),dark);
                }else if(objective.kind=="mine"){
                    for(int n=0;n<7;n++){
                        float a=n*Mathf.PI*2/7;Vector3 p=new Vector3(Mathf.Cos(a)*.4f,.34f+(n%3)*.13f,Mathf.Sin(a)*.4f);
                        b.Box(p,new Vector3(.47f,.44f,.42f),n%2==0?FieldColor("#9DA28A"):dark,Quaternion.Euler(0,n*27,0));
                        b.Box(p+new Vector3(.08f,.24f,-.09f),new Vector3(.16f,.09f,.13f),FieldColor("#E8C96C"));
                    }
                }else if(objective.kind=="supply"){
                    b.Box(new Vector3(0,.47f,0),new Vector3(.94f,.5f,.68f),wood);b.Box(new Vector3(0,.77f,0),new Vector3(1.02f,.15f,.74f),FieldColor("#AB8752"));
                    for(int sign=-1;sign<=1;sign+=2)b.Box(new Vector3(sign*.28f,.5f,0),new Vector3(.1f,.62f,.78f),FieldColor("#5A716F"));
                    b.Box(new Vector3(0,.56f,-.39f),new Vector3(.15f,.18f,.06f),FieldColor("#F2D37C"));
                }else{
                    for(int sign=-1;sign<=1;sign+=2){b.Box(new Vector3(sign*.45f,.6f,0),new Vector3(.19f,.8f,.32f),dark);b.Box(new Vector3(sign*.45f,1.07f,0),new Vector3(.34f,.14f,.43f),light);}
                    b.Box(new Vector3(0,.47f,0),new Vector3(.62f,.32f,.62f),FieldColor("#A3B89D"));
                    if(objective.kind=="beacon")b.Box(new Vector3(0,.82f,0),new Vector3(.43f,.45f,.43f),FieldColor("#90DBE7"));
                }
            },Vector3.zero);
            v.ring=FieldMesh("Ownership ring",v.root,"objective-ring",b=>b.Ring(1.15f,1.05f,Color.white),new Vector3(0,.045f,0),true);
            v.ringRenderer=v.ring.GetComponent<MeshRenderer>();
            v.progress=FieldMesh("Capture progress",v.root,"objective-progress",b=>{
                for(int n=0;n<24;n++){
                    float a=n*Mathf.PI/12;b.Box(new Vector3(Mathf.Cos(a)*1.3f,0,Mathf.Sin(a)*1.3f),new Vector3(.1f,.025f,.1f),Color.white);
                }
            },new Vector3(0,.065f,0),true);v.progressRenderer=v.progress.GetComponent<MeshRenderer>();
            v.icon=FieldMesh("Objective floating rune",v.root,"objective-rune-"+objective.kind,b=>{
                Color c=Color.white;
                if(objective.kind=="mine")b.Box(Vector3.zero,new Vector3(.22f,.3f,.22f),c,Quaternion.Euler(0,45,25));
                else if(objective.kind=="tower"){b.Ring(.22f,.16f,c);b.Box(Vector3.zero,new Vector3(.065f,.4f,.065f),c);}
                else {b.Box(Vector3.zero,new Vector3(.13f,.42f,.13f),c);b.Box(Vector3.zero,new Vector3(.36f,.1f,.1f),c);}
            },new Vector3(0,objective.kind=="tower"?2.95f:1.5f,0),true);v.iconRenderer=v.icon.GetComponent<MeshRenderer>();
            return v;
        }

        FieldMarker GetFieldMarker(int index)
        {
            while(fieldMarkers.Count<=index&&fieldMarkers.Count<FieldMarkerBudget){
                FieldMarker m=new FieldMarker();m.root=NewRoot("Pooled weather or command telegraph",fieldMarkerRoot);
                m.ring=FieldMesh("Warning outline",m.root,"marker-ring",b=>b.Ring(1,.955f,Color.white),Vector3.zero,true);m.ringRenderer=m.ring.GetComponent<MeshRenderer>();
                m.fill=FieldMesh("Transparent danger area",m.root,"marker-fill",b=>b.Disc(.96f,0,.001f,32,Color.white,Color.white),new Vector3(0,-.004f,0),true);m.fillRenderer=m.fill.GetComponent<MeshRenderer>();
                m.beam=FieldMesh("Impact or command pillar",m.root,"marker-beam",b=>b.Box(Vector3.zero,Vector3.one,Color.white),Vector3.zero,true);m.beamRenderer=m.beam.GetComponent<MeshRenderer>();
                m.root.gameObject.SetActive(false);fieldMarkers.Add(m);
            }
            return index<fieldMarkers.Count?fieldMarkers[index]:null;
        }

        void SyncFieldMarkers()
        {
            int count=0;
            for(int i=0;i<fieldState.hazards.Count&&count<FieldMarkerBudget;i++){
                HazardState h=fieldState.hazards[i];FieldMarker m=GetFieldMarker(count++);
                m.kind=h.kind;m.zone=false;m.x=h.x;m.y=h.y;m.radius=Mathf.Max(.7f,h.radius*WorldScale);m.delay=h.delay;m.remaining=h.remaining;m.triggered=h.triggered;
                m.color=FieldColor(h.kind=="lightning"?"#E1CEFF":h.kind=="rift"?"#BAA0FF":h.kind=="trap"?"#E5C578":"#FFB16E");m.root.gameObject.SetActive(true);
            }
            for(int i=0;i<fieldState.zones.Count&&count<FieldMarkerBudget;i++){
                CommandZoneState z=fieldState.zones[i];FieldMarker m=GetFieldMarker(count++);
                m.kind=z.kind;m.zone=true;m.x=z.x;m.y=z.y;m.radius=Mathf.Max(.65f,z.radius*WorldScale);m.delay=0;m.remaining=z.remaining;m.triggered=false;
                m.color=z.kind=="ward"?FieldColor("#8CD9F5"):z.kind=="cleanse"?FieldColor("#AEF4C7"):z.kind=="smoke"?FieldColor("#B4C6C3"):z.kind=="focus"?FieldColor("#FF9E88"):Teams[Mathf.Clamp(z.slot,0,3)];
                m.root.gameObject.SetActive(true);
            }
            for(int i=count;i<fieldMarkers.Count;i++)fieldMarkers[i].root.gameObject.SetActive(false);
            fieldMarkerCount=count;
        }

        void UpdateBattlefieldPresentation(float dt,float time)
        {
            if(!fieldVisible||fieldState==null||fieldMap==null)return;
            dt=Mathf.Clamp(dt,0,.08f);
            fieldRevealProgress=FieldRevealProgress(Time.unscaledTime-fieldSnapshotTime);
            string revealKey=FieldRevealKey();
            if(fieldPhase=="reveal"&&FieldEmergenceAge>=ArenaGame.RevealTerrainSeconds-.001f&&fieldRiseSoundKey==revealKey&&fieldSettleSoundKey!=revealKey){
                fieldSettleSoundKey=revealKey;fieldSettleAt=time;PlayFieldRevealSound("map_lock",.7f);
            }
            ApplyFieldEmergence();
            for(int i=0;i<fieldMap.terrain.Count;i++){
                FieldTerrainView v=fieldMap.terrain[i];TerrainState t=v.state;if(t==null)continue;
                if(v.standing!=null){
                    float hp=t.maxHp>0?Mathf.Clamp01(t.hp/t.maxHp):1;
                    v.standing.gameObject.SetActive(hp>0);v.standing.localScale=new Vector3(1,.38f+.62f*hp,1);
                    v.standing.localRotation=Quaternion.Euler(0,0,(1-hp)*6);
                    v.rubble.localScale=new Vector3(1.1f-hp*.25f,1,1.1f-hp*.25f);
                }
                if(v.water!=null){
                    UpdateFieldIce(v,t,time,i);
                }
                if(v.fire!=null){
                    bool burning=t.kind=="lava"||t.burnTime>0;v.fire.gameObject.SetActive(burning);
                    if(burning){v.fire.localScale=new Vector3(1,.8f+Mathf.Sin(time*9+i)*.2f,1);TintField(v.fireRenderer,new Color(1,1,1,.68f+Mathf.Sin(time*13)*.12f));}
                }
            }
            for(int i=0;i<fieldMap.objectives.Count;i++){
                FieldObjectiveView v=fieldMap.objectives[i];ObjectiveState o=v.state;if(o==null)continue;
                Color owner=o.slot>=0&&o.slot<4?Teams[o.slot]:FieldColor("#F1DBA1");
                TintField(v.ringRenderer,new Color(owner.r,owner.g,owner.b,.65f));TintField(v.iconRenderer,new Color(owner.r,owner.g,owner.b,.9f));
                float capture=Mathf.Clamp01(o.capture);v.progress.gameObject.SetActive(capture>.005f&&!o.claimed);
                v.progress.localScale=Vector3.one*(.86f+capture*.14f);TintField(v.progressRenderer,new Color(owner.r,owner.g,owner.b,.25f+capture*.65f));
                v.ring.localScale=Vector3.one*(1+Mathf.Sin(time*2+i)*.025f);
                v.icon.localPosition=new Vector3(0,(o.kind=="tower"?2.95f:1.5f)+Mathf.Sin(time*2+i)*.09f,0);
                v.icon.localRotation=Quaternion.Euler(0,time*28+i*21,0);
                if(o.kind=="supply")v.contents.gameObject.SetActive(!o.claimed);
                v.icon.gameObject.SetActive(!o.claimed);
            }
            for(int i=0;i<fieldMarkerCount;i++)UpdateFieldMarker(fieldMarkers[i],time,i);
            UpdateFieldLighting(dt);UpdateFieldWeather(dt,time);UpdateFieldEnvironment(dt,time,FieldEnvironmentRise);
        }

        void UpdateFieldMarker(FieldMarker m,float time,int index)
        {
            float since=Time.unscaledTime-fieldSnapshotTime,delay=Mathf.Max(0,m.delay-since),remaining=Mathf.Max(0,m.remaining-since);
            bool strike=m.triggered||(!m.zone&&m.kind!="trap"&&m.kind!="rift"&&delay<=0);
            float pulse=.65f+Mathf.Sin(time*(strike?24:8)+index)*.25f;
            m.root.localPosition=FieldAt(m.x,m.y,BattlefieldVisualHeight(m.x,m.y)+.16f);
            m.ring.localScale=new Vector3(m.radius,1,m.radius);m.fill.localScale=new Vector3(m.radius,1,m.radius);
            Color c=m.color;TintField(m.ringRenderer,new Color(c.r,c.g,c.b,m.zone ? .55f : Mathf.Clamp01(pulse)));
            TintField(m.fillRenderer,new Color(c.r,c.g,c.b,m.zone ? .055f : strike ? .24f : .055f+pulse*.055f));
            bool beam=(strike&&(m.kind=="lightning"||m.kind=="eruption"))||(m.zone&&m.kind=="ward")||m.kind=="rift";
            m.beam.gameObject.SetActive(beam);
            if(beam){
                bool lightning=m.kind=="lightning";float height=lightning?11:m.kind=="ward"?1.8f:m.kind=="rift"?2.3f:3.2f;
                float width=lightning ? .18f : m.kind=="ward" ? m.radius*1.8f : m.kind=="rift" ? .55f : .65f;
                m.beam.localPosition=new Vector3(0,height*.5f,0);m.beam.localScale=new Vector3(width,height,width);
                m.beam.localRotation=Quaternion.Euler(0,m.kind=="rift"?time*100:0,lightning?Mathf.Sin(time*35)*4:0);
                TintField(m.beamRenderer,new Color(c.r,c.g,c.b,lightning ? .65f : m.kind=="ward" ? .035f : .22f));
            }
            if(remaining<=0&&m.triggered){TintField(m.ringRenderer,Color.clear);TintField(m.fillRenderer,Color.clear);m.beam.gameObject.SetActive(false);}
        }

        void SaveFieldLighting()
        {
            if(fieldLightingSaved)return;fieldLightingSaved=true;
            fieldOriginalSky=RenderSettings.ambientSkyColor;fieldOriginalEquator=RenderSettings.ambientEquatorColor;fieldOriginalGround=RenderSettings.ambientGroundColor;
            fieldOriginalFog=RenderSettings.fogColor;fieldOriginalFogStart=RenderSettings.fogStartDistance;fieldOriginalFogEnd=RenderSettings.fogEndDistance;
            if(viewCamera!=null)fieldOriginalBackground=viewCamera.backgroundColor;
            Transform sun=transform.Find("Warm afternoon sun");if(sun!=null)fieldSun=sun.GetComponent<Light>();
            if(fieldSun!=null){fieldOriginalSun=fieldSun.color;fieldOriginalSunIntensity=fieldSun.intensity;}
        }
        void RestoreFieldLighting()
        {
            if(!fieldLightingSaved)return;fieldLightingSaved=false;
            RenderSettings.ambientSkyColor=fieldOriginalSky;RenderSettings.ambientEquatorColor=fieldOriginalEquator;RenderSettings.ambientGroundColor=fieldOriginalGround;
            RenderSettings.fogColor=fieldOriginalFog;RenderSettings.fogStartDistance=fieldOriginalFogStart;RenderSettings.fogEndDistance=fieldOriginalFogEnd;
            if(viewCamera!=null)viewCamera.backgroundColor=fieldOriginalBackground;
            if(fieldSun!=null){fieldSun.color=fieldOriginalSun;fieldSun.intensity=fieldOriginalSunIntensity;}
        }
        void UpdateFieldLighting(float dt)
        {
            Color fog=fieldOriginalFog,sky=fieldOriginalSky,equator=fieldOriginalEquator,sun=fieldOriginalSun;
            float start=fieldOriginalFogStart,end=fieldOriginalFogEnd,intensity=fieldOriginalSunIntensity;
            switch(fieldWeatherId){
                case "rain":fog=FieldColor("#506D72");sky=FieldColor("#9EB6BB");sun=FieldColor("#C1D9DC");intensity=.75f;start=38;end=98;break;
                case "storm":fog=FieldColor("#465066");sky=FieldColor("#9AA6BA");sun=FieldColor("#B4C5ED");intensity=.62f;start=36;end=92;break;
                case "fog":fog=FieldColor("#8BABA6");sky=FieldColor("#B6CDC6");sun=FieldColor("#D3E5D7");intensity=.7f;start=28;end=83;break;
                case "snow":fog=FieldColor("#839FAC");sky=FieldColor("#BCD8E0");equator=FieldColor("#9FBABD");sun=FieldColor("#D7EAFA");intensity=.85f;start=40;end=105;break;
                case "heat":fog=FieldColor("#9D805F");sky=FieldColor("#E0C49A");sun=FieldColor("#FFD79A");intensity=1.2f;start=52;end=116;break;
                case "sandstorm":fog=FieldColor("#AD9876");sky=FieldColor("#D4C2A0");sun=FieldColor("#E4CA9D");intensity=.82f;start=29;end=87;break;
            }
            float distanceScale=Mathf.Lerp(1,1.8f,Mathf.Clamp01(ExpansionScale-1));start*=distanceScale;end*=distanceScale;
            float blend=1-Mathf.Exp(-dt*2.5f);
            RenderSettings.ambientSkyColor=Color.Lerp(RenderSettings.ambientSkyColor,sky,blend);RenderSettings.ambientEquatorColor=Color.Lerp(RenderSettings.ambientEquatorColor,equator,blend);
            RenderSettings.fogColor=Color.Lerp(RenderSettings.fogColor,fog,blend);RenderSettings.fogStartDistance=Mathf.Lerp(RenderSettings.fogStartDistance,start,blend);RenderSettings.fogEndDistance=Mathf.Lerp(RenderSettings.fogEndDistance,end,blend);
            if(viewCamera!=null)viewCamera.backgroundColor=Color.Lerp(viewCamera.backgroundColor,fog,blend);
            if(fieldSun!=null){fieldSun.color=Color.Lerp(fieldSun.color,sun,blend);fieldSun.intensity=Mathf.Lerp(fieldSun.intensity,intensity,blend);}
        }

        void UpdateFieldWeather(float dt,float time)
        {
            if(fieldWeather==null||fieldWeatherId=="clear")return;
            bool rain=fieldWeatherId=="rain"||fieldWeatherId=="storm",snow=fieldWeatherId=="snow",fog=fieldWeatherId=="fog",sand=fieldWeatherId=="sandstorm";
            float radiusScale=fieldMap==null?1:fieldMap.scale;
            float rate=(rain?360:snow?120:fog?26:sand?110:35)*Mathf.Min(2,radiusScale);
            fieldWeatherEmission+=dt*rate*fieldRevealProgress;
            int count=Mathf.Min(30,Mathf.FloorToInt(fieldWeatherEmission));fieldWeatherEmission-=count;
            count=Mathf.Min(count,FieldWeatherBudget-fieldWeather.particleCount);
            for(int i=0;i<count;i++){
                float angle=UnityEngine.Random.value*Mathf.PI*2,radius=Mathf.Sqrt(UnityEngine.Random.value)*23*radiusScale;
                Vector3 at=new Vector3(Mathf.Cos(angle)*radius,rain?12:snow?7:UnityEngine.Random.Range(.2f,.7f),Mathf.Sin(angle)*radius);
                ParticleSystem.EmitParams p=new ParticleSystem.EmitParams();p.position=at;p.applyShapeToPosition=false;
                if(rain){p.velocity=new Vector3(-1.5f,-19,-.8f);p.startLifetime=.7f;p.startSize3D=new Vector3(.022f,.62f,.022f);p.startColor=new Color(.65f,.83f,.91f,.43f);}
                else if(snow){p.velocity=new Vector3(.18f+Mathf.Sin(time)*.15f,-.7f,.14f);p.startLifetime=10;p.startSize3D=Vector3.one*UnityEngine.Random.Range(.04f,.085f);p.startColor=new Color(.88f,.97f,1,.72f);p.rotation3D=new Vector3(i*19,0,i*37);}
                else if(fog){p.velocity=new Vector3(.35f,.006f,.11f);p.startLifetime=9;p.startSize3D=new Vector3(UnityEngine.Random.Range(1.2f,2.3f),.12f,UnityEngine.Random.Range(.8f,1.6f));p.startColor=new Color(.75f,.86f,.82f,.065f);}
                else if(sand){p.velocity=new Vector3(2.2f,.06f,.4f);p.startLifetime=5;p.startSize3D=new Vector3(.12f,.035f,.06f);p.startColor=new Color(.88f,.74f,.49f,.29f);}
                else {p.velocity=new Vector3(.1f,.3f,.05f);p.startLifetime=4;p.startSize3D=Vector3.one*.045f;p.startColor=new Color(1,.65f,.34f,.35f);}
                fieldWeather.Emit(p,1);
            }
        }
    }
}
