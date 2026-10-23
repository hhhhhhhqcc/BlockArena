using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BlockArena.Core;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Opt-in rendered-player regression. Resolution is supplied by the launcher.
    public sealed partial class ArenaExpansionWorldProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool success;
            public int width,height,maxEnvironmentParticles,maxWeatherParticles;
            public string error,unityVersion,graphicsDevice;
            public List<string> checks=new List<string>();
            public List<string> captures=new List<string>();
        }
        readonly Report report=new Report();
        ArenaApp app;
        string folder;
        bool failed,volumesSaved;
        float music,sfx;
        bool muted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-expansion-test")>=0)
                new GameObject("Native expanded arena world verification").AddComponent<ArenaExpansionWorldProbe>();
        }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){
                IEnumerator current=stack.Peek();bool moved=false;object next=null;
                try{moved=current.MoveNext();if(moved)next=current.Current;}catch(Exception e){Fail(e.ToString());}
                if(failed)yield break;
                if(!moved){stack.Pop();continue;}
                if(next is IEnumerator){stack.Push((IEnumerator)next);continue;}
                yield return next;
            }
        }
        IEnumerator Run()
        {
            folder=Path.Combine(Application.dataPath,"..","Captures","ExpansionWorld");
            string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);report.width=Screen.width;report.height=Screen.height;
            report.unityVersion=Application.unityVersion;report.graphicsDevice=SystemInfo.graphicsDeviceName;
            yield return new WaitForSecondsRealtime(.5f);
            app=ArenaApp.Instance;Require(app!=null&&app.World!=null,"native arena world initialized");
            app.VerificationInput=true;app.VerificationPointer=new Vector2(-1000,-1000);
            app.PortText=FreePort().ToString();app.Host(false);
            float deadline=Time.realtimeSinceStartup+12;
            while(!app.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Connected,"actual local TCP host and client establish before rendered fixtures");
            app.ShowRoom=false;
            Require(app.Sound!=null&&app.Ambience!=null,"native sound and independent ambience components initialized");
            music=app.Sound.MusicVolume;sfx=app.Sound.SfxVolume;muted=app.Sound.Muted;volumesSaved=true;
            app.Sound.SetVolumes(music,.7f,false);

            var baseline=AuthoritativeFixture("courtyards","clear",29);
            var preparation=PreparationFixture(baseline);Show(preparation);yield return Settle(.6f);
            VoxelCreature original=Find(preparation.players[0].units[0].id);
            Require(original!=null,"preparation fixture contains the server-owned creature");
            float originalSize=original.Size;
            Transform floor=app.World.transform.Find("Arena scenery/Octagonal arena/Meadow and pale stone crossing");
            Require(floor!=null,"shipping meadow floor mesh exists");
            Vector3 unexpandedFloor=floor.GetComponent<Renderer>().bounds.size;

            foreach(string map in BattlefieldCatalog.Maps){
                var source=AuthoritativeFixture(map,map=="bog"?"rain":map=="frost"?"snow":"clear",29);
                var reveal=Clone(source);reveal.phase="reveal";reveal.revealStage="map";reveal.revealStageAge=0;reveal.battlefield.revealAge=0;
                Show(reveal);yield return Settle(.45f);
                Require(Mathf.Abs(app.World.ExpansionScale-1)<.001f,map+" preserves ordinary arena size until map emergence");
                VerifyDeployments(reveal,false);
                reveal.revealStage="terrain";reveal.revealStageAge=.7f;reveal.battlefield.revealAge=.7f;Show(reveal);
                yield return Settle(.25f);
                Require(app.World.ExpansionScale>1&&app.World.ExpansionScale<2,map+" grows smoothly during terrain reveal");
                VerifyMovingFormationTargets(reveal);
                VerifyFoundationHidden(source);
                Capture("map-"+map+"-01-emerging");
                yield return VerifyLayeredEmergence(reveal);
                Show(source);app.World.ResetView();yield return Settle(1.2f);
                Require(Mathf.Abs(app.World.ExpansionScale-2)<.001f,map+" settles at double width and depth / four times area");
                Vector3 expanded=floor.GetComponent<Renderer>().bounds.size;
                Require(Mathf.Abs(expanded.x/unexpandedFloor.x-2)<.001f&&Mathf.Abs(expanded.z/unexpandedFloor.z-2)<.001f&&Mathf.Abs(expanded.y-unexpandedFloor.y)<.001f,map+" expands the physical floor horizontally without inflating its height");
                Require(app.World.FieldTerrainCount==source.battlefield.terrain.Count&&app.World.FieldObjectiveCount==source.battlefield.objectives.Count,map+" retains all terrain and objective geometry");
                Require(app.World.FieldGeometryCount>source.battlefield.terrain.Count+source.battlefield.objectives.Count,map+" includes layered biome detail geometry");
                VerifyLandscapeColor(source);
                VerifyCircularFloor(source);
                VerifyDeployments(source,true);VerifyOverview(source);
                Capture("map-"+map+"-02-overview");VerifyGroundPicking(source);
                Require(Mathf.Abs(Find(source.players[0].units[0].id).Size-originalSize)<.0001f,map+" leaves creature model scale unchanged");
                float size=app.World.ViewCamera.orthographicSize;
                app.World.Zoom(6,new Vector2(Screen.width*.5f,Screen.height*.5f));yield return Settle(.75f);
                Require(app.World.ZoomFactor<.6f&&app.World.ViewCamera.orthographicSize<size*.7f,map+" mouse-wheel zoom reaches close tactical view");
                Vector3 before=app.World.ViewCamera.transform.position;
                app.World.Pan(new Vector2(7,-4));yield return Settle(.75f);
                Require(Vector3.Distance(before,app.World.ViewCamera.transform.position)>.2f,map+" camera panning remains responsive after expansion");
                VerifyGroundPicking(source);Capture("map-"+map+"-03-zoom-and-pan");
                app.World.ResetView();
            }

            yield return VerifyContinuousExpansion();
            yield return VerifyProgressiveIce();
            yield return VerifyClimateIceContinuity();

            foreach(string weather in BattlefieldCatalog.Weathers){
                string map=weather=="snow"?"frost":weather=="rain"||weather=="fog"||weather=="heat"?"bog":"courtyards";
                var state=AuthoritativeFixture(map,weather,61);state.phase="battle";
                foreach(var owner in state.players)foreach(var unit in owner.units){var f=ArenaGame.PreviewFighter(owner,unit);f.id=unit.id;f.unitId=unit.id;f.ownerId=owner.id;f.slot=owner.slot;state.fighters.Add(f);}
                if(weather=="snow")foreach(var terrain in state.battlefield.terrain)if(terrain.kind=="water")terrain.freeze=1;
                if(weather=="storm")state.battlefield.hazards.Add(new HazardState{id="expansion-lightning-once",kind="lightning",x=0,y=0,radius=90,triggered=true,remaining=.4f});
                Show(state);app.World.ResetView();yield return Settle(1.2f);
                for(int frame=0;frame<60;frame++){
                    int environment=app.World.EnvironmentParticleCount,sky=app.World.WeatherParticleCount;
                    report.maxEnvironmentParticles=Mathf.Max(report.maxEnvironmentParticles,environment);report.maxWeatherParticles=Mathf.Max(report.maxWeatherParticles,sky);
                    Require(environment<=580&&sky<=1100,"environment and sky effects respect fixed 580 / 1100 particle budgets");
                    yield return null;
                }
                if(weather!="clear")Require(app.World.WeatherParticleCount>0,weather+" has rendered atmospheric particles");
                if(weather=="rain"||weather=="storm"||weather=="snow"||map=="bog")Require(app.World.EnvironmentParticleCount>0,weather+" has local ground or biome particles");
                if(weather=="storm")Require(app.World.EnvironmentStrikeCount==1,"one network lightning hazard triggers exactly one environment impact");
                Capture("weather-"+weather);
            }
            yield return VerifyAudio();
            yield return VerifyExpandedExecution();
            RestoreVolumes();app.VerificationState=null;app.Leave();report.success=true;WriteReport();
            Debug.Log("ARENA_EXPANSION_OK "+report.checks.Count+" checks / "+report.captures.Count+" nonblack captures");Application.Quit(0);
        }
        GameState AuthoritativeFixture(string map,string weather,int seed)
        {
            var game=new ArenaGame(app.PlayerId,"环境验证",seed);
            for(int i=1;i<4;i++)game.AddPlayer("expansion-player-"+i,"军团 "+i);
            game.Apply(app.PlayerId,new GameCommand{type="world_options",mapId=map,weather=weather});
            game.Apply(app.PlayerId,new GameCommand{type="start",bots=false});
            foreach(var player in game.State.players){game.Apply(player.id,new GameCommand{type="skip"});game.Apply(player.id,new GameCommand{type="ready"});}
            for(int i=0;i<500&&game.State.phase!="tactics";i++)game.Tick(.05f);
            Require(game.State.phase=="tactics","authoritative "+map+" fixture completes recruitment, random deployment and reveal");
            return game.SnapshotFor(app.PlayerId);
        }
        static GameState Clone(GameState state){return JsonUtility.FromJson<GameState>(JsonUtility.ToJson(state));}
        static GameState PreparationFixture(GameState source)
        {
            GameState state=Clone(source);state.phase="prep";state.battlefield=null;
            foreach(var player in state.players){
                foreach(var unit in player.units){Point2 local=ArenaGame.WorldToFormation(player,unit.x,unit.y);Point2 at=ArenaGame.LocalToWorld(player.slot,local.x,local.y);unit.x=at.x;unit.y=at.y;}
                player.deployed=false;player.deploymentSide=player.slot;player.deploymentOffset=new Point2();player.steve=ArenaGame.LocalToWorld(player.slot,0,390);
            }
            return state;
        }
        void VerifyDeployments(GameState state,bool final)
        {
            var sides=new HashSet<int>();
            foreach(var player in state.players){
                Require(player.deployed&&sides.Add(player.deploymentSide),"server assigns each army a distinct random deployment side");
                Point2 oldCentre=ArenaGame.LocalToWorld(player.slot,0,265),newCentre=ArenaGame.FormationToWorld(player,0,265);
                Require(Vector2.Distance(new Vector2(oldCentre.x,oldCentre.y),new Vector2(newCentre.x,newCentre.y))>300,"server relocates each formation into the expanded battlefield");
                Transform terrace=app.World.transform.Find("Arena scenery/Team "+player.slot+" independent preparation terrace");
                Require(terrace!=null&&terrace.gameObject.activeInHierarchy,"living army owns a visible independent deployment terrace");
                Point2 centre=final?ArenaGame.FormationToWorld(player,0,265):ArenaGame.LocalToWorld(player.slot,0,265);
                Vector3 rendered=terrace.TransformPoint(new Vector3(0,0,265f/25));
                Require(Vector2.Distance(new Vector2(rendered.x,rendered.z),new Vector2(centre.x/25,centre.y/25))<.002f,"deployment terrace agrees with the authoritative formation transform");
                if(final){
                    foreach(float x in new[]{-185f,185f})foreach(float y in new[]{175f,355f}){
                        Point2 corner=ArenaGame.FormationToWorld(player,x,y);
                        Require(InView(app.World.ViewCamera.WorldToViewportPoint(app.World.WorldSurfacePoint(corner.x,corner.y))),"overview retains every corner of all four relocated deployment plots");
                    }
                    if(player.id==app.PlayerId){
                        Vector2 movement=ArenaApp.ScreenMovementToWorld(Vector2.right,app.World.ViewCamera.transform.rotation);
                        Vector3 c=app.World.ViewCamera.WorldToViewportPoint(rendered),right=app.World.ViewCamera.WorldToViewportPoint(rendered+new Vector3(movement.x,0,movement.y)*2),rear=app.World.ViewCamera.WorldToViewportPoint(terrace.TransformPoint(new Vector3(0,0,315f/25)));
                        Require(right.x>c.x+.005f&&rear.y<c.y-.005f,"screen-right Steve movement projects right while the random deployment rear remains at screen bottom");
                    }
                }
                foreach(var unit in player.units){
                    VoxelCreature view=Find(unit.id);Require(view!=null,"every living server roster unit has a native creature model");
                    Point2 local=ArenaGame.WorldToFormation(player,unit.x,unit.y);
                    Point2 at=final?new Point2(unit.x,unit.y):ArenaGame.LocalToWorld(player.slot,local.x,local.y);
                    Require(Vector2.Distance(new Vector2(view.Target.x,view.Target.z),new Vector2(at.x/25,at.y/25))<.002f,"creature deployment target remains aligned with its moving terrace");
                    if(final){
                        Vector3 bodyLocal=terrace.InverseTransformPoint(view.transform.position);
                        Require(Vector2.Distance(new Vector2(bodyLocal.x*25,bodyLocal.z*25),new Vector2(local.x,local.y))<.65f,"settled native creature transform preserves its server formation on its own terrace");
                    }
                }
            }
        }
        void VerifyMovingFormationTargets(GameState state)
        {
            foreach(var player in state.players){
                Transform terrace=app.World.transform.Find("Arena scenery/Team "+player.slot+" independent preparation terrace");
                foreach(var unit in player.units){
                    VoxelCreature model=Find(unit.id);Point2 local=ArenaGame.WorldToFormation(player,unit.x,unit.y);
                    Vector3 at=terrace.TransformPoint(new Vector3(local.x/25,0,local.y/25));
                    Require(model!=null&&Vector2.Distance(new Vector2(model.Target.x,model.Target.z),new Vector2(at.x,at.z))<.004f,"moving terrain and all animated creature targets share the same deployment interpolation");
                    Require(Vector2.Distance(new Vector2(model.transform.position.x,model.transform.position.z),new Vector2(at.x,at.z))<.004f,"actual creature bodies stay on their moving deployment terrace without interpolation lag");
                }
            }
        }
        void VerifyOverview(GameState state)
        {
            float edge=BattlefieldCatalog.Extent(state.battlefield);
            foreach(float x in new[]{-edge,edge})foreach(float y in new[]{-edge,edge})
                Require(InView(app.World.ViewCamera.WorldToViewportPoint(app.World.WorldSurfacePoint(x,y))),"overview contains every playable map corner");
            foreach(var player in state.players)foreach(var unit in player.units)
                Require(InView(app.World.ViewCamera.WorldToViewportPoint(app.World.WorldSurfacePoint(unit.x,unit.y))),"overview contains all four deployed armies");
        }
        void VerifyGroundPicking(GameState state)
        {
            int samples=0,ramps=0;
            foreach(var terrain in state.battlefield.terrain){
                if(terrain.kind!="bridge"&&terrain.kind!="highland")continue;
                // Exercise the near edge, interior and corners of the actual expanded ramp mesh.
                foreach(float dx in new[]{-terrain.halfWidth+12,0,terrain.halfWidth-12})
                    foreach(float dy in new[]{-terrain.halfHeight+12,0,terrain.halfHeight-12}){
                        float x=terrain.x+dx,y=terrain.y+dy;Vector3 surface=app.World.WorldSurfacePoint(x,y);float rendered;
                        Require(app.World.TryGetRenderedRampHeight(x,y,out rendered)&&Mathf.Abs(rendered-surface.y)<.001f,"expanded ramp triangles agree with the authoritative walkable height");ramps++;
                        if(!app.World.IsTacticalPointVisible(surface))continue;
                        Point2 hit;Vector2 screen=app.World.ViewCamera.WorldToScreenPoint(surface);
                        bool picked=app.World.TryGround(screen,out hit),aligned=picked&&Mathf.Abs(hit.x-x)<.15f&&Mathf.Abs(hit.y-y)<.15f;
                        Require(aligned,aligned?"actual terrain ray picking follows visible expanded slopes":
                            "expanded ramp pick mismatch map="+state.battlefield.mapId+" terrain="+terrain.id+" at=("+x+","+y+") hit="+picked+" actual=("+hit.x.ToString("F5")+","+hit.y.ToString("F5")+") surface="+surface.ToString("F6")+" camera="+app.World.ViewCamera.transform.position.ToString("F6")+" rotation="+app.World.ViewCamera.transform.eulerAngles.ToString("F6")+" screen="+screen.ToString("F6"));samples++;
                    }
            }
            // Bog is flat; the other maps also validate flat ground outside raised areas.
            for(int x=-300;x<=300;x+=150)for(int y=-300;y<=300;y+=150){
                Vector3 surface=app.World.WorldSurfacePoint(x,y);if(!app.World.IsTacticalPointVisible(surface))continue;
                Point2 hit;bool picked=app.World.TryGround(app.World.ViewCamera.WorldToScreenPoint(surface),out hit),aligned=picked&&Mathf.Abs(hit.x-x)<.15f&&Mathf.Abs(hit.y-y)<.15f;
                Require(aligned,aligned?"ground picking remains aligned after camera zoom and pan":"expanded flat pick mismatch map="+state.battlefield.mapId+" at=("+x+","+y+") actual=("+hit.x.ToString("F5")+","+hit.y.ToString("F5")+") surface="+surface.ToString("F6"));samples++;
            }
            Require(samples>=4&&(!state.battlefield.terrain.Exists(t=>t.kind=="highland")||ramps>=9),"map projection exercises multiple real surface samples");
        }
        void VerifyLandscapeColor(GameState state)
        {
            string map=state.battlefield.mapId;
            MeshFilter ground=null;
            foreach(var filter in app.World.GetComponentsInChildren<MeshFilter>()){
                if(filter.name!="Continuous biome ground and connected paths"||filter.transform.parent==null||filter.transform.parent.name!="Map "+map)continue;
                ground=filter;break;
            }
            Require(ground!=null&&ground.sharedMesh!=null,map+" retains one actual continuous landscape mesh");
            Vector3[] vertices=ground.sharedMesh.vertices;Color[] colors=ground.sharedMesh.colors;
            Require(vertices.Length>1000&&vertices.Length==colors.Length,map+" shipping ground mesh carries authored terrain colours");
            Point2 road=map=="courtyards"?new Point2(0,600):map=="bog"?new Point2(-375,205):new Point2(450,238);
            Point2 verge=map=="courtyards"?new Point2(150,600):map=="bog"?new Point2(-375,430):new Point2(450,450);
            Color roadColor=ClosestGroundVertexColor(ground,vertices,colors,road),vergeColor=ClosestGroundVertexColor(ground,vertices,colors,verge);
            float difference=Vector3.Distance(new Vector3(roadColor.r,roadColor.g,roadColor.b),new Vector3(vergeColor.r,vergeColor.g,vergeColor.b));
            bool visible=difference>.08f&&roadColor.grayscale>vergeColor.grayscale+.035f;
            Require(visible,visible?map+" actual ground vertex colours visibly distinguish the connected resource approach from adjacent land":
                map+" ground path lost its authored contrast: RGB distance="+difference.ToString("F4")+" road="+roadColor+" verge="+vergeColor);
        }
        Color ClosestGroundVertexColor(MeshFilter ground,Vector3[] vertices,Color[] colors,Point2 at)
        {
            Vector3 desired=app.World.WorldSurfacePoint(at.x,at.y);int closest=-1;float nearest=float.PositiveInfinity;
            for(int i=0;i<vertices.Length;i++){
                Vector3 actual=ground.transform.TransformPoint(vertices[i]);float dx=actual.x-desired.x,dz=actual.z-desired.z,distance=dx*dx+dz*dz;
                if(distance<nearest){nearest=distance;closest=i;}
            }
            Require(closest>=0&&nearest<.8f,"landscape colour sample resolves a nearby vertex of the actual rendered mesh");
            return colors[closest];
        }
        IEnumerator VerifyAudio()
        {
            foreach(string id in new[]{"ambient_courtyards","ambient_bog","ambient_frost","weather_rain","weather_storm","weather_fog","weather_snow","weather_heat","weather_sandstorm","weather_thunder"}){
                AudioClip clip=Resources.Load<AudioClip>("ArenaAudio/"+id);Require(clip!=null,"new environment audio resource loads: "+id);
                clip.LoadAudioData();float deadline=Time.realtimeSinceStartup+5;
                while(clip.loadState==AudioDataLoadState.Loading&&Time.realtimeSinceStartup<deadline)yield return null;
                Require(clip.loadState==AudioDataLoadState.Loaded&&clip.samples>0&&clip.frequency>=16000,"environment clip has decoded PCM audio: "+id);
                var data=new float[clip.samples*clip.channels];
                Require(clip.GetData(data,0),"environment clip exposes sample data: "+id);
                double sum=0,highEnergy=0,differenceEnergy=0,low=0;
                double alpha=1-Math.Exp(-2*Math.PI*1500/clip.frequency);
                float peak=0;
                for(int i=0;i<data.Length;i++){
                    float value=data[i];sum+=value*value;peak=Mathf.Max(peak,Mathf.Abs(value));
                    low+=alpha*(value-low);highEnergy+=(value-low)*(value-low);
                    if(i>0){double difference=value-data[i-1];differenceEnergy+=difference*difference;}
                }
                double rms=Math.Sqrt(sum/data.Length);
                Require(rms>.005&&rms<.09&&peak<.43,"environment clip is quiet, finite and unclipped: "+id+" RMS="+rms.ToString("F5")+" peak="+peak.ToString("F4"));
                Require(highEnergy/sum<.065,"decoded environmental audio suppresses the old broadband hiss: "+id+" high-pass energy ratio="+(highEnergy/sum).ToString("F5"));
                if(id!="weather_thunder"){
                    double boundary=Math.Abs(data[0]-data[data.Length-1]);
                    double derivativeRms=Math.Sqrt(differenceEnergy/(data.Length-1));
                    Require(boundary<.012&&boundary<derivativeRms*3,"loop boundary has no artificial transient: "+id+" step="+boundary.ToString("F6")+" derivative RMS="+derivativeRms.ToString("F6"));
                }else Require(Mathf.Abs(data[0])<.0001f&&Mathf.Abs(data[data.Length-1])<.0001f,"rolling thunder begins and ends without a discontinuity");
            }
            var state=AuthoritativeFixture("bog","rain",83);Show(state);yield return Settle(2.2f);
            Require(app.Ambience.LoadedClips>=3&&app.Ambience.BiomeLevel>.01f&&app.Ambience.WeatherLevel>.01f,"independent biome and weather ambience play during the expanded map");
            var oldBiome=EnvironmentSource("ambient_bog");var oldWeather=EnvironmentSource("weather_rain");
            Require(oldBiome!=null&&oldWeather!=null,"decoded map and rain clips play on independent native sources");
            float oldBiomeTime=oldBiome.time;
            Show(Clone(state));yield return Settle(.2f);
            Require(oldBiome.isPlaying&&oldBiome.time>oldBiomeTime+.05f,"duplicate battlefield snapshots keep the environmental audio playback position");
            var next=AuthoritativeFixture("frost","snow",83);Show(next);yield return Settle(.42f);
            var newBiome=EnvironmentSource("ambient_frost");var newWeather=EnvironmentSource("weather_snow");
            Require(oldBiome.isPlaying&&oldWeather.isPlaying&&oldBiome.volume>.005f&&oldWeather.volume>.005f&&
                newBiome!=null&&newWeather!=null&&newBiome.volume>.005f&&newWeather.volume>.005f&&app.Ambience.CrossfadeSourceCount==4,
                "native map and weather transitions overlap both old and new sources instead of a hard stop");
            yield return Settle(1.8f);
            Require(!oldBiome.isPlaying&&!oldWeather.isPlaying&&newBiome.isPlaying&&newWeather.isPlaying&&app.Ambience.CrossfadeSourceCount==2,
                "crossfades finish and release old environment playback sources");
            float tacticalLevel=app.Ambience.BiomeLevel;
            var battle=Clone(next);battle.phase="battle";Show(battle);yield return Settle(.25f);
            Require(app.Ambience.BiomeLevel<tacticalLevel*.76f&&app.Ambience.BiomeLevel>tacticalLevel*.64f,
                "combat automatically leaves headroom for attacks by reducing the environmental bed");
            app.Sound.SetVolumes(music,.7f,true);yield return null;yield return null;
            foreach(var source in app.Ambience.GetComponentsInChildren<AudioSource>())
                Require(source.mute||source.volume<=.0001f,"global audio mute silences every environment source immediately");
            app.Sound.SetVolumes(music,.7f,false);yield return null;yield return null;
            Require(app.Ambience.BiomeLevel>.01f,"unmuting restores environment sound without restarting the map");
        }
        AudioSource EnvironmentSource(string clip)
        {
            foreach(var source in app.Ambience.GetComponentsInChildren<AudioSource>())
                if(source.clip!=null&&source.clip.name==clip&&source.isPlaying)return source;
            return null;
        }
        IEnumerator VerifyExpandedExecution()
        {
            // Preserve the real host-assigned corner deployments; an old scale-one
            // cinematic fixture would not catch a far-away commander being cropped.
            var baseline=AuthoritativeFixture("courtyards","clear",211);baseline.phase="battle";baseline.round=12;
            foreach(var owner in baseline.players){
                owner.hp=20;owner.eliminated=false;
                foreach(var unit in owner.units){var fighter=ArenaGame.PreviewFighter(owner,unit);fighter.id=unit.id;fighter.unitId=unit.id;fighter.ownerId=owner.id;fighter.slot=owner.slot;baseline.fighters.Add(fighter);}
            }
            Show(baseline);app.World.ResetView();yield return Settle(1.2f);
            float wideSize=app.World.ViewCamera.orthographicSize;Vector3 widePosition=app.World.ViewCamera.transform.position;
            int beforeHits=app.World.CommanderHitCount;
            var results=Clone(baseline);results.phase="results";results.timeLeft=12;
            PlayerState victim=results.players[1];victim.hp=0;victim.eliminated=true;
            results.placements.Add(new PlacementState{playerId=victim.id,rank=4,damage=20});
            Show(results);
            Require(app.World.RitualActive&&app.World.PlannedExecutionCount==1&&app.World.CommanderHitCount==beforeHits+1,"expanded settlement schedules one lethal crystal strike through the real health observer");
            bool peak=false,beam=false,seal=false,burst=false,shatter=false,soul=false,captured=false;
            float deadline=Time.realtimeSinceStartup+12;
            while(app.World.RitualActive&&Time.realtimeSinceStartup<deadline){
                beam|=app.World.ExecutionBeamConnected(victim.slot);seal|=app.World.ExecutionSealed(victim.slot);
                burst|=app.World.ExecutionExploding(victim.slot);shatter|=app.World.ExecutionShattered(victim.slot);soul|=app.World.ExecutionSoulVisible(victim.slot);
                if(app.World.ExecutionCameraWeight>.995f){
                    Require(app.World.ExecutionFocusSlot==victim.slot,"expanded execution camera follows the eliminated remote commander");
                    VerifyExecutionFraming(victim);
                    Require(app.World.ViewCamera.orthographicSize<wideSize*.9f,"expanded execution provides a visible closeup while retaining the firing platform");
                    peak=true;
                    if(!captured&&app.World.ExecutionSealed(victim.slot)){
                        yield return new WaitForEndOfFrame();VerifyExecutionFraming(victim);Capture("execution-expanded-01-seal-and-platform");captured=true;
                    }
                }
                yield return null;
            }
            Require(peak&&captured&&beam&&seal&&burst&&shatter&&soul,"expanded lethal strike presents its closeup, connecting beam, seal, explosion, fragments and rising soul");
            Require(!app.World.RitualActive&&!app.World.ExecutionActive&&app.World.ExecutionCount==1&&app.World.RitualImpactCount==1,"expanded lethal strike completes exactly once within its scheduled ceremony");
            yield return Settle(.65f);
            Require(app.World.ExecutionCameraWeight<.001f&&app.World.ExecutionFocusSlot==-1&&Mathf.Abs(app.World.ViewCamera.orthographicSize-wideSize)<wideSize*.02f&&Vector3.Distance(app.World.ViewCamera.transform.position,widePosition)<.45f,"expanded execution restores the full arena camera after the soul finishes");
            VerifyOverview(results);Capture("execution-expanded-02-wide-return");
            Show(Clone(results));yield return Settle(.3f);
            Require(!app.World.RitualActive&&!app.World.ExecutionActive&&app.World.CommanderHitCount==beforeHits+1&&app.World.ExecutionCount==1,"a repeated expanded settlement snapshot cannot replay the lethal strike");
        }
        void VerifyExecutionFraming(PlayerState victim)
        {
            Vector3 target=app.World.WorldSurfacePoint(victim.steve.x,victim.steve.y);
            var points=new List<Vector3>();
            // Read actual enlarged renderers rather than passing against the former 6.9-high crystal.
            foreach(Bounds bounds in new[]{app.World.RitualPillarBounds,app.World.RitualCrystalBounds,app.World.RitualPlatformBounds,
                new Bounds(target+Vector3.up*2.1f,new Vector3(8,5.2f,8))})
                for(int cell=0;cell<8;cell++)
                    points.Add(bounds.center+Vector3.Scale(bounds.extents,
                        new Vector3((cell&1)==0?-1:1,(cell&2)==0?-1:1,(cell&4)==0?-1:1)));
            foreach(Vector3 point in points){
                Vector3 at=app.World.ViewCamera.WorldToViewportPoint(point);
                Require(at.z>0&&at.x>=.025f&&at.x<=.975f&&at.y>=.025f&&at.y<=.975f,
                    at.z>0&&at.x>=.025f&&at.x<=.975f&&at.y>=.025f&&at.y<=.975f?"expanded execution keeps the entire platform, crystal, victim effects and soul inside the frame":
                    "expanded execution clips its platform or victim: world="+point.ToString("F4")+" viewport="+at.ToString("F4"));
            }
        }
        void Show(GameState state){app.Hud.ClosePinnedInspection();app.VerificationState=state;app.ShowRoom=app.ShowShop=false;app.World.SetState(state,app.PlayerId);}
        IEnumerator Settle(float seconds){yield return new WaitForSecondsRealtime(seconds);yield return new WaitForEndOfFrame();}
        VoxelCreature Find(string id){foreach(var view in app.World.GetComponentsInChildren<VoxelCreature>())if(view.UnitId==id&&view.gameObject.activeInHierarchy&&!view.Dead)return view;return null;}
        static bool InView(Vector3 at){return at.z>0&&at.x>=0&&at.x<=1&&at.y>=0&&at.y<=1;}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        void Capture(string name)
        {
            var frame=ScreenCapture.CaptureScreenshotAsTexture();int samples=0,lit=0,pink=0;float low=1,high=0;
            for(int y=10;y<frame.height;y+=19)for(int x=10;x<frame.width;x+=19){Color c=frame.GetPixel(x,y);float l=c.grayscale;low=Mathf.Min(low,l);high=Mathf.Max(high,l);if(l>.025f)lit++;if(c.r>.85f&&c.b>.85f&&c.g<.12f)pink++;samples++;}
            Require(lit>samples*.65f&&high-low>.15f,"capture "+name+" contains a nonblack rendered world");
            Require(pink<samples*.02f,"capture "+name+" contains no missing-shader surfaces");
            File.WriteAllBytes(Path.Combine(folder,name+".png"),frame.EncodeToPNG());Destroy(frame);report.captures.Add(name+".png");
        }
        void Require(bool value,string description){if(!value)throw new InvalidOperationException(description);if(!report.checks.Contains(description))report.checks.Add(description);}
        void RestoreVolumes(){if(volumesSaved&&app!=null&&app.Sound!=null){app.Sound.SetVolumes(music,sfx,muted);volumesSaved=false;}}
        void WriteReport(){if(folder!=null)File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        void Fail(string message){failed=true;report.error=message;RestoreVolumes();WriteReport();if(app!=null)app.Leave();Debug.LogError("ARENA_EXPANSION_FAILED "+message);Application.Quit(2);}
    }
}
