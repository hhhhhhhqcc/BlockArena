using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BlockArena.Core;
using BlockArena.Presentation;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace BlockArena.Runtime
{
    // Opt-in native-player measurement. Frame timings describe this machine, graphics
    // device and fixture; they are deliberately not a cross-device FPS guarantee.
    public sealed class ArenaPerformanceProbe:MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool success;public string error="",unityVersion,graphicsDevice,cpu;
            public int width,height,originals,summons,battleModelBuilds,battleMeshBuilds,migrations,warmedModels,prewarmedImpactMeshBuilds,peakWarmupJobs,peakWarmupModels,warmupBudgetOverruns;
            public int effectsCapacity,effectsAllocated,peakEffects,frameSamples;
            public double preparationSetStateMs,tacticsSetStateMs,battleSetStateMs,warmupSoftBudgetMs,warmupPeakFrameMs,warmupTotalMs;
            public double firstBattleFrameP50Ms,firstBattleFrameP95Ms,firstBattleFrameMaxMs,snapshotP50Ms,snapshotP95Ms,snapshotMaxMs;
            public List<string> checks=new List<string>();public List<string> captures=new List<string>();
            public List<string> battleNewMeshKeys=new List<string>();
        }
        readonly Report report=new Report();readonly List<double> frames=new List<double>(),snapshots=new List<double>();
        readonly Dictionary<string,VoxelCreature> originals=new Dictionary<string,VoxelCreature>();
        ArenaApp app;ArenaWorld world;string folder;bool failed;
        const string Viewer="performance-player-0";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequested()
        {if(Array.IndexOf(Environment.GetCommandLineArgs(),"-performance-test")>=0)new GameObject("Native battle transition performance verification").AddComponent<ArenaPerformanceProbe>();}
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){var iterator=stack.Peek();bool moved=false;object next=null;
                try{moved=iterator.MoveNext();if(moved)next=iterator.Current;}catch(Exception e){Fail(e.ToString());}
                if(failed)yield break;if(!moved){stack.Pop();continue;}if(next is IEnumerator){stack.Push((IEnumerator)next);continue;}yield return next;}
        }
        IEnumerator Run()
        {
            folder=Path.Combine(Application.dataPath,"..","Captures","Performance");var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);report.width=Screen.width;report.height=Screen.height;report.unityVersion=Application.unityVersion;
            report.graphicsDevice=SystemInfo.graphicsDeviceName;report.cpu=SystemInfo.processorType;
            yield return new WaitForSecondsRealtime(.6f);app=ArenaApp.Instance;
            Require(app!=null&&app.World!=null,"native ArenaApp and render world initialized");
            Require(!app.HasSession,"performance fixture runs without a simulation/network session");
            app.VerificationInput=true;app.VerificationPointer=new Vector2(-1000,-1000);app.enabled=false;world=app.World;
            world.Clear();world.ResetView();GameState state=Fixture();
            report.preparationSetStateMs=Show(state);
            float deadline=Time.realtimeSinceStartup+20;
            do {yield return null;Require(world.WarmupActiveBodyCount==0,"prewarmed bodies remain inactive");}
            while(!world.WarmupReady&&Time.realtimeSinceStartup<deadline);
            Require(world.WarmupReady,"preparation finishes the finite shared-mesh, effect and first-summon warmup queue");
            Require(world.WarmupModelBuildCount>0,"off-camera enemy and summon models were prepared before reveal");
            Require(world.CombatVfxActiveCount==0&&world.CombatParticleCount==0,"warming effects emits no live particles or combat accents");
            Require(world.WarmupPeakFrameJobs<=2&&world.WarmupPeakFrameModels<=1,"warmup runs at most two jobs and one whole model per rendered frame");
            report.warmedModels=world.WarmupModelBuildCount;report.peakWarmupJobs=world.WarmupPeakFrameJobs;report.peakWarmupModels=world.WarmupPeakFrameModels;
            report.warmupSoftBudgetMs=world.WarmupSoftBudgetMilliseconds;report.warmupPeakFrameMs=world.WarmupPeakFrameMilliseconds;
            report.warmupTotalMs=world.WarmupTotalMilliseconds;report.warmupBudgetOverruns=world.WarmupBudgetOverruns;
            report.prewarmedImpactMeshBuilds=world.WarmupCombatImpactMeshBuildCount;
            int warmedBuilds=world.CreatureVisualBuildCount;
            state.phase="tactics";state.battlefield=BattlefieldCatalog.CreateExpanded("courtyards","clear",731);state.battlefield.revealAge=20;
            report.tacticsSetStateMs=Show(state);yield return new WaitForSecondsRealtime(.7f);
            Require(world.CreatureVisualBuildCount==warmedBuilds,"revealing the full roster acquires prewarmed models without rebuilding them");
            foreach(var player in state.players)foreach(var unit in player.units){var visual=world.CreatureVisualForVerification("unit:"+unit.id);Require(visual!=null,"tactical unit has an existing articulated model");originals.Add(unit.id,visual);}
            report.originals=originals.Count;yield return new WaitForEndOfFrame();Capture("01-ready-armies");
            // PNG encoding/readback belongs to verification, not the battle transition.
            // Let two rendered frames pass before sampling the opening frame interval.
            yield return null;yield return null;
            state.phase="battle";state.timeLeft=120;
            foreach(var player in state.players)foreach(var unit in player.units){var fighter=ArenaGame.PreviewFighter(player,unit);fighter.id="live-"+unit.id;fighter.unitId=unit.id;state.fighters.Add(fighter);}
            AddOpeningEffects(state);int builds=world.CreatureVisualBuildCount,meshes=world.CachedVoxelMeshBuildCount,migrations=world.CreatureBattleMigrationCount;
            var meshesBeforeBattle=new HashSet<string>(world.CachedVoxelMeshKeysForVerification);
            report.battleSetStateMs=Show(state);
            foreach(var fighter in state.fighters)Require(world.CreatureVisualForVerification("fighter:"+fighter.id)==originals[fighter.unitId],"live fighter keeps its exact tactical GameObject despite a new server fighter id");
            float started=Time.realtimeSinceStartup,nextSnapshot=started;
            while(Time.realtimeSinceStartup-started<3){
                yield return null;frames.Add(Time.unscaledDeltaTime*1000d);
                if(Time.realtimeSinceStartup>=nextSnapshot){nextSnapshot+=.05f;snapshots.Add(Show(state));}
                report.peakEffects=Math.Max(report.peakEffects,world.CombatVfxActiveCount);
                Require(world.CombatVfxAllocatedCount<=world.CombatVfxCapacity,"opening combat respects the original effect-object capacity");
            }
            report.battleModelBuilds=world.CreatureVisualBuildCount-builds;report.battleMeshBuilds=world.CachedVoxelMeshBuildCount-meshes;
            foreach(string key in world.CachedVoxelMeshKeysForVerification)if(!meshesBeforeBattle.Contains(key))report.battleNewMeshKeys.Add(key);
            report.migrations=world.CreatureBattleMigrationCount-migrations;
            Require(report.battleModelBuilds==0&&report.migrations==report.originals,"opening battle builds zero duplicate originals and migrates the whole roster");
            Require(report.battleMeshBuilds==0,"the mixed opening attacks use only prewarmed shared meshes");
            // A summon has the source unitId but an independent fighter id and must
            // acquire a reserved object rather than taking its original's identity.
            var source=state.fighters.Find(f=>f.kind=="evoker");
            for(int i=0;i<2;i++){var summoned=ArenaGame.PreviewFighter(state.players[source.slot],new UnitState{id=source.unitId,kind="vex",x=source.x+i*28,y=source.y-25});summoned.id="summon-first-"+i;summoned.unitId=source.unitId;summoned.summoned=true;state.fighters.Add(summoned);}
            var decoy=ArenaGame.PreviewFighter(state.players[3],new UnitState{id="performance-decoy-unit",kind="iron_golem",x=0,y=0});decoy.id="performance-decoy-fighter";decoy.summoned=true;state.fighters.Add(decoy);
            Show(state);report.summons=3;
            Require(world.CreatureVisualBuildCount==builds,"first summon wave uses inactive prewarmed species models");
            Require(world.CreatureVisualForVerification("fighter:"+source.id)==originals[source.unitId],"summon provenance never steals the source unit model");
            Require(world.CreatureVisualForVerification("fighter:summon-first-0")!=originals[source.unitId],"summoned creature owns a separate visual identity");
            Require(world.CreatureVisualForVerification("fighter:performance-decoy-fighter")!=null,"the selected commander decoy also acquires its prewarmed iron golem");
            yield return new WaitForSecondsRealtime(.1f);yield return new WaitForEndOfFrame();Capture("02-live-battle");
            // Filtered authoritative snapshots contain no hidden enemy fighter.
            var enemy=state.fighters.Find(f=>f.slot==1&&!f.summoned);string enemyId=enemy.id;state.fighters.Remove(enemy);Show(state);
            Require(world.CreatureVisualForVerification("fighter:"+enemyId)==null,"an enemy absent from the authoritative visible snapshot has no active visual");
            Require(world.WarmupActiveBodyCount==0,"inactive reserve models never reveal hidden or future creature positions");
            report.effectsCapacity=world.CombatVfxCapacity;report.effectsAllocated=world.CombatVfxAllocatedCount;report.frameSamples=frames.Count;
            report.firstBattleFrameP50Ms=Percentile(frames,.5);report.firstBattleFrameP95Ms=Percentile(frames,.95);report.firstBattleFrameMaxMs=Percentile(frames,1);
            report.snapshotP50Ms=Percentile(snapshots,.5);report.snapshotP95Ms=Percentile(snapshots,.95);report.snapshotMaxMs=Percentile(snapshots,1);
            Require(report.frameSamples>=20,"first three battle seconds contain native rendered-frame measurements");
            report.success=true;WriteReport();Debug.Log("ARENA_PERFORMANCE_OK originals="+report.originals+" newModels="+report.battleModelBuilds+" newMeshes="+report.battleMeshBuilds+" openingMs="+report.battleSetStateMs.ToString("F3")+" frameP95Ms="+report.firstBattleFrameP95Ms.ToString("F3"));Application.Quit(0);
        }
        GameState Fixture()
        {
            var state=new GameState{phase="prep",round=10,timeLeft=120};
            string[][] kinds={new[]{"evoker","slime","magma_cube","skeleton","blaze","iron_golem","allay","wolf","warden","guardian","creeper","snow_golem"},new[]{"skeleton","zombie","cave_spider","witch","ravager","enderman","bee","pillager","bogged","shulker","wither","bat"},new[]{"evoker","magma_cube","slime","breeze","drowned","elder_guardian","ghast","piglin","wither_skeleton","creaking","happy_ghast","ender_dragon"},new[]{"skeleton","snow_golem","allay","wolf","zombie","iron_golem","blaze","witch","creeper","guardian","vex","goat"}};
            for(int slot=0;slot<4;slot++){
                var owner=new PlayerState{id="performance-player-"+slot,name="军团 "+slot,slot=slot,hp=20,steve=ArenaGame.LocalToWorld(slot,0,385)};
                if(slot==3)owner.commanderSkill="decoy";
                state.players.Add(owner);for(int i=0;i<kinds[slot].Length;i++){Point2 point=ArenaGame.LocalToWorld(slot,(i%4-1.5f)*75,185+(i/4)*65);owner.units.Add(new UnitState{id="performance-unit-"+slot+"-"+i,kind=kinds[slot][i],x=point.x,y=point.y});}
            }return state;
        }
        void AddOpeningEffects(GameState state)
        {
            for(int i=0;i<state.fighters.Count;i++){
                var source=state.fighters[i];var target=state.fighters[(i+12)%state.fighters.Count];
                if(i%2==0)state.effects.Add(new EffectState{id="performance-opening-"+i,type=source.range>70?"arrow":"strike",kind=source.kind,slot=source.slot,sourceId=source.id,targetId=target.id,x=source.x,y=source.y,targetX=target.x,targetY=target.y,value=12});
                if(i%3==0)state.effects.Add(new EffectState{id="performance-hit-"+i,type="hit",kind=source.kind,slot=source.slot,sourceId=source.id,targetId=target.id,x=target.x,y=target.y,targetX=target.x,targetY=target.y,value=12});
            }
        }
        double Show(GameState state){app.VerificationState=state;long at=Stopwatch.GetTimestamp();world.SetState(state,Viewer);return(Stopwatch.GetTimestamp()-at)*1000d/Stopwatch.Frequency;}
        static double Percentile(List<double> values,double fraction){if(values.Count==0)return 0;var sorted=values.ToArray();Array.Sort(sorted);return sorted[Mathf.Clamp((int)Math.Ceiling(sorted.Length*fraction)-1,0,sorted.Length-1)];}
        void Capture(string name){var texture=ScreenCapture.CaptureScreenshotAsTexture();try{Require(texture!=null,"native rendered screenshot is available");int visible=0,samples=0;for(int y=0;y<texture.height;y+=23)for(int x=0;x<texture.width;x+=23){samples++;if(texture.GetPixel(x,y).grayscale>.025f)visible++;}Require(visible>samples*.35f,"native screenshot contains a visible arena");File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());report.captures.Add(name+".png");}finally{if(texture!=null)Destroy(texture);}}
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);if(!report.checks.Contains(message))report.checks.Add(message);}
        void WriteReport(){if(folder!=null)File.WriteAllText(Path.Combine(folder,"performance-report.json"),JsonUtility.ToJson(report,true));}
        void Fail(string message){failed=true;report.error=message;try{WriteReport();}catch(Exception error){Debug.LogError(error);}Debug.LogError("ARENA_PERFORMANCE_FAILED "+message);Application.Quit(2);}
    }
}
