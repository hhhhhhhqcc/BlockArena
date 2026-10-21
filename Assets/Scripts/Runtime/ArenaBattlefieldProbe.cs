using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlockArena.Core;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Opt-in native player checks, using the actual rendered world and pointer-selection path.
    public sealed class ArenaBattlefieldProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool success;
            public string error,unityVersion,graphicsDevice;
            public int width,height;
            public List<string> checks=new List<string>();
            public List<string> captures=new List<string>();
        }
        readonly Report report=new Report();
        ArenaApp app;string folder;bool failed;
        bool rasterRoute;
        Point2 rasterFrom,rasterTo;
        readonly Color rasterColor=new Color(.08f,1,.18f,1);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-battlefield-test")>=0)
                new GameObject("Native battlefield verification").AddComponent<ArenaBattlefieldProbe>();
        }
        IEnumerator Start()
        {
            IEnumerator run=Run();
            while(true){
                object next=null;bool moved=false;
                try{moved=run.MoveNext();if(moved)next=run.Current;}catch(Exception e){Fail(e.ToString());}
                if(failed||!moved)yield break;yield return next;
            }
        }
        IEnumerator Run()
        {
            string[] args=Environment.GetCommandLineArgs();
            folder=Path.Combine(Application.dataPath,"..","Captures","Battlefields");
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);
            report.width=Screen.width;report.height=Screen.height;
            report.unityVersion=Application.unityVersion;report.graphicsDevice=SystemInfo.graphicsDeviceName;
            yield return new WaitForSecondsRealtime(.7f);
            app=ArenaApp.Instance;Require(app!=null&&app.World!=null,"native world initialized");
            app.VerificationInput=true;app.VerificationPointer=new Vector2(-1000,-1000);
            app.Hud.ShowCreatureRange=true;
            app.Host();float deadline=Time.realtimeSinceStartup+12;
            while(!app.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Connected,"actual local TCP session established");
            Require(app.State!=null&&app.State.battlefield==null,"Unity wire codec preserves the absence of battlefield before reveal");
            Require(app.State.roundEvent==null,"Unity wire codec preserves the absence of removed central events");
            // VerificationState isolates rendered fixtures; keep draining real TCP traffic
            // so a longer screenshot run cannot overflow the transport receive queue.
            app.ShowRoom=false;app.SnapshotPauseUntil=0;
            var lobby=Fixture("courtyards","clear","lobby");lobby.battlefield=null;Show(lobby);
            app.ShowRoom=true;app.Tactics.ProcessInput();yield return new WaitForSecondsRealtime(.3f);
            app.Tactics.OptionsOpen=true;
            yield return new WaitForSecondsRealtime(.2f);yield return new WaitForEndOfFrame();Capture("lobby-map-options");
            app.Tactics.OptionsOpen=false;app.Tactics.SkillPickerOpen=true;
            yield return new WaitForEndOfFrame();Capture("lobby-commander-picker");
            app.ShowRoom=false;app.Tactics.SkillPickerOpen=false;app.Tactics.OptionsOpen=false;
            var preparation=Fixture("courtyards","clear","prep");preparation.battlefield=null;Show(preparation);
            yield return new WaitForSecondsRealtime(.3f);
            Require(app.Hud.BattlefieldControlsFit,"preparation commander controls do not overlap army and purchased-skill buttons");
            yield return new WaitForEndOfFrame();Capture("preparation-controls");
            preparation.round=3;Show(preparation);app.Tactics.ProcessInput();
            Require(app.Hud.CommanderSelectableForVerification,"commander skills can be selected again in later rounds");
            app.Tactics.SkillPickerOpen=true;yield return new WaitForSecondsRealtime(.35f);
            Rect picker=app.Hud.CommanderPickerScreenRect;
            Require(picker.xMin>=0&&picker.yMin>=0&&picker.xMax<=Screen.width+.5f&&picker.yMax<=Screen.height+.5f,"compact commander picker fits the native screen");
            for(int i=0;i<BattlefieldCatalog.Commanders.Length;i++){
                Rect choice=app.Hud.CommanderChoiceScreenRect(i);
                Require(picker.Contains(choice.min+Vector2.one)&&picker.Contains(choice.max-Vector2.one),"commander choice "+i+" fits the compact panel");
            }
            app.VerificationPointer=app.Hud.CommanderChoiceScreenRect(0).center;
            yield return null;yield return new WaitForEndOfFrame();
            string commanderBody=app.Hud.HoverBodyText;
            Require(commanderBody.Contains("8")&&!commanderBody.Contains("‹color")&&!commanderBody.Contains("‹/color"),"hovering commander choices shows effects without escaped color tags");
            Require(System.Text.RegularExpressions.Regex.Matches(commanderBody,"<color=").Count==System.Text.RegularExpressions.Regex.Matches(commanderBody,"</color>").Count,"commander tooltip rich text keeps balanced color tags");
            Capture("commander-compact-round-three-tooltip");
            preparation.players[0].ready=true;
            Require(!app.Hud.CommanderSelectableForVerification,"ready commanders lock their selection until readiness is cancelled");
            app.Tactics.SkillPickerOpen=false;app.VerificationPointer=new Vector2(-1000,-1000);
            var arena=Fixture("courtyards","clear","reveal");arena.battlefield=null;arena.revealStage="arena";arena.revealStageAge=.4f;Show(arena);
            yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();Capture("reveal-01-normal-arena");
            arena.revealStage="map";arena.revealStageAge=.6f;Show(arena);
            yield return new WaitForEndOfFrame();Capture("reveal-02-map-draw");
            foreach(string map in BattlefieldCatalog.Maps){
                var state=Fixture(map,BattlefieldCatalog.GetMap(map).weathers[0],"reveal");state.battlefield.revealAge=0;
                state.revealStage="terrain";state.revealStageAge=0;state.battlefield.weather="clear";
                Show(state);yield return new WaitForSecondsRealtime(.15f);
                Require(app.World.RevealProgress<.4f,map+" terrain begins below its settled height");
                Require(app.World.MapRevealSoundCount>0,"map emergence has an audible cue");
                yield return new WaitForEndOfFrame();Capture(map+"-01-emerging");
                state.battlefield.revealAge=5;state.revealStageAge=4;Show(state);yield return new WaitForSecondsRealtime(.5f);
                Require(app.World.RevealProgress>.99f,map+" terrain fully rises before planning");
                state.revealStage="weather";state.revealStageAge=1;Show(state);yield return new WaitForEndOfFrame();
                Capture(map+"-weather-draw-after-terrain");
                state.phase="tactics";state.timeLeft=120;Show(state);
                yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();Capture(map+"-02-planning");
                Require(app.World.FieldGeometryCount>0&&app.World.FieldTerrainCount==state.battlefield.terrain.Count&&app.World.FieldObjectiveCount==state.battlefield.objectives.Count,map+" creates geometry for every terrain and objective");
                float expected=BattlefieldCatalog.HeightAt(state.battlefield,0,0)+.06f;
                Require(Mathf.Abs(app.World.WorldSurfacePoint(0,0).y-expected)<.03f,map+" unit and route projection use the terrain height");
            }
            foreach(string weather in BattlefieldCatalog.Weathers){
                string map=weather=="snow"?"frost":weather=="rain"||weather=="fog"||weather=="heat"?"bog":"courtyards";
                var state=Fixture(map,weather,"battle");
                if(weather=="storm")state.battlefield.hazards.Add(new HazardState{id="warning",kind="lightning",x=0,y=0,radius=75,delay=1.2f,remaining=2});
                if(weather=="snow")foreach(var t in state.battlefield.terrain)if(t.kind=="water")t.freeze=1;
                Show(state);yield return new WaitForSecondsRealtime(.8f);
                if(weather!="clear")Require(app.World.WeatherParticleCount>0,weather+" has visible weather particles");
                Require(app.Hud.BattlefieldControlsFit,"battle commander controls do not overlap the purchased-skill button");
                Require(!app.Hud.KeyboardCaptured,"world verification has no focused input");
                yield return new WaitForEndOfFrame();Capture("weather-"+weather);
            }
            var tactical=Fixture("courtyards","clear","tactics");Show(tactical);
            app.Tactics.ProcessInput();yield return new WaitForSecondsRealtime(.8f);
            var own=tactical.players[0];var a=Find(own.units[0].id);var b=Find(own.units[1].id);
            Require(a!=null&&b!=null,"tactical creature colliders exist");
            Vector3 aCenter=Vector3.zero,bCenter=Vector3.zero;
            Require(app.World.TryGetUnitSelectionPoint(a.UnitId,out aCenter)&&app.World.TryGetUnitSelectionPoint(b.UnitId,out bCenter),"creature selection anchors follow their rendered models");
            Vector2 pa=app.World.ViewCamera.WorldToScreenPoint(aCenter);
            Vector2 pb=app.World.ViewCamera.WorldToScreenPoint(bCenter);
            Require(!app.Hud.Contains(pa)&&!app.Hud.Contains(pb),"selectable creatures are outside HUD interception regions");
            Click(pa,false);Require(app.Tactics.SelectedCount==1,"left click selects one actual creature");
            Require(app.Hud.PinnedInspectionId==own.units[0].id,"single click pins the selected creature stats");
            app.Tactics.AddWait();Click(pa,false);
            Require(app.Tactics.RouteNodeCount==1&&app.Tactics.Dirty,"clicking an already selected creature preserves its unsaved route");
            app.Tactics.ClearRoute();
            app.VerificationPointer=new Vector2(-1000,-1000);yield return null;
            Require(app.Hud.PinnedInspectionId==own.units[0].id,"pinned stats survive leaving the creature");
            Require(app.World.CreatureRangeVisible,"pinned creatures retain a visible attack range");
            app.Hud.ShowSettings=true;yield return new WaitForSecondsRealtime(.25f);
            Require(app.Hud.ToolbarScreenRect.width==0&&app.Hud.Contains(app.Hud.CreatureRangeToggleScreenRect.center)&&app.Hud.Contains(app.Hud.CommanderRangeToggleScreenRect.center),"floating toolbar is removed and both settings switches capture the pointer");
            Rect settings=app.Hud.AudioScreenRect;
            Require(settings.xMin>=0&&settings.yMin>=0&&settings.xMax<=Screen.width+.5f&&settings.yMax<=Screen.height+.5f,"unified settings panel fits the native screen");
            yield return new WaitForEndOfFrame();Capture("settings-display-and-audio");
            app.Hud.ShowSettings=false;app.Hud.ShowCreatureRange=false;yield return null;
            Require(!app.World.CreatureRangeVisible,"creature attack range can be disabled while stats remain pinned");
            Require(app.Hud.PinnedInspectionId==own.units[0].id,"hiding attack range retains the pinned creature stats");
            app.Hud.ShowCreatureRange=true;yield return null;
            Require(app.World.CreatureRangeVisible,"creature attack range returns when the display switch is enabled");
            Click(pb,true);Require(app.Tactics.SelectedCount==2,"Shift click adds a second creature");
            Click(pb,true);Require(app.Tactics.SelectedCount==1,"Shift click toggles a selected creature");
            var min=Vector2.Min(pa,pb)-Vector2.one*34;var max=Vector2.Max(pa,pb)+Vector2.one*34;
            app.Tactics.ProcessTacticalPointer(min,true,true,false,false,false);
            app.Tactics.ProcessTacticalPointer(max,false,true,false,false,false);
            Require(!app.Tactics.BoxSelecting,"ordinary left drag does not start a selection marquee");
            app.Tactics.ProcessTacticalPointer(max,false,false,true,false,false);
            app.Tactics.ProcessTacticalPointer(min,true,true,false,true,false);
            app.Tactics.ProcessTacticalPointer(max,false,true,false,true,false);
            Require(app.Tactics.BoxSelecting,"drag displays a selection marquee");
            yield return new WaitForEndOfFrame();Capture("tactics-01-box-selection");
            app.Tactics.ProcessTacticalPointer(max,false,false,true,true,false);
            Require(app.Tactics.SelectedCount==2,"box selection selects only the two friendly creatures");
            yield return null;
            Require(app.World.TacticalSelectionCount==2,"world selection rings match the selected squad");
            Require(app.World.TacticalCircleCount==2,"each selected creature has exactly one terrain-conforming tactical ring");
            Point2 point=new Point2(0,0);Vector2 target=app.World.ViewCamera.WorldToScreenPoint(app.World.WorldSurfacePoint(point.x,point.y));
            app.Tactics.ProcessTacticalPointer(target,false,false,false,false,true);
            Require(app.Tactics.RouteNodeCount==1&&app.Tactics.DraftNodes[0].action=="capture","right click on altar creates a capture node");
            app.Tactics.AddWait();app.Tactics.CycleCondition(1);app.Tactics.ChangeSeconds(1,2);
            var command=app.Tactics.BuildPlanCommand();
            Require(command.unitIds.Length==2&&command.nodes.Length==2&&command.nodes[1].action=="wait"&&command.nodes[1].condition=="after_time","visual editor serializes squad, actions and time condition");
            command.nodes[0].x=250;Require(app.Tactics.DraftNodes[0].x!=250,"wire command cannot mutate unsaved visual draft");
            app.Tactics.SetSelectionForVerification(new[]{own.units[1].id});
            Require(app.Tactics.RouteNodeCount==2,"switching squads preserves the route while its snapshot is still stale");
            app.Tactics.SetSelectionForVerification(new[]{own.units[0].id,own.units[1].id});
            yield return new WaitForSecondsRealtime(.25f);yield return new WaitForEndOfFrame();Capture("tactics-02-node-program");
            app.Tactics.SetSelectionForVerification(new[]{own.units[0].id});
            app.Tactics.ClearRoute();app.Tactics.AddNode(new Point2(-80,130));app.Tactics.Save();
            app.Tactics.SetSelectionForVerification(new[]{own.units[0].id,own.units[1].id});
            Require(app.Tactics.RoutesMixed&&app.Tactics.RouteNodeCount==0,"selecting different routes never presents the first unit's route as a shared draft");
            System.Collections.Generic.IReadOnlyList<TacticNode> routeA,routeB;
            Require(app.Tactics.TryGetRoute(own.units[0].id,out routeA)&&routeA.Count==1&&app.Tactics.TryGetRoute(own.units[1].id,out routeB)&&routeB.Count==2,"mixed selection preserves each unit's existing route");
            yield return new WaitForEndOfFrame();Capture("tactics-03-mixed-squad-routes");
            app.Tactics.AddNode(new Point2(90,150));
            Require(!app.Tactics.RoutesMixed&&app.Tactics.RouteNodeCount==1&&app.Tactics.BuildPlanCommand().unitIds.Length==2,"a new route intentionally replaces a mixed squad draft for the full selection");
            app.Tactics.ClearRoute();
            for(int i=0;i<ArenaTactics.MaxNodes;i++)app.Tactics.AddNode(new Point2(-160+i*8,130));
            string lastAction=app.Tactics.DraftNodes[ArenaTactics.MaxNodes-1].action;
            app.Tactics.AddWait();
            Require(app.Tactics.RouteNodeCount==ArenaTactics.MaxNodes&&app.Tactics.DraftNodes[ArenaTactics.MaxNodes-1].action==lastAction,"adding wait at the node limit does not mutate the final node");
            app.Tactics.Save();
            Require(app.Tactics.PendingRouteCount==2,"saving a squad retains one pending route per selected creature");
            Require(app.Tactics.ReconcilePendingRoutes(Time.unscaledTime+6)&&app.Tactics.PendingRouteCount==0&&app.Tactics.RouteNodeCount==0,"an unacknowledged route expires back to the authoritative route");
            app.Tactics.AddWait();app.Tactics.Save();app.Tactics.ChangeSeconds(0,1);
            app.Tactics.ReconcilePendingRoutes(Time.unscaledTime+6);
            Require(app.Tactics.Dirty&&app.Tactics.RouteNodeCount==1&&app.Tactics.DraftNodes[0].seconds==6,"route timeout preserves a newer unsaved local edit");
            Rect editor=app.Hud.EditorScreenRect;
            Require(editor.xMin>=0&&editor.yMin>=0&&editor.xMax<=Screen.width+.5f&&editor.yMax<=Screen.height+.5f,"tactical editor stays within native screen at this resolution");
            var eliminated=Fixture("courtyards","clear","tactics");eliminated.players[1].eliminated=true;Show(eliminated);
            yield return null;
            Require(Find(eliminated.players[1].units[0].id)==null,"eliminated player's bench creatures do not return during tactics");
            var selecting=Fixture("courtyards","clear","tactics");Show(selecting);app.Tactics.ProcessInput();yield return new WaitForSecondsRealtime(.2f);
            var selectedView=Find(selecting.players[0].units[0].id);
            Vector2 dragAt=app.World.ViewCamera.WorldToScreenPoint(selectedView.transform.position+Vector3.up*(selectedView.Height*selectedView.Size*.5f));
            string pickedId;bool picked=app.World.TryPickUnit(dragAt,out pickedId);
            app.Tactics.ProcessTacticalPointer(dragAt,true,true,false,false,false);
            app.Tactics.ProcessTacticalPointer(dragAt+new Vector2(24,8),false,true,false,false,false);
            Require(app.PlacementDragging&&!app.Tactics.BoxSelecting,"holding a creature starts placement rather than box selection"+
                " [pick="+picked+":"+pickedId+", arrange="+app.CanArrangeCreatures+", hud="+app.Hud.Contains(dragAt)+", captured="+app.Hud.PointerCaptured+", position="+dragAt+"]");
            app.Tactics.ProcessTacticalPointer(app.Hud.EditorScreenRect.center,false,false,true,false,false);
            Require(!app.PlacementDragging&&!app.Tactics.BoxSelecting,"releasing a creature over the editor cancels its drag cleanly");
            app.Tactics.ProcessTacticalPointer(new Vector2(Screen.width*.6f,Screen.height*.55f),true,true,false,true,false);
            app.Tactics.ProcessTacticalPointer(new Vector2(Screen.width*.7f,Screen.height*.65f),false,true,false,true,false);
            Require(app.Tactics.BoxSelecting,"shift drag begins a fresh marquee");
            app.Tactics.ProcessTacticalPointer(new Vector2(Screen.width*.7f,Screen.height*.65f),false,false,false,true,false);
            Require(!app.Tactics.BoxSelecting,"a lost mouse-up clears the marquee instead of leaving a stale overlay");
            app.CancelDragging();app.Tactics.CancelTargeting();
            var battle=Fixture("courtyards","storm","battle");
            battle.battlefield.zones.Add(new CommandZoneState{id="drum-zone",kind="drums",ownerId=own.id,slot=0,x=60,y=50,radius=100,remaining=5});
            battle.battlefield.hazards.Add(new HazardState{id="storm-warning",kind="lightning",x=0,y=0,radius=70,delay=1.2f,remaining=2});
            Show(battle);app.Tactics.ProcessInput();app.Tactics.ArmSkill();
            Require(app.Tactics.Targeting,"Q card supports explicit targeting mode");
            app.VerificationPointer=app.World.ViewCamera.WorldToScreenPoint(app.World.WorldSurfacePoint(0,250));
            app.Hud.ShowCommanderRange=true;yield return null;
            Require(app.World.CommanderRangeVisible,"commander cast distance appears while aiming");
            yield return new WaitForEndOfFrame();Capture("battle-command-aiming");
            app.Hud.ShowCommanderRange=false;yield return null;
            Require(!app.World.CommanderRangeVisible,"settings can hide the commander cast distance");
            app.Hud.ShowCommanderRange=true;app.Tactics.CancelTargeting();yield return null;
            Require(!app.Tactics.Targeting&&!app.World.CommanderRangeVisible,"leaving targeting hides the commander cast distance");
            yield return new WaitForSecondsRealtime(.6f);yield return new WaitForEndOfFrame();Capture("battle-command-and-warning");
            foreach(var renderer in app.World.GetComponentsInChildren<Renderer>())
                foreach(var material in renderer.sharedMaterials)Require(material!=null&&material.shader!=null&&material.shader.isSupported,"active world material has a supported shader");
            IEnumerator tacticalChecks=VerifyTacticalPickingAndProjection();
            while(tacticalChecks.MoveNext())yield return tacticalChecks.Current;
            report.success=true;Write();Debug.Log("ARENA_BATTLEFIELD_OK "+report.checks.Count+" checks / "+report.captures.Count+" nonblack captures");
            if(Array.IndexOf(args,"-smoke-exit")>=0)Application.Quit(0);
        }
        GameState Fixture(string map,string weather,string phase)
        {
            var s=new GameState{hostId=app.PlayerId,phase=phase,round=1,timeLeft=120,battlefield=BattlefieldCatalog.Create(map,weather,19)};
            s.battlefield.revealAge=6;s.battlefield.elapsed=12;
            for(int slot=0;slot<4;slot++){
                string id=slot==0?app.PlayerId:"probe-"+slot;
                var p=new PlayerState{id=id,name=slot==0?"本地指挥官":"军团 "+slot,slot=slot,commanderSkill="drums",gold=12};
                p.steve=ArenaGame.LocalToWorld(slot,0,330);
                for(int i=0;i<2;i++){
                    var at=slot==0?new Point2(i==0?-55:60,55):ArenaGame.LocalToWorld(slot,i==0?-55:60,265);
                    var u=new UnitState{id=id+"-unit-"+i,kind=i==0?"zombie":"skeleton",x=at.x,y=at.y};p.units.Add(u);
                    var f=ArenaGame.PreviewFighter(p,u);f.id=id+"-fighter-"+i;f.unitId=u.id;f.ownerId=id;f.slot=slot;f.x=u.x;f.y=u.y;
                    s.fighters.Add(f);
                }
                s.players.Add(p);
            }
            return s;
        }
        void Show(GameState state){app.VerificationState=state;app.World.SetState(state,app.PlayerId);}
        void OnGUI()
        {
            if(!rasterRoute||app==null)return;
            int depth=GUI.depth;GUI.depth=-10000;
            app.Hud.DrawTacticalSegmentForVerification(rasterFrom,rasterTo,rasterColor,3);
            GUI.depth=depth;
        }
        IEnumerator VerifyTacticalPickingAndProjection()
        {
            app.Hud.ClosePinnedInspection();app.Hud.ShowSettings=false;
            app.Tactics.CancelTargeting();app.World.ResetView();
            var bodyFixture=Fixture("bog","rain","tactics");
            bodyFixture.players[0].units[0].kind="turtle";
            bodyFixture.players[0].units[0].x=0;bodyFixture.players[0].units[0].y=30;
            bodyFixture.players[0].units[1].kind="allay";
            bodyFixture.players[0].units[1].x=100;bodyFixture.players[0].units[1].y=50;
            bodyFixture.players[1].units[0].kind="zombie";
            bodyFixture.players[1].units[0].x=0;bodyFixture.players[1].units[0].y=57;
            Show(bodyFixture);app.Tactics.ProcessInput();
            yield return new WaitForSecondsRealtime(.65f);
            var turtle=Find(bodyFixture.players[0].units[0].id);var fairy=Find(bodyFixture.players[0].units[1].id);
            var blocker=Find(bodyFixture.players[1].units[0].id);
            Bounds turtleBounds=new Bounds(),fairyBounds=new Bounds();
            Require(app.World.TryGetBodyBounds(turtle,out turtleBounds)&&app.World.TryGetBodyBounds(fairy,out fairyBounds),"flat and flying species expose their current animated body bounds");
            Vector3 center;Require(app.World.TryGetUnitSelectionPoint(turtle.UnitId,out center)&&Vector3.Distance(center,turtleBounds.center)<.001f,"turtle marquee anchor matches its rendered body rather than an expanded capsule");
            string picked;VoxelCreature inspected;FighterState fighter;
            Vector2 fairyAt=app.World.ViewCamera.WorldToScreenPoint(fairyBounds.center);
            Require(app.World.TryPickUnit(fairyAt,out picked)&&picked==fairy.UnitId,"hovering a flying body's rendered position selects that species");
            bool overlap=false;
            for(int row=0;row<5&&!overlap;row++)for(int column=0;column<5&&!overlap;column++){
                Vector3 sample=turtleBounds.center+new Vector3((column-2)*turtleBounds.extents.x*.25f,(row-2)*turtleBounds.extents.y*.25f,0);
                Vector2 screen=app.World.ViewCamera.WorldToScreenPoint(sample);
                blocker.gameObject.SetActive(false);bool friendly=app.World.TryPickUnit(screen,out picked)&&picked==turtle.UnitId;
                blocker.gameObject.SetActive(true);
                if(!friendly||!app.World.TryInspectCreature(screen,out inspected,out fighter)||inspected!=blocker)continue;
                overlap=true;Require(!app.World.TryPickUnit(screen,out picked),"an enemy body blocks clicking through to the friendly turtle behind it");
            }
            Require(overlap,"the native fixture contains genuinely overlapping friendly and enemy rendered bodies");
            Vector3 upper=app.World.ViewCamera.WorldToScreenPoint(turtleBounds.center);
            for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                upper.y=Mathf.Max(upper.y,app.World.ViewCamera.WorldToScreenPoint(turtleBounds.center+Vector3.Scale(turtleBounds.extents,new Vector3(x,y,z))).y);
            blocker.gameObject.SetActive(false);
            Require(!app.World.TryPickUnit(new Vector2(upper.x,upper.y+4),out picked),"empty sky above a flat creature is not captured by its generic capsule");
            blocker.gameObject.SetActive(true);
            app.Tactics.SetSelectionForVerification(new[]{turtle.UnitId});
            yield return null;yield return new WaitForEndOfFrame();Capture("tactics-turtle-rendered-picking-and-selection");

            foreach(string map in new[]{"bog","courtyards","frost"}){
                var fixture=Fixture(map,map=="bog"?"rain":map=="frost"?"snow":"clear","tactics");
                foreach(PlayerState p in fixture.players)p.units.Clear();
                Show(fixture);app.Tactics.ProcessInput();
                yield return new WaitForSecondsRealtime(.65f);
                // These diagonals cross wetland banks and the actual low/ramp/highland profiles.
                rasterFrom=map=="bog"?new Point2(-130,-65):map=="courtyards"?new Point2(-65,-115):new Point2(98,68);
                rasterTo=map=="bog"?new Point2(130,65):map=="courtyards"?new Point2(65,65):new Point2(195,185);
                rasterRoute=true;yield return new WaitForEndOfFrame();
                VerifyRoutePixels(map+"-normal");Capture("tactics-raster-"+map);
                rasterRoute=false;
                if(map=="courtyards"){
                    Point2 ground;
                    int checkedPoints=0;
                    // The old five-plane fixed-point iteration drifted/oscillated along
                    // both edges of the steep 25-unit approach to this plateau.
                    for(int x=-88;x<=88;x+=11)for(int y=-112;y<=112;y+=7){
                        Vector3 surface=app.World.WorldSurfacePoint(x,y);Vector2 screen=app.World.ViewCamera.WorldToScreenPoint(surface);
                        float renderedHeight;
                        if(BattlefieldCatalog.HeightAt(fixture.battlefield,x,y)>0){
                            bool rendered=app.World.TryGetRenderedRampHeight(x,y,out renderedHeight);
                            bool matches=rendered&&Mathf.Abs(renderedHeight-surface.y)<.0005f;
                            Require(matches,matches?"rendered plateau and all four corner ramps match the authoritative height field":
                                "ramp mesh mismatch at ("+x+","+y+"): mesh="+renderedHeight.ToString("F6")+" heightfield="+surface.y.ToString("F6"));
                        }
                        // Visibility and picking must use the same ray. A point lifted
                        // above a back slope can be visible while the actual ground is
                        // hidden by the nearer rim, where selecting that rim is correct.
                        if(!app.World.IsTacticalPointVisible(surface))continue;
                        bool hit=app.World.TryGround(screen,out ground);
                        bool aligned=hit&&Mathf.Abs(ground.x-x)<.12f&&Mathf.Abs(ground.y-y)<.12f;
                        Require(aligned,aligned?"highland ray picking stays aligned with every visible slope sample":
                            "highland ray picking mismatch: expected ("+x+","+y+") actual ("+ground.x.ToString("F5")+","+ground.y.ToString("F5")+") hit="+hit+
                            " surface="+surface.ToString("F5")+" screen="+screen.ToString("F3")+" camera="+app.World.ViewCamera.transform.position.ToString("F5"));
                        checkedPoints++;
                    }
                    Require(checkedPoints>100,"ground picking exercises more than one hundred visible ramp and plateau samples");
                    Require(!app.World.IsTacticalPointVisible(app.World.WorldSurfacePoint(50,45)-Vector3.up*.8f),"route projection rejects a point behind opaque highland geometry");
                    Require(!app.World.IsTacticalPointVisible(new Vector3(200,0,200)),"route projection rejects points outside the current camera viewport");
                    app.World.Zoom(2,new Vector2(Screen.width*.5f,Screen.height*.5f));
                    yield return new WaitForSecondsRealtime(.65f);
                    rasterRoute=true;yield return new WaitForEndOfFrame();VerifyRoutePixels("courtyards-zoomed");rasterRoute=false;
                    app.World.ResetView();
                }
                if(map=="bog"){
                    int slot=fixture.players[0].slot;fixture.players[0].slot=fixture.players[1].slot;fixture.players[1].slot=slot;
                    Show(fixture);yield return new WaitForSecondsRealtime(1);
                    rasterRoute=true;yield return new WaitForEndOfFrame();VerifyRoutePixels("bog-rotated-camera");rasterRoute=false;
                }
            }
            app.World.ResetView();app.VerificationPointer=new Vector2(-1000,-1000);
        }
        void VerifyRoutePixels(string name)
        {
            var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0,false);image.Apply(false,false);
            int expected=0,painted=0,longestGap=0,gap=0;
            for(int n=2;n<199;n++){
                float t=n/200f;Vector3 point=app.World.WorldSurfacePoint(Mathf.Lerp(rasterFrom.x,rasterTo.x,t),Mathf.Lerp(rasterFrom.y,rasterTo.y,t))+Vector3.up*.16f;
                if(!app.World.IsTacticalPointVisible(point)){gap=0;continue;}
                Vector3 screen=app.World.ViewCamera.WorldToScreenPoint(point);int sx=Mathf.RoundToInt(screen.x),sy=Mathf.RoundToInt(screen.y);bool found=false;
                for(int y=Mathf.Max(0,sy-3);y<=Mathf.Min(image.height-1,sy+3)&&!found;y++)for(int x=Mathf.Max(0,sx-3);x<=Mathf.Min(image.width-1,sx+3);x++){
                    Color c=image.GetPixel(x,y);if(c.g>.9f&&c.r<.25f&&c.b<.45f){found=true;break;}
                }
                expected++;if(found){painted++;gap=0;}else{gap++;longestGap=Mathf.Max(longestGap,gap);}
            }
            Destroy(image);
            Require(expected>80,"raster "+name+" has more than eighty visible projected surface samples");
            Require(painted>=expected*.96f&&longestGap<=4,"raster "+name+" stays continuous and aligned with its projected terrain path at "+Screen.width+"x"+Screen.height+" ["+painted+"/"+expected+", gap="+longestGap+"]");
        }
        VoxelCreature Find(string id){foreach(var v in app.World.GetComponentsInChildren<VoxelCreature>())if(v.UnitId==id&&v.gameObject.activeInHierarchy)return v;return null;}
        void Click(Vector2 at,bool shift){app.Tactics.ProcessTacticalPointer(at,true,true,false,shift,false);app.Tactics.ProcessTacticalPointer(at,false,false,true,shift,false);}
        void Require(bool value,string text){if(!value)throw new InvalidOperationException(text);if(!report.checks.Contains(text))report.checks.Add(text);}
        void Capture(string name)
        {
            var texture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
            texture.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0,false);texture.Apply(false,false);
            int lit=0,pink=0,samples=0;float low=1,high=0;
            for(int y=10;y<texture.height;y+=19)for(int x=10;x<texture.width;x+=19){
                Color c=texture.GetPixel(x,y);float l=c.grayscale;low=Mathf.Min(low,l);high=Mathf.Max(high,l);if(l>.025f)lit++;
                if(c.r>.85f&&c.b>.85f&&c.g<.12f)pink++;samples++;
            }
            Require(lit>samples*.65f&&high-low>.15f,"capture "+name+" contains a nonblack rendered world");
            Require(pink<samples*.02f,"capture "+name+" contains no missing-shader pink surface");
            File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());Destroy(texture);report.captures.Add(name+".png");
        }
        void Write(){File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        void Fail(string error){failed=true;report.error=error;Debug.LogError("ARENA_BATTLEFIELD_FAILED "+error);if(folder!=null)Write();Application.Quit(2);}
    }
}
