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
    // Opt-in native regression for measured CJK/rich-text skill rows and phase-gated UI.
    public sealed class ArenaExpansionUiProbe : MonoBehaviour
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
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-expansion-ui-test")>=0)
                new GameObject("Native expanded arena interface verification").AddComponent<ArenaExpansionUiProbe>();
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
            folder=Path.Combine(Application.dataPath,"..","Captures","ExpansionUi");
            string[] args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);report.width=Screen.width;report.height=Screen.height;
            app=ArenaApp.Instance;app.VerificationInput=true;app.VerificationPointer=new Vector2(-200,-200);
            yield return Settle();
            app.PortText=FreePort().ToString();app.Host(false);
            float deadline=Time.realtimeSinceStartup+10;
            while(!app.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Connected,"actual TCP session establishes before native UI fixtures");
            app.ShowRoom=false;
            var dolphin=Fixture("prep","dolphin",false);Show(dolphin);yield return Settle();
            Pin(dolphin);yield return Settle();
            Require(Fits(app.Hud.PinnedInspectionScreenRect),"dolphin fixed inspection fits the native viewport");
            Require(app.Hud.PinnedSkillBodyLineCount("dolphin_heal")>=2,"dolphin healing ability exercises a genuinely wrapped description");
            Require(app.Hud.PinnedInspectionSkillText.Contains(ArenaDescription.Brief(CreatureAbilities.Get("dolphin_heal"))),"dolphin summary preserves its complete final phrase");
            Rect[] dolphinLines=app.Hud.PinnedSkillBodyLineScreenRects("dolphin_heal");
            Require(dolphinLines.Length>=2&&Inside(app.Hud.PinnedInspectionSkillsScreenRect,dolphinLines[dolphinLines.Length-1]),"last dolphin healing line is inside the skill scroll viewport");
            Require(LineHasGlyphPixels(dolphinLines[dolphinLines.Length-1]),"last dolphin healing line actually renders visible text pixels");
            Capture("01-dolphin-full-ability");

            var abilityIds=new HashSet<string>();
            foreach(string kind in CreatureCatalog.Kinds)foreach(string id in CreatureCatalog.Get(kind).skills)abilityIds.Add(id);
            foreach(string id in abilityIds)foreach(float rowWidth in new[]{250f,316f,360f})
                Require(app.Hud.PinnedSkillTextFits(CreatureAbilities.Get(id),rowWidth),"complete skill glyphs fit measured rows: "+id+" at "+rowWidth);
            foreach(string kind in new[]{"wither","ender_dragon","warden"}){
                var boss=Fixture("battle",kind,true);Show(boss);yield return Settle();Pin(boss);yield return Settle();
                Require(Fits(app.Hud.PinnedInspectionScreenRect)&&!app.Hud.PinnedInspectionScreenRect.Overlaps(app.Hud.CommandDockScreenRect),kind+" panel fits without covering command controls");
                Require(app.Hud.PinnedInspectionSkillCount==boss.fighters[0].skills.Length,kind+" retains every skill in fixed inspection");
                Capture("02-boss-"+kind);
            }
            var many=Fixture("battle","wither",true);many.fighters[0].skills=new List<string>(abilityIds).GetRange(0,Mathf.Min(18,abilityIds.Count)).ToArray();
            Show(many);yield return Settle();Pin(many);yield return Settle();
            Require(app.Hud.PinnedInspectionSkillsScrollable,"long skill list has a bounded internal scroll view");
            app.Hud.PinnedInspectionSkillScroll=new Vector2(0,100000);yield return Settle();
            string lastId=many.fighters[0].skills[many.fighters[0].skills.Length-1];
            Rect[] lastLines=app.Hud.PinnedSkillBodyLineScreenRects(lastId);
            Require(lastLines.Length>0&&Inside(app.Hud.PinnedInspectionSkillsScreenRect,lastLines[lastLines.Length-1]),"scrolling reaches the final line of the final skill");
            Require(LineHasGlyphPixels(lastLines[lastLines.Length-1]),"final long-list skill line renders visible text pixels");
            Capture("03-long-skills-last-line");app.Hud.ClosePinnedInspection();

            foreach(string phase in new[]{"prep","recruit","tactics","battle"}){
                Show(Fixture(phase,"dolphin",false));yield return Settle();
                Require(app.Hud.OwnedSkillButtonScreenRect.width==0,phase+" hides the useless zero-skill button");
                Show(Fixture(phase,"dolphin",true));yield return Settle();
                Require(Fits(app.Hud.OwnedSkillButtonScreenRect),phase+" keeps purchased skills accessible");
            }
            foreach(string stage in new[]{"results","arena","map","terrain","weather","finished"}){
                var hidden=Fixture(stage=="results"||stage=="finished"?stage:"reveal","dolphin",true);hidden.revealStage=stage;
                Show(hidden);app.Hud.ShowOwnedSkills=true;yield return Settle();
                Require(app.Hud.OwnedSkillButtonScreenRect.width==0,stage+" does not render an out-of-phase skill button");
                Require(!app.Hud.ShowOwnedSkills,stage+" closes an obsolete purchased-skill panel");
                if(stage!="finished")Require(!app.Hud.Contains(app.Hud.OwnedSkillButtonAnchorScreenRect.center),stage+" has no invisible skill-button hitbox");
                if(stage=="results"||stage=="terrain")Capture("04-no-skill-button-"+stage);
            }
            var recruit=Fixture("recruit","dolphin",true);recruit.players[0].recruitPending=true;recruit.players[0].shop=new[]{"dolphin","skeleton","wither"};
            Show(recruit);app.Hud.SetRecruitVisible(true);yield return Settle();
            Require(app.Hud.RecruitVisible&&app.Hud.OwnedSkillButtonScreenRect.width==0,"recruit overlay hides underlying skill controls");
            app.Hud.SetRecruitVisible(false);yield return Settle();
            Require(Fits(app.Hud.OwnedSkillButtonScreenRect),"collapsing recruitment restores purchased-skill access");
            app.VerificationState=null;app.Leave();report.success=true;WriteReport();
            Debug.Log("ARENA_EXPANSION_UI_OK "+report.checks.Count+" checks / "+report.captures.Count+" nonblack captures");Application.Quit(0);
        }
        GameState Fixture(string phase,string kind,bool owned)
        {
            var state=new GameState{phase=phase,round=3,timeLeft=120,hostId=app.PlayerId};
            if(phase=="battle"||phase=="tactics"||phase=="results"||phase=="reveal"){
                state.battlefield=BattlefieldCatalog.Create("courtyards","clear",23);state.battlefield.revealAge=6;
            }
            var owner=new PlayerState{id=app.PlayerId,name="界面验证",slot=0,gold=35,hp=20,steve=ArenaGame.LocalToWorld(0,0,360)};
            for(int i=0;i<2;i++){
                Point2 at=ArenaGame.LocalToWorld(0,i==0?-65:65,270);
                var unit=new UnitState{id="ui-expansion-unit-"+i,kind=i==0?kind:"skeleton",x=at.x,y=at.y,paidPrice=12};owner.units.Add(unit);
                var fighter=ArenaGame.PreviewFighter(owner,unit);fighter.id=unit.id;fighter.unitId=unit.id;fighter.ownerId=owner.id;fighter.slot=0;state.fighters.Add(fighter);
            }
            if(owned)foreach(string id in TradeCatalog.Ids)if(TradeCatalog.Get(id).type=="skill"){owner.upgrades.Add(id);break;}
            state.players.Add(owner);return state;
        }
        void Pin(GameState state)
        {
            PlayerState owner=state.players[0];UnitState unit=owner.units[0];FighterState fighter=state.phase=="battle"?state.fighters[0]:null;
            app.Hud.PinInspection(CreatureCatalog.Get(unit.kind),fighter,unit,owner,Vector2.zero);
        }
        void Show(GameState state){app.Hud.ClosePinnedInspection();app.VerificationState=state;app.ShowRoom=false;app.ShowShop=false;app.World.SetState(state,app.PlayerId);}
        IEnumerator Settle(){yield return new WaitForSecondsRealtime(.22f);yield return new WaitForEndOfFrame();}
        static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
        static bool Fits(Rect r){return r.width>0&&r.height>0&&r.xMin>=-.5f&&r.yMin>=-.5f&&r.xMax<=Screen.width+.5f&&r.yMax<=Screen.height+.5f;}
        static bool Inside(Rect outer,Rect inner){return inner.width>0&&inner.height>0&&outer.xMin<=inner.xMin+.5f&&outer.yMin<=inner.yMin+.5f&&outer.xMax>=inner.xMax-.5f&&outer.yMax>=inner.yMax-.5f;}
        bool LineHasGlyphPixels(Rect r)
        {
            var frame=ScreenCapture.CaptureScreenshotAsTexture();int count=0;
            for(int y=Mathf.Max(0,Mathf.CeilToInt(r.yMin));y<Mathf.Min(frame.height,Mathf.FloorToInt(r.yMax));y++)
                for(int x=Mathf.Max(0,Mathf.CeilToInt(r.xMin));x<Mathf.Min(frame.width,Mathf.FloorToInt(r.xMax));x++){
                    Color c=frame.GetPixel(x,y);if(c.r>.45f&&c.g>.45f&&c.b>.38f)count++;
                }
            Destroy(frame);return count>=4;
        }
        void Capture(string name)
        {
            var frame=ScreenCapture.CaptureScreenshotAsTexture();int count=0,visible=0;
            for(int y=0;y<frame.height;y+=Mathf.Max(1,frame.height/40))for(int x=0;x<frame.width;x+=Mathf.Max(1,frame.width/60)){
                Color c=frame.GetPixel(x,y);count++;if(Mathf.Max(c.r,c.g,c.b)>.035f)visible++;
            }
            Require(visible>count*.25f,name+" has rendered scene content");string file=name+".png";
            File.WriteAllBytes(Path.Combine(folder,file),frame.EncodeToPNG());Destroy(frame);report.captures.Add(file);
        }
        void Require(bool value,string description){if(!value)throw new InvalidOperationException(description);report.checks.Add(description);}
        void WriteReport(){File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        void Fail(string message){failed=true;report.error=message;WriteReport();if(app!=null)app.Leave();Debug.LogError("ARENA_EXPANSION_UI_FAILED "+message);Application.Quit(2);}
    }
}
