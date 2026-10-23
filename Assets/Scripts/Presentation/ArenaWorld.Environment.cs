using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        const int GroundWeatherBudget=320, BiomeMoteBudget=260;
        ParticleSystem groundWeather,biomeMotes;
        readonly HashSet<string> environmentStrikes=new HashSet<string>();
        string environmentRound;
        float groundWeatherCarry,biomeCarry,lightningFlash;
        internal int EnvironmentParticleCount { get { return (groundWeather==null?0:groundWeather.particleCount)+(biomeMotes==null?0:biomeMotes.particleCount); } }
        internal int EnvironmentStrikeCount { get { return environmentStrikes.Count; } }

        void BuildFieldEnvironment(FieldMapView map,BattlefieldState source)
        {
            bool frost=map.id=="frost",bog=map.id=="bog";
            map.environment=NewRoot("Retained biome details",map.root);
            // Large detail batches are retained with the map; no per-frame mesh building.
            FieldMesh("Weathered arena strata and biome border",map.environment,map.geometryKey+"-strata",b=>{
                Color rock=FieldColor(frost?"#647E88":bog?"#596651":"#817D6A");
                for(int n=0;n<80;n++){
                    float a=(n+.5f)*Mathf.PI*2/80,r=LandscapeRadius(map.scale)-.65f;
                    if(Mathf.Abs(Mathf.Sin(a*2))<.17f)continue;
                    Vector3 p=new Vector3(Mathf.Cos(a)*r,.1f,Mathf.Sin(a)*r);
                    Quaternion q=Quaternion.Euler(0,-a*Mathf.Rad2Deg,0);
                    float h=.08f+(n%4)*.055f;
                    b.Box(p,new Vector3(.65f,h,.42f),rock*(.88f+(n%5)*.04f),q);
                    if(frost)b.Box(p+Vector3.up*h*.55f,new Vector3(.71f,.055f,.47f),FieldColor("#D8E9E5"),q);
                    else if(bog)b.Box(p+new Vector3(.19f,h*.55f,0),new Vector3(.34f,.035f,.29f),FieldColor("#7F965D"),q);
                }
            },Vector3.zero);
            for(int index=0;index<source.terrain.Count;index++){
                TerrainState terrain=source.terrain[index];
                if(terrain.kind!="forest"&&terrain.kind!="water"&&terrain.kind!="wetland"&&terrain.kind!="bridge"&&terrain.kind!="highland")continue;
                if(!string.IsNullOrEmpty(terrain.shape))continue;
                if(terrain.kind=="highland"&&map.id!="courtyards")continue;
                TerrainState t=terrain;string key=map.geometryKey+"-"+t.id+"-finish";
                FieldMesh("Biome detail "+t.id,map.environment,key,b=>{
                    float w=t.halfWidth*WorldScale,d=t.halfHeight*WorldScale;
                    Vector3 center=FieldAt(t.x,t.y);
                    if(t.kind=="forest"){
                        for(int n=0;n<44;n++){
                            float x=((n*19)%43/42f-.5f)*w*1.8f,z=((n*23)%47/46f-.5f)*d*1.8f;
                            Vector3 p=center+new Vector3(x,.13f,z);
                            if(frost){
                                b.Box(p,new Vector3(.45f+(n%3)*.12f,.07f,.25f),FieldColor(n%2==0?"#C3DBD6":"#E0ECE7"),Quaternion.Euler(0,n*37,0));
                            }else{
                                b.Box(p+Vector3.up*.07f,new Vector3(.055f,.18f,.055f),FieldColor("#C7B48B"));
                                b.Box(p+Vector3.up*.17f,new Vector3(.17f,.055f,.17f),FieldColor(n%3==0?"#CF8D7B":"#A6B989"));
                                if(n%3==0)b.Box(p+new Vector3(.17f,.035f,.07f),new Vector3(.19f,.045f,.24f),FieldColor("#829B59"));
                            }
                        }
                    }else if(t.kind=="water"||t.kind=="wetland"){
                        for(int n=0;n<24;n++){
                            float x=(n/23f-.5f)*w*1.85f,z=(n%2==0?-1:1)*d*.96f;
                            Vector3 p=center+new Vector3(x,.12f,z);
                            b.Box(p,new Vector3(.32f+(n%3)*.13f,.08f,.2f),FieldColor(frost?"#B4CBD0":"#72846A"));
                            if(!frost&&n%3==0){
                                b.Box(p+new Vector3(0,.16f,-.1f),new Vector3(.04f,.38f,.04f),FieldColor("#879D62"));
                                b.Box(p+new Vector3(0,.37f,-.1f),new Vector3(.075f,.15f,.075f),FieldColor("#B2A56C"));
                            }
                        }
                    }else{
                        // Inlays follow the actual plateau surface, never replace its collision.
                        for(int n=0;n<24;n++){
                            float x=((n*7)%23/22f-.5f)*Mathf.Max(.1f,w*2-2.15f);
                            float z=((n*11)%29/28f-.5f)*Mathf.Max(.1f,d*2-2.15f);
                            float h=BattlefieldCatalog.HeightAt(source,t.x+x/WorldScale,t.y+z/WorldScale);
                            b.Box(center+new Vector3(x,.071f+h,z),new Vector3(.25f+(n%3)*.18f,.012f,.032f),FieldColor(frost?"#CAE5E9":"#C3BC97"),Quaternion.Euler(0,n%2*90,0));
                        }
                    }
                },Vector3.zero);
            }
            BuildGrandLandscape(map,source);
        }

        ParticleSystem MakeEnvironmentParticles(string name,int budget,Mesh mesh)
        {
            var g=new GameObject(name);g.transform.SetParent(fieldRoot,false);
            var particles=g.AddComponent<ParticleSystem>();
            var main=particles.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=budget;main.startSpeed=0;main.startSize3D=true;
            var emission=particles.emission;emission.enabled=false;var shape=particles.shape;shape.enabled=false;
            var over=particles.colorOverLifetime;over.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(0,1)});
            over.color=new ParticleSystem.MinMaxGradient(gradient);
            var renderer=g.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
            renderer.mesh=mesh;renderer.sharedMaterial=fieldMaterial;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            particles.Play();return particles;
        }
        void EnsureEnvironmentParticles()
        {
            if(groundWeather==null)groundWeather=MakeEnvironmentParticles("Rain impacts and icy surface shimmer",GroundWeatherBudget,
                VoxelAssets.Cached("environment-ripple",b=>b.Ring(1,.77f,Color.white)));
            if(biomeMotes==null)biomeMotes=MakeEnvironmentParticles("Local leaves, snow dust and marsh fireflies",BiomeMoteBudget,VoxelAssets.Block(Color.white));
        }
        void ResetFieldEnvironment()
        {
            if(groundWeather!=null)groundWeather.Clear();if(biomeMotes!=null)biomeMotes.Clear();
            groundWeatherCarry=biomeCarry=lightningFlash=0;environmentRound=null;environmentStrikes.Clear();
        }
        void UpdateFieldEnvironment(float dt,float time,float rise)
        {
            EnsureEnvironmentParticles();
            string key=state.round+":"+fieldState.seed+":"+fieldMap.id;
            if(environmentRound!=key){ResetFieldEnvironment();environmentRound=key;}
            bool rain=fieldWeatherId=="rain"||fieldWeatherId=="storm",snow=fieldWeatherId=="snow";
            groundWeatherCarry+=dt*(rain?85:snow?10:0)*rise;
            int count=Mathf.Min(10,Mathf.FloorToInt(groundWeatherCarry));groundWeatherCarry-=count;
            for(int i=0;i<count&&groundWeather.particleCount<GroundWeatherBudget;i++){
                float r=Mathf.Sqrt(UnityEngine.Random.value)*22*fieldMap.scale,a=UnityEngine.Random.value*Mathf.PI*2;
                float x=Mathf.Cos(a)*r,y=Mathf.Sin(a)*r;
                float h=BattlefieldVisualHeight(x/WorldScale,y/WorldScale);
                var p=new ParticleSystem.EmitParams {position=new Vector3(x,h+.085f,y),velocity=Vector3.zero,
                    startLifetime=rain?.45f:1.1f,startSize3D=new Vector3(.14f,.035f,.14f),startColor=rain?new Color(.65f,.87f,.94f,.3f):new Color(.8f,.95f,1,.25f)};
                groundWeather.Emit(p,1);
            }
            biomeCarry+=dt*(fieldMap.id=="bog"?13:9)*rise;
            int motes=Mathf.Min(4,Mathf.FloorToInt(biomeCarry));biomeCarry-=motes;
            for(int i=0;i<motes&&fieldState.terrain.Count>0&&biomeMotes.particleCount<BiomeMoteBudget;i++){
                TerrainState forest=null;
                int start=UnityEngine.Random.Range(0,fieldState.terrain.Count);
                for(int t=0;t<fieldState.terrain.Count;t++){var candidate=fieldState.terrain[(start+t)%fieldState.terrain.Count];if(candidate.kind=="forest"||candidate.kind=="wetland"){forest=candidate;break;}}
                if(forest==null)break;
                Point2 sample=BattlefieldCatalog.TerrainPoint(forest,UnityEngine.Random.Range(-forest.halfWidth,forest.halfWidth),UnityEngine.Random.Range(-forest.halfHeight,forest.halfHeight));
                float x=sample.x,y=sample.y;
                if(!BattlefieldCatalog.Contains(forest,x,y))continue;
                bool firefly=fieldMap.id=="bog"&&fieldWeatherId!="heat";
                var p=new ParticleSystem.EmitParams {position=FieldAt(x,y,firefly?.45f:2.1f),
                    velocity=firefly?new Vector3(Mathf.Sin(time+i)*.11f,.035f,Mathf.Cos(time+i)*.11f):new Vector3(.3f,-.25f,.14f),
                    startLifetime=firefly?3:5,startSize3D=Vector3.one*(firefly?.045f:.06f),
                    startColor=firefly?new Color(.7f,1,.45f,.7f):fieldMap.id=="frost"?new Color(.83f,.96f,1,.42f):new Color(.7f,.83f,.4f,.5f)};
                biomeMotes.Emit(p,1);
            }
            foreach(var hazard in fieldState.hazards){
                if(hazard.kind!="lightning"||!hazard.triggered||!environmentStrikes.Add(hazard.id))continue;
                // Joining in the middle of an old impact does not replay the thunder.
                if(hazard.remaining>.03f){lightningFlash=.23f;if(ThunderCue!=null)ThunderCue();}
            }
            lightningFlash=Mathf.Max(0,lightningFlash-dt);
            if(lightningFlash>0&&fieldSun!=null){float flash=lightningFlash/.23f;fieldSun.intensity=Mathf.Max(fieldSun.intensity,.65f+flash*.7f);RenderSettings.ambientSkyColor=Color.Lerp(RenderSettings.ambientSkyColor,new Color(.78f,.85f,1),flash*.15f);}
        }
        public System.Action ThunderCue;
    }
}
