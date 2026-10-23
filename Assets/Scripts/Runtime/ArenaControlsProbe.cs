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
    // Opt-in checks call the same binding, camera and pointer paths used by the live game.
    public sealed class ArenaControlsProbe : MonoBehaviour
    {
        [Serializable] sealed class Report { public bool success; public string error; public int width,height; public List<string> checks=new List<string>(); public List<string> captures=new List<string>(); }
        readonly Report report=new Report();
        ArenaApp app; string folder; bool failed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested() { if(Array.IndexOf(Environment.GetCommandLineArgs(),"-controls-test")>=0)new GameObject("Native controls verification").AddComponent<ArenaControlsProbe>(); }
        IEnumerator Start()
        {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){
                IEnumerator current=stack.Peek();bool moved=false;object next=null;
                try{moved=current.MoveNext();if(moved)next=current.Current;}catch(Exception error){Fail(error.ToString());}
                if(failed)yield break;if(!moved){stack.Pop();continue;}if(next is IEnumerator){stack.Push((IEnumerator)next);continue;}yield return next;
            }
        }
        IEnumerator Run()
        {
            folder=Path.Combine(Application.dataPath,"..","Captures","Controls");string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);report.width=Screen.width;report.height=Screen.height;
            app=ArenaApp.Instance;app.VerificationInput=true;app.VerificationPointer=new Vector2(-200,-200);
            Require(app.Keys!=null,"native application owns one binding service");
            Require(app.Keys.Key("cast")==KeyCode.R&&app.Keys.Key("rotate-left")==KeyCode.Q&&app.Keys.Key("rotate-right")==KeyCode.E,"defaults separate R commander skill from Q/E quarter-turn camera");
            var unique=new HashSet<KeyCode>();foreach(string action in ArenaKeyBindings.Actions)Require(unique.Add(app.Keys.Key(action)),"default binding is unique: "+action);
            app.Hud.ShowSettings=true;app.Hud.SetSettingsTab(2);yield return Settle();
            Require(app.Hud.SettingsTabForVerification==2,"native settings exposes its own third controls tab");
            foreach(string action in ArenaKeyBindings.Actions)Require(ArenaKeyBindings.Allowed(app.Keys.Key(action)),"default binding is permitted: "+action);
            for(int i=0;i<ArenaKeyBindings.Actions.Length;i++)Require(Fits(app.Hud.KeyBindingScreenRect(i)),"binding button stays on screen: "+ArenaKeyBindings.Actions[i]);
            Require(Fits(app.Hud.KeyBindingsResetScreenRect),"reset bindings control stays on screen");Capture("01-controls-default");
            app.Hud.BeginKeyBinding("cast");Require(!app.Hud.SubmitKeyBinding(KeyCode.Q),"conflicting camera key cannot silently replace the skill");
            Require(app.Hud.KeyBindingActive&&app.Keys.Key("cast")==KeyCode.R,"rejected duplicate keeps the previous binding and waits for another key");
            Require(!app.Hud.SubmitKeyBinding(KeyCode.Return)&&!app.Hud.SubmitKeyBinding(KeyCode.KeypadEnter),"both Enter keys stay reserved for chat");
            Require(!app.Hud.SubmitKeyBinding(KeyCode.LeftShift)&&!app.Hud.SubmitKeyBinding(KeyCode.Escape),"selection modifier and escape cannot become gameplay bindings");
            Require(app.Hud.SubmitKeyBinding(KeyCode.P)&&app.Keys.Key("cast")==KeyCode.P&&!app.Hud.KeyBindingActive,"valid remap updates the live commander key through the UI submission path");
            Require(ArenaHud.CommanderTooltipBody("drums",app.Keys.Label("cast")).Contains("P 瞄准"),"commander descriptions show the remapped key");
            string feedback;Require(app.Keys.TryBind("talk",KeyCode.T,out feedback)&&app.Voice.Status.Contains("T"),"voice hint follows the remapped push-to-talk key");
            app.Hud.BeginKeyBinding("forward");app.HandleEscape();Require(app.Hud.ShowSettings&&!app.Hud.KeyBindingActive,"Escape cancels rebinding before it closes the settings page");
            Require(app.Keys.Writes==0,"native verification never changes saved player key preferences");
            Capture("02-controls-remapped");app.Keys.ResetDefaults();app.Hud.ShowSettings=false;
            app.PortText=FreePort().ToString();app.Host(false);float deadline=Time.realtimeSinceStartup+10;
            while(!app.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Connected,"actual TCP room establishes before battlefield interaction checks");app.ShowRoom=false;
            GameState state=Fixture();app.VerificationState=state;app.World.SetState(state,app.PlayerId);yield return new WaitForSecondsRealtime(1.4f);yield return Settle();
            PlayerState owner=state.players[0];UnitState unit=owner.units[0];app.Hud.PinInspection(CreatureCatalog.Get(unit.kind),null,unit,owner,Vector2.zero);yield return Settle();
            Rect initial=app.Hud.PinnedInspectionScreenRect;Require(Fits(initial),"new pinned inspection opens within the viewport");
            Vector2 grab=app.Hud.PinnedInspectionDragScreenRect.center,delta=new Vector2(-Screen.width*.21f,Screen.height*.09f);
            app.Hud.ProcessInspectionPointer(grab,true,true,false);Require(app.Hud.InspectionDragging&&app.Hud.PointerCaptured,"inspection header grabs and owns pointer input");
            app.Hud.ProcessInspectionPointer(grab+delta,false,true,false);Rect moved=app.Hud.PinnedInspectionScreenRect;
            Require(Vector2.Distance(initial.position,moved.position)>Screen.width*.08f,"pinned inspection follows the dragged header");
            Require(app.Hud.Contains(new Vector2(Screen.width-3,Screen.height-3)),"dragging blocks world input even outside the panel");
            app.Hud.ProcessInspectionPointer(grab+delta,false,false,true);Require(!app.Hud.InspectionDragging&&app.Hud.PointerCaptured,"release frame remains owned by inspection instead of placing creatures");yield return Settle();
            Require(!app.Hud.PointerCaptured,"inspection releases its pointer on the next frame");
            Require(app.Hud.PinnedInspectionId==unit.id&&app.Hud.PinnedInspectionSkillCount>0,"dragging preserves selected creature and its skill content");
            Capture("03-inspection-dragged");
            grab=app.Hud.PinnedInspectionDragScreenRect.center;app.Hud.ProcessInspectionPointer(grab,true,true,false);
            app.Hud.ProcessInspectionPointer(new Vector2(-Screen.width*2,Screen.height*3),false,true,false);Require(Fits(app.Hud.PinnedInspectionScreenRect),"pinned inspection clamps at top-left viewport edges");
            app.Hud.ProcessInspectionPointer(new Vector2(Screen.width*3,-Screen.height*2),false,false,true);Require(Fits(app.Hud.PinnedInspectionScreenRect),"pinned inspection clamps at bottom-right viewport edges");yield return Settle();
            Vector2 body=app.Hud.PinnedInspectionScreenRect.center;app.Hud.ProcessInspectionPointer(body,true,true,false);Require(!app.Hud.InspectionDragging,"body and skill scrolling do not drag the header-only panel");app.Hud.ProcessInspectionPointer(body,false,false,true);yield return Settle();
            Capture("04-inspection-clamped");app.Hud.ClosePinnedInspection();yield return Settle();
            Quaternion baseline=app.World.ViewCamera.transform.rotation;
            for(int turn=1;turn<=4;turn++){
                app.World.RotateView(1);yield return new WaitForSecondsRealtime(1.3f);yield return Settle();
                Require(app.World.ViewQuarterTurns==turn%4,"camera accumulates exact quarter-turn state "+turn);
                Quaternion expected=Quaternion.AngleAxis(turn*90,Vector3.up)*baseline;
                Require(Quaternion.Angle(expected,app.World.ViewCamera.transform.rotation)<.12f,"camera settles to exactly 90 degrees per press "+turn);
                Vector2 right=app.World.LocalMovementToWorld(Vector2.right);Vector3 visualRight=app.World.ViewCamera.transform.right;visualRight.y=0;visualRight.Normalize();
                Require(Vector2.Dot(right,new Vector2(visualRight.x,visualRight.z))>.999f,"movement stays relative to rendered camera "+turn);
                Point2 target=new Point2(520,460),picked;Vector3 world=app.Tactics.WorldPoint(target.x,target.y,0);Vector3 screen=app.World.ViewCamera.WorldToScreenPoint(world);
                Require(screen.z>0&&screen.x>0&&screen.x<Screen.width&&screen.y>0&&screen.y<Screen.height&&app.World.IsTacticalPointVisible(world),"expanded terrain point is truly visible after quarter-turn "+turn);
                Require(app.World.TryGround(screen,out picked)&&Mathf.Abs(picked.x-target.x)<.15f&&Mathf.Abs(picked.y-target.y)<.15f,"ground picking matches rotated battlefield "+turn);
                if(turn==1||turn==3)Capture("05-camera-quarter-"+turn);
            }
            app.World.RotateView(-1);yield return new WaitForSecondsRealtime(1.3f);Require(app.World.ViewQuarterTurns==3,"left camera rotation reverses by one quarter-turn");
            app.World.ResetView();yield return new WaitForSecondsRealtime(1.3f);Require(app.World.ViewQuarterTurns==0&&Quaternion.Angle(baseline,app.World.ViewCamera.transform.rotation)<.12f,"reset restores the commander-facing default view");
            Require(app.Hud.AcceptCommunicationEnter(true,"",1000),"Enter remains the chat submission key");
            Require(!app.Hud.AcceptCommunicationEnter(true,"中文候选",1001)&&!app.Hud.AcceptCommunicationEnter(true,"",1002)&&app.Hud.AcceptCommunicationEnter(true,"",1003),"IME confirmation does not send a chat draft");
            Require(app.Keys.Writes==0,"all controls checks leave player preferences untouched");
            app.VerificationState=null;app.Leave();report.success=true;WriteReport();Debug.Log("ARENA_CONTROLS_OK "+report.checks.Count+" checks / "+report.captures.Count+" captures");Application.Quit(0);
        }
        GameState Fixture()
        {
            var state=new GameState{phase="tactics",round=3,timeLeft=120,hostId=app.PlayerId,battlefield=BattlefieldCatalog.CreateExpanded("courtyards","clear",23)};state.battlefield.revealAge=20;
            var owner=new PlayerState{id=app.PlayerId,name="按键与视角",slot=0,gold=35,hp=20,steve=ArenaGame.LocalToWorld(0,0,360)};
            owner.units.Add(new UnitState{id="controls-unit",kind="dolphin",x=-65,y=270,paidPrice=12});state.players.Add(owner);return state;
        }
        IEnumerator Settle(){yield return null;yield return new WaitForEndOfFrame();yield return null;yield return new WaitForEndOfFrame();}
        bool Fits(Rect r){return r.width>0&&r.height>0&&r.x>=-.5f&&r.y>=-.5f&&r.xMax<=Screen.width+.5f&&r.yMax<=Screen.height+.5f;}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        void Capture(string name){Texture2D frame=ScreenCapture.CaptureScreenshotAsTexture();int total=0,visible=0;for(int y=0;y<frame.height;y+=Mathf.Max(1,frame.height/40))for(int x=0;x<frame.width;x+=Mathf.Max(1,frame.width/60)){total++;Color c=frame.GetPixel(x,y);if(Mathf.Max(c.r,c.g,c.b)>.035f)visible++;}Require(visible>total*.25f,name+" renders visible scene content");string file=name+".png";File.WriteAllBytes(Path.Combine(folder,file),frame.EncodeToPNG());Destroy(frame);report.captures.Add(file);}
        void Require(bool result,string description){if(!result)throw new InvalidOperationException(description);report.checks.Add(description);}
        void WriteReport(){if(!string.IsNullOrEmpty(folder))File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        void Fail(string message){failed=true;report.error=message;WriteReport();if(app!=null){app.VerificationState=null;app.Keys.ResetDefaults(false);app.Leave();}Debug.LogError("ARENA_CONTROLS_FAILED "+message);Application.Quit(2);}
    }
}
