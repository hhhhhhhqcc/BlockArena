using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BlockArena.Core;
using BlockArena.Networking;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Optional native regression: authoritative attributes travel through the shipping
    // snapshot/JSON codec before the actual HUD and objective picking are exercised.
    public sealed class ArenaEnvironmentUiProbe : MonoBehaviour
    {
        [Serializable] sealed class Report { public bool success;public string error;public int width,height;public List<string> checks=new List<string>();public List<string> captures=new List<string>(); }
        readonly Report report=new Report();ArenaApp app;string folder;bool failed;float started;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested(){if(Array.IndexOf(Environment.GetCommandLineArgs(),"-environment-ui-test")>=0)new GameObject("Native environment UI verification").AddComponent<ArenaEnvironmentUiProbe>();}
        void Update(){if(!failed&&started>0&&Time.realtimeSinceStartup-started>100)Fail("environment UI verification exceeded 100 seconds");}
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0&&!failed){
                IEnumerator run=stack.Peek();bool moved=false;object next=null;
                try{moved=run.MoveNext();if(moved)next=run.Current;}catch(Exception error){Fail(error.ToString());}
                if(failed)yield break;if(!moved){stack.Pop();continue;}if(next is IEnumerator){stack.Push((IEnumerator)next);continue;}yield return next;
            }
        }
        IEnumerator Run()
        {
            started=Time.realtimeSinceStartup;folder=Path.Combine(Application.dataPath,"..","Captures","EnvironmentUi");
            string[] args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);report.width=Screen.width;report.height=Screen.height;
            app=ArenaApp.Instance;app.VerificationInput=true;app.VerificationPointer=new Vector2(-200,-200);yield return Settle();
            Require(app.Hud.FrontActionScreenRect("help")==Rect.zero&&app.Hud.GameMenuActionScreenRect("help")==Rect.zero&&app.Hud.SettingsActionScreenRect("help")==Rect.zero,"help page has no main menu, Escape or settings entry");
            Require(typeof(ArenaApp).GetField("ShowHelp")==null,"removed help page has no runtime feature state");
            foreach(string action in new[]{"single","host","join","bestiary","settings","exit"})Require(Fits(app.Hud.FrontActionScreenRect(action)),"remaining main action fits: "+action);
            app.Hud.ShowSettings=true;yield return Settle();Require(Fits(app.Hud.SettingsActionScreenRect("bestiary"))&&Fits(app.Hud.SettingsActionScreenRect("room")),"remaining exploration actions fit settings");
            Capture("01-settings-without-help");app.Hud.ShowSettings=false;
            app.PortText=FreePort().ToString();app.Host(false);float deadline=Time.realtimeSinceStartup+10;
            while(!app.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Connected,"real local session supplies production inspection input");app.ShowRoom=false;
            ArenaGame game=CreateCombat();FighterState original=game.State.fighters.Find(f=>f.ownerId==app.PlayerId);
            FighterState baseline=ArenaGame.PreviewFighter(game.State.players[0],game.State.players[0].units[0]);
            original.slowTime=10;original.slowMultiplier=.55f;game.Tick(.04f);
            GameState state=RoundTrip(game.SnapshotFor(app.PlayerId));state.phase="battle";Show(state);yield return new WaitForSecondsRealtime(1.4f);yield return Settle();
            PlayerState owner=state.players.Find(p=>p.id==app.PlayerId);FighterState fighter=state.fighters.Find(f=>f.ownerId==app.PlayerId);
            app.Hud.PinInspection(CreatureCatalog.Get(fighter.kind),fighter,owner.units[0],owner,Vector2.zero);yield return Settle();
            string attributes=app.Hud.PinnedInspectionAttributeText;
            Require(attributes.Contains("移速 "+(baseline.speed*.85f*.55f).ToString("0.#")),"fixed inspection displays snow plus slow movement after snapshot and JSON transport");
            Require(attributes.Contains("攻速 "+(baseline.attackSpeed*.9f).ToString("0.##")+"/秒"),"fixed inspection displays authoritative snow attack speed");
            Require(attributes.Contains("护甲 "+(fighter.armor*100).ToString("0.#")+"%")&&attributes.Contains("射程 "+fighter.range.ToString("0.#")),"fixed inspection uses current armor and attack range");
            fighter.stunTime=2;Require(app.Hud.PinnedInspectionAttributeText.Contains("移速 0\n"),"stunned creature reports zero effective movement");fighter.stunTime=0;
            fighter.rootTime=2;Require(app.Hud.PinnedInspectionAttributeText.Contains("移速 0\n"),"rooted creature reports zero effective movement");fighter.rootTime=0;
            state.battlefield.weather="fog";Require(app.Hud.PinnedInspectionAttributeText.Contains("视野 385"),"ordinary fog sight uses the shared 385 range");
            state.battlefield.objectives.Add(new ObjectiveState{id="probe-owned-tower",kind="tower",ownerId=owner.id});
            Require(app.Hud.PinnedInspectionAttributeText.Contains("视野 500.5"),"captured tower raises fog sight without a duplicate UI formula");
            state.battlefield.objectives.RemoveAt(state.battlefield.objectives.Count-1);state.battlefield.weather="snow";
            Require(Fits(app.Hud.PinnedInspectionScreenRect)&&Inside(app.Hud.PinnedInspectionScreenRect,app.Hud.PinnedInspectionSkillsScreenRect),"expanded attributes retain an on-screen independent skill viewport");
            Capture("02-live-attributes");app.Hud.ClosePinnedInspection();
            foreach(string kind in CreatureCatalog.Kinds){
                var definition=CreatureCatalog.Get(kind);string text=app.Hud.BestiaryAttributeText(kind);
                Require(text.Contains("移速 ")&&text.Contains("护甲 ")&&text.Contains("视野 ")&&text.Contains(definition.flying?"飞行":"地面"),"bestiary exposes movement, protection and sight for "+kind);
            }
            app.ShowBestiary=true;yield return Settle();Capture("03-bestiary-attributes");app.ShowBestiary=false;
            var coveredKinds=new HashSet<string>();
            foreach(string map in BattlefieldCatalog.Maps){
                state=ObjectiveFixture(map);Show(state);app.World.ResetView();yield return new WaitForSecondsRealtime(1.4f);yield return Settle();
                foreach(ObjectiveState objective in state.battlefield.objectives){
                    if(coveredKinds.Contains(objective.kind))continue;
                    Vector2 pixel=Vector2.zero;bool found=false;
                    for(int turn=0;turn<4&&!found;turn++){
                        found=FindObjectivePixel(objective,out pixel);
                        if(!found){app.World.RotateView(1);yield return new WaitForSecondsRealtime(1.1f);yield return Settle();}
                    }
                    Require(found,"visible geometry can be inspected for objective "+objective.kind);
                    app.VerificationPointer=pixel;app.InspectPointer(pixel);yield return Settle();
                    if(app.Hud.HoveredObjectiveId!=objective.id){Capture("failed-objective-"+objective.kind);throw new InvalidOperationException("production pointer selects the precise "+objective.kind+" structure; "+InspectionDiagnostic(pixel));}
                    Require(true,"production pointer selects the precise "+objective.kind+" structure");
                    Require(app.Hud.HoveredObjectiveText.Contains("3 秒")&&app.Hud.HoveredObjectiveText.Contains("最多 +4"),"objective tooltip describes real capture and capped income rules: "+objective.kind);
                    Require(Fits(app.Hud.HoverTooltipScreenRect),"compact objective tooltip fits the viewport: "+objective.kind);
                    Capture("04-objective-"+objective.kind);coveredKinds.Add(objective.kind);
                    app.ShowGameMenu=true;app.InspectPointer(pixel);Require(app.Hud.HoveredObjectiveId==null,"Escape menu prevents objective inspection through UI");app.ShowGameMenu=false;
                    if(objective.kind=="supply"){
                        objective.claimed=true;Require(app.Hud.ObjectiveDescription(objective).Contains("不再刷新")&&!app.Hud.ObjectiveDescription(objective).Contains("+1"),"claimed supply no longer advertises a collectible reward");objective.claimed=false;
                    }
                    PlayerState viewer=state.players[0];FighterState distant=ArenaGame.PreviewFighter(viewer,viewer.units[0]);
                    distant.x=objective.x>0?-700:700;distant.y=objective.y>0?-700:700;
                    state.fighters.Add(distant);state.phase="battle";app.World.SetState(state,app.PlayerId);app.Hud.ClearHover();
                    Require(!app.World.IsInArmySight(objective.x,objective.y),"distant objective lies outside actual shared army sight: "+objective.kind);
                    ObjectiveState obscured;Require(app.World.TryInspectObjective(pixel,out obscured)&&obscured.id==objective.id,"ordinary map objectives remain inspectable outside army targeting sight: "+objective.kind);
                    app.VerificationPointer=pixel;app.InspectPointer(pixel);Require(app.Hud.HoveredObjectiveId==objective.id,"production pointer exposes the visible map objective without fullscreen sight fog: "+objective.kind);
                    state.fighters.Clear();state.phase="tactics";app.VerificationPointer=new Vector2(-200,-200);app.World.SetState(state,app.PlayerId);app.Hud.ClearHover();yield return Settle();
                }
                state.phase="reveal";app.World.SetState(state,app.PlayerId);ObjectiveState hidden;
                Require(!app.World.TryInspectObjective(new Vector2(Screen.width/2,Screen.height/2),out hidden),"unfinished reveal cannot expose objective hover");
            }
            Require(coveredKinds.Count==5,"all five objective families have native pointer and tooltip coverage");
            app.VerificationState=null;app.Leave();report.success=true;WriteReport();Debug.Log("ARENA_ENVIRONMENT_UI_OK "+report.checks.Count+" checks / "+report.captures.Count+" captures");Application.Quit(0);
        }
        ArenaGame CreateCombat()
        {
            var game=new ArenaGame(app.PlayerId,"属性实测",71);game.AddPlayer("environment-opponent","远端军团");
            game.State.preferredMap="frost";game.State.preferredWeather="snow";game.Apply(app.PlayerId,new GameCommand{type="start",bots=false});
            foreach(var player in game.State.players){
                player.units.Clear();player.units.Add(new UnitState{id="ui-unit-"+player.slot,kind="skeleton",x=player.slot==0?-650:650,y=player.slot==0?-650:650});
                game.Apply(player.id,new GameCommand{type="skip"});game.Apply(player.id,new GameCommand{type="ready"});
            }
            for(int tick=0;tick<300&&game.State.phase!="tactics";tick++)game.Tick(.1f);
            Require(game.State.phase=="tactics","authoritative preparation and reveal naturally reach tactics");
            foreach(var player in game.State.players)game.Apply(player.id,new GameCommand{type="ready"});
            for(int tick=0;tick<30&&game.State.phase!="battle";tick++)game.Tick(.1f);
            Require(game.State.phase=="battle","authoritative ready commands naturally start combat");
            foreach(var f in game.State.fighters){f.x=f.slot==0?-650:650;f.y=f.slot==0?-650:650;}
            return game;
        }
        GameState ObjectiveFixture(string map)
        {
            var state=new GameState{phase="tactics",round=5,timeLeft=120,hostId=app.PlayerId,battlefield=BattlefieldCatalog.CreateExpanded(map,"clear",29)};state.battlefield.revealAge=30;
            var owner=new PlayerState{id=app.PlayerId,name="目标巡览",slot=0,hp=20,steve=new Point2(0,700)};
            owner.units.Add(new UnitState{id="objective-ui-unit",kind="skeleton",x=-120,y=620});state.players.Add(owner);return state;
        }
        bool FindObjectivePixel(ObjectiveState target,out Vector2 pixel)
        {
            Vector3 center=app.World.ViewCamera.WorldToScreenPoint(app.World.WorldSurfacePoint(target.x,target.y)+Vector3.up);
            bool found=false;float best=float.PositiveInfinity;pixel=Vector2.zero;
            for(int y=-70;y<=85;y+=3)for(int x=-50;x<=50;x+=3){
                float score=x*x+y*y;if(score>=best)continue;
                Vector2 candidate=new Vector2(center.x+x,center.y+y);
                // A fixture soldier stands just in front of the first courtyard cache.
                // Match production priority so that a visible creature always owns its pixel.
                // Use an interior cluster of real triangle hits; never enlarge the pick area.
                if(!ExactObjectivePixel(candidate,target.id)||!ExactObjectivePixel(candidate+Vector2.left,target.id)||
                    !ExactObjectivePixel(candidate+Vector2.right,target.id)||!ExactObjectivePixel(candidate+Vector2.up,target.id)||
                    !ExactObjectivePixel(candidate+Vector2.down,target.id))continue;
                pixel=candidate;best=score;found=true;
            }
            return found;
        }
        bool ExactObjectivePixel(Vector2 pixel,string objectiveId)
        {
            if(pixel.x<5||pixel.y<5||pixel.x>Screen.width-5||pixel.y>Screen.height-5||app.Hud.Contains(pixel))return false;
            VoxelCreature creature;FighterState fighter;if(app.World.TryInspectCreature(pixel,out creature,out fighter))return false;
            ObjectiveState hit;return app.World.TryInspectObjective(pixel,out hit)&&hit.id==objectiveId;
        }
        string InspectionDiagnostic(Vector2 pixel)
        {
            VoxelCreature creature;FighterState fighter;bool body=app.World.TryInspectCreature(pixel,out creature,out fighter);
            ObjectiveState objective;bool structure=app.World.TryInspectObjective(pixel,out objective);
            return "pixel="+pixel+", UI="+app.Hud.Contains(pixel)+", creature="+(body?creature.UnitId:"none")+
                ", objective="+(structure?objective.id:"none")+", tooltip="+(app.Hud.HoveredObjectiveId??"none")+
                ", inputBlocked="+app.BattlefieldInputBlocked+", pointerCaptured="+app.Hud.PointerCaptured;
        }
        static GameState RoundTrip(GameState state){var codec=new UnityWireCodec();return codec.Decode(codec.Encode(new WirePacket{state=state})).state;}
        void Show(GameState state){app.VerificationState=state;app.ShowRoom=false;app.ShowGameMenu=false;app.ShowBestiary=false;app.VerificationPointer=new Vector2(-200,-200);app.Hud.ClearHover();app.World.SetState(state,app.PlayerId);}
        IEnumerator Settle(){yield return null;yield return new WaitForEndOfFrame();yield return null;yield return new WaitForEndOfFrame();}
        static bool Fits(Rect r){return r.width>0&&r.height>0&&r.x>=-.5f&&r.y>=-.5f&&r.xMax<=Screen.width+.5f&&r.yMax<=Screen.height+.5f;}
        static bool Inside(Rect outer,Rect inner){return inner.width>0&&inner.height>0&&outer.Contains(inner.min+Vector2.one)&&outer.Contains(inner.max-Vector2.one);}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        void Require(bool condition,string description){if(!condition)throw new InvalidOperationException(description);report.checks.Add(description);}
        void Capture(string name){Texture2D frame=ScreenCapture.CaptureScreenshotAsTexture();int visible=0,total=0;for(int y=0;y<frame.height;y+=Math.Max(1,frame.height/40))for(int x=0;x<frame.width;x+=Math.Max(1,frame.width/60)){Color color=frame.GetPixel(x,y);if(Mathf.Max(color.r,color.g,color.b)>.035f)visible++;total++;}Require(visible>total*.2f,name+" contains rendered scene pixels");string file=name+".png";File.WriteAllBytes(Path.Combine(folder,file),frame.EncodeToPNG());Destroy(frame);report.captures.Add(file);}
        void WriteReport(){if(folder!=null)File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        void Fail(string error){failed=true;report.error=error;WriteReport();if(app!=null){app.VerificationState=null;app.Leave();}Debug.LogError("ARENA_ENVIRONMENT_UI_FAILED "+error);Application.Quit(2);}
    }
}
