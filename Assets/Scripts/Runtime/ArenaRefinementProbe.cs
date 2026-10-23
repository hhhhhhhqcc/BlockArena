using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    // A rendered-player regression of the exact pointer -> draft -> authority path.
    public sealed class ArenaRefinementProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool success;
            public int width,height;
            public string error;
            public List<string> checks=new List<string>();
            public List<string> captures=new List<string>();
        }
        readonly Report report=new Report();
        ArenaApp app;
        string folder;
        bool failed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-refinement-test")>=0)
                new GameObject("Native battlefield refinement verification").AddComponent<ArenaRefinementProbe>();
        }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){
                IEnumerator current=stack.Peek();bool moved=false;object next=null;
                try{moved=current.MoveNext();if(moved)next=current.Current;}catch(Exception exception){Fail(exception.ToString());}
                if(failed)yield break;
                if(!moved){stack.Pop();continue;}
                if(next is IEnumerator){stack.Push((IEnumerator)next);continue;}
                yield return next;
            }
        }
        IEnumerator Run()
        {
            folder=Path.Combine(Application.dataPath,"..","Captures","Refinement");
            string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);report.width=Screen.width;report.height=Screen.height;
            yield return new WaitForSecondsRealtime(.5f);
            app=ArenaApp.Instance;Require(app!=null&&app.World!=null,"native arena initialized");
            app.VerificationInput=true;app.VerificationPointer=new Vector2(-1000,-1000);
            app.PortText=FreePort().ToString();app.Host(false);
            float deadline=Time.realtimeSinceStartup+12;
            while(!app.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Connected,"local TCP handshake established before native pointer regression");
            app.ShowRoom=false;
            int fixtureRound=30;
            foreach(string map in BattlefieldCatalog.Maps){
                ArenaGame authority=Fixture(map);authority.State.round=fixtureRound++;
                Show(authority);app.World.ResetView();app.Tactics.ProcessInput();
                yield return Settle(1.4f);
                Require(app.Tactics.Active,map+" enters editable tactical planning");
                app.Tactics.SelectAll(false);
                Require(app.Tactics.SelectedCount==app.Me.units.Count,map+" selects actual rendered roster");
                foreach(int sx in new[]{-1,1})foreach(int sy in new[]{-1,1}){
                    bool tested=false;
                    foreach(float major in new[]{620f,700f,540f,760f}){
                        foreach(float minor in new[]{470f,580f,360f}){
                            float x=sx*major,y=sy*minor;
                            Vector3 surface=app.World.WorldSurfacePoint(x,y);
                            Vector3 projected=app.World.ViewCamera.WorldToScreenPoint(surface);
                            if(projected.z<=0||projected.x<12||projected.x>Screen.width-12||projected.y<12||projected.y>Screen.height-12)continue;
                            Vector2 screen=new Vector2(projected.x,projected.y);
                            if(app.Hud.Contains(screen)||!app.World.IsTacticalPointVisible(surface)||NearObjective(authority.State,x,y))continue;
                            Point2 picked;if(!app.World.TryGround(screen,out picked))continue;
                            if(Mathf.Abs(picked.x-x)>.15f||Mathf.Abs(picked.y-y)>.15f)continue;
                            int before=app.Tactics.RouteNodeCount;
                            Require(app.Tactics.ProcessTacticalPointer(screen,false,false,false,false,true),map+" right-click consumed in outer quadrant "+sx+","+sy);
                            Require(app.Tactics.RouteNodeCount==before+1,map+" right-click creates an outer route node "+sx+","+sy);
                            TacticNode node=app.Tactics.DraftNodes[before];
                            Require(Mathf.Abs(node.x-x)<.15f&&Mathf.Abs(node.y-y)<.15f,
                                map+" outer node retains clicked coordinates instead of clamping to 405: "+x+","+y+" -> "+node.x+","+node.y);
                            GameCommand command=app.Tactics.BuildPlanCommand();
                            authority.Apply(app.PlayerId,command);
                            GameState accepted=authority.SnapshotFor(app.PlayerId);
                            TacticNode stored=accepted.players.Find(player=>player.id==app.PlayerId).tactics[0].nodes[before];
                            Require(Mathf.Abs(stored.x-x)<.15f&&Mathf.Abs(stored.y-y)<.15f,map+" authority and snapshot preserve exterior node "+sx+","+sy);
                            tested=true;break;
                        }
                        if(tested)break;
                    }
                    Require(tested,map+" exercises a visible exterior point in quadrant "+sx+","+sy);
                }
                yield return Settle(.12f);Capture(map+"-overview-and-outer-routes");
                app.World.Zoom(4,new Vector2(Screen.width*.5f,Screen.height*.5f));
                app.World.Pan(new Vector2(5,-3));yield return Settle(.85f);
                bool zoomTested=false;
                for(int x=-740;x<=740&&!zoomTested;x+=80)for(int y=-700;y<=700&&!zoomTested;y+=80){
                    if(Mathf.Abs(x)<=420&&Mathf.Abs(y)<=420||NearObjective(authority.State,x,y))continue;
                    Vector3 surface=app.World.WorldSurfacePoint(x,y),projected=app.World.ViewCamera.WorldToScreenPoint(surface);
                    Vector2 screen=new Vector2(projected.x,projected.y);
                    if(projected.z<=0||screen.x<16||screen.x>Screen.width-16||screen.y<16||screen.y>Screen.height-16||app.Hud.Contains(screen)||!app.World.IsTacticalPointVisible(surface))continue;
                    Point2 hit;if(!app.World.TryGround(screen,out hit)||Mathf.Abs(hit.x-x)>.15f||Mathf.Abs(hit.y-y)>.15f)continue;
                    int before=app.Tactics.RouteNodeCount;app.Tactics.ProcessTacticalPointer(screen,false,false,false,false,true);
                    Require(app.Tactics.RouteNodeCount==before+1,map+" zoomed and panned outer click adds a node");
                    var node=app.Tactics.DraftNodes[before];
                    Require(Mathf.Abs(node.x-x)<.15f&&Mathf.Abs(node.y-y)<.15f,map+" zoomed/panned pointer retains expanded coordinates");
                    authority.Apply(app.PlayerId,app.Tactics.BuildPlanCommand());zoomTested=true;
                }
                Require(zoomTested,map+" tests exterior routing after camera zoom and pan");
                yield return Settle(.12f);Capture(map+"-tactical-close-view");
                app.Tactics.AddNode(new Point2(float.NaN,0),false);
                Require(app.Tactics.RouteNodeCount==5,map+" invalid pointer coordinate creates no corrupt node");
                Show(authority);app.World.ResetView();yield return Settle(.6f);
            }
            Require(ArenaGame.BattleSeconds==120,"authoritative battle limit is 120 seconds");
            app.VerificationState=null;app.Leave();report.success=true;WriteReport();
            Debug.Log("ARENA_REFINEMENT_OK "+report.checks.Count+" checks / "+report.captures.Count+" nonblack captures");
            Application.Quit(0);
        }
        ArenaGame Fixture(string map)
        {
            var game=new ArenaGame(app.PlayerId,"战术验证",145);
            for(int i=1;i<4;i++)game.AddPlayer("refinement-player-"+i,"军团 "+i);
            game.Apply(app.PlayerId,new GameCommand{type="world_options",mapId=map,weather="clear"});
            game.Apply(app.PlayerId,new GameCommand{type="start",bots=false});
            foreach(var player in game.State.players){game.Apply(player.id,new GameCommand{type="skip"});game.Apply(player.id,new GameCommand{type="ready"});}
            for(int i=0;i<500&&game.State.phase!="tactics";i++)game.Tick(.05f);
            Require(game.State.phase=="tactics",map+" server completes map reveal and random deployment");return game;
        }
        static bool NearObjective(GameState state,float x,float y)
        {
            foreach(var objective in state.battlefield.objectives){float dx=x-objective.x,dy=y-objective.y;if(dx*dx+dy*dy<85*85)return true;}return false;
        }
        void Show(ArenaGame game)
        {
            app.Hud.ClosePinnedInspection();app.VerificationState=game.SnapshotFor(app.PlayerId);
            app.ShowRoom=app.ShowShop=false;app.World.SetState(app.State,app.PlayerId);
        }
        IEnumerator Settle(float seconds){yield return new WaitForSecondsRealtime(seconds);yield return new WaitForEndOfFrame();}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        void Capture(string name)
        {
            var frame=ScreenCapture.CaptureScreenshotAsTexture();int samples=0,lit=0,pink=0;float low=1,high=0;
            for(int y=10;y<frame.height;y+=19)for(int x=10;x<frame.width;x+=19){Color c=frame.GetPixel(x,y);float l=c.grayscale;low=Mathf.Min(low,l);high=Mathf.Max(high,l);if(l>.025f)lit++;if(c.r>.85f&&c.b>.85f&&c.g<.12f)pink++;samples++;}
            Require(lit>samples*.65f&&high-low>.15f,"capture "+name+" contains an actual nonblack rendered world");
            Require(pink<samples*.02f,"capture "+name+" has no missing shaders");
            File.WriteAllBytes(Path.Combine(folder,name+".png"),frame.EncodeToPNG());Destroy(frame);report.captures.Add(name+".png");
        }
        void Require(bool condition,string description){if(!condition)throw new InvalidOperationException(description);report.checks.Add(description);}
        void WriteReport(){if(folder!=null)File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        void Fail(string message){failed=true;report.error=message;WriteReport();if(app!=null)app.Leave();Debug.LogError("ARENA_REFINEMENT_FAILED "+message);Application.Quit(2);}
    }
}
