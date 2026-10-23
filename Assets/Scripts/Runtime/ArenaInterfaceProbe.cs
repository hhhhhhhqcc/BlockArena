using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Opt-in native pixel regression for the shared HUD; inactive during ordinary play.
    public sealed class ArenaInterfaceProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool success;
            public int width, height;
            public string error;
            public List<string> checks = new List<string>();
            public List<string> captures = new List<string>();
        }
        readonly Report report = new Report();
        ArenaApp app;
        string folder;
        bool failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-interface-test") >= 0)
                new GameObject("Native interface verification").AddComponent<ArenaInterfaceProbe>();
        }
        IEnumerator Start()
        {
            var runs = new Stack<IEnumerator>(); runs.Push(Run());
            while (runs.Count > 0)
            {
                bool moved = false; object next = null;
                IEnumerator run = runs.Peek();
                try { moved = run.MoveNext(); if (moved) next = run.Current; }
                catch (Exception e) { Fail(e.ToString()); }
                if (failed) yield break;
                if (!moved) { runs.Pop(); continue; }
                if (next is IEnumerator) { runs.Push((IEnumerator)next); continue; }
                yield return next;
            }
        }
        IEnumerator Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            folder = Path.Combine(Application.dataPath, "..", "Captures", "Interface");
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-dir") folder = args[i + 1];
            Directory.CreateDirectory(folder); report.width = Screen.width; report.height = Screen.height;
            app = ArenaApp.Instance; app.VerificationInput = true;
            app.VerificationPointer = new Vector2(Screen.width * .52f, Screen.height * .50f);
            yield return new WaitForSecondsRealtime(.7f);
            yield return new WaitForEndOfFrame(); Capture("01-main-menu");
            app.Host(false);
            float deadline = Time.realtimeSinceStartup + 10;
            while (!app.Connected && Time.realtimeSinceStartup < deadline) yield return null;
            Require(app.Connected, "actual local TCP session established");
            yield return new WaitForSecondsRealtime(.3f);
            yield return new WaitForEndOfFrame(); Capture("02-lobby");
            app.ShowRoom = false;
            yield return VerifyCommanderTooltips("lobby", false);
            app.Tactics.SkillPickerOpen = false;

            var prep = Fixture("prep"); Show(prep);
            yield return new WaitForSecondsRealtime(.6f);
            Require(Fits(app.Hud.CommandDockScreenRect), "combined command dock stays within screen");
            Require(Fits(app.Hud.SessionSummaryScreenRect), "combined phase and battlefield summary stays within screen");
            Require(app.Hud.CommandDockScreenRect.Contains(app.Hud.PrimaryActionScreenRect.min) &&
                app.Hud.CommandDockScreenRect.Contains(app.Hud.PrimaryActionScreenRect.max - Vector2.one), "primary preparation action belongs to the command dock");
            yield return new WaitForEndOfFrame(); Capture("03-preparation-dock");
            app.ShowShop = true;
            yield return new WaitForSecondsRealtime(.5f);
            Require(Fits(app.Hud.MarketScreenRect), "draggable market stays within screen");
            yield return new WaitForEndOfFrame(); Capture("04-skill-market");
            app.ShowShop = false;
            app.Hud.ShowOwnedSkills = true;
            yield return new WaitForSecondsRealtime(.3f);
            Require(Fits(app.Hud.OwnedSkillsScreenRect), "owned skill collection stays within screen");
            yield return new WaitForEndOfFrame(); Capture("05-owned-skills");
            app.Hud.PreviewSkillForVerification(prep.players[0].upgrades[0]);
            yield return new WaitForSecondsRealtime(.3f);
            Require(Fits(app.Hud.SkillDetailsScreenRect), "skill details stay within screen");
            yield return VerifyOverlayColours("skill-details", app.Hud.SkillDetailsScreenRect);
            app.Hud.CloseSkillDetails(); app.Hud.ShowOwnedSkills = false;

            app.Tactics.SkillPickerOpen = true;
            yield return new WaitForSecondsRealtime(.4f);
            yield return VerifyCommanderTooltips("preparation", true);
            yield return VerifyOverlayColours("commander-picker", app.Hud.CommanderPickerScreenRect);
            app.Tactics.SkillPickerOpen = false;
            app.Hud.ShowSettings = true;
            yield return new WaitForSecondsRealtime(.4f);
            Require(Fits(app.Hud.AudioScreenRect), "dedicated settings page fits viewport");
            Require(app.Hud.Contains(Vector2.zero) && app.Hud.Contains(new Vector2(Screen.width-1,Screen.height-1)), "settings page owns all arena pointer input");
            Require(app.World.FrontendMode == "settings" && !app.World.GameplayWorldVisible, "settings uses its own background instead of the arena");
            yield return new WaitForEndOfFrame(); Capture("settings-page");
            app.Hud.ShowSettings = false;
            yield return new WaitForSecondsRealtime(.3f);

            var tactical = Fixture("tactics"); Show(tactical);
            yield return new WaitForSecondsRealtime(.5f);
            app.Tactics.SelectAll(false);
            app.Tactics.AddNode(new Point2(-85, 99), false);
            app.Tactics.AddNode(new Point2(82, 50), false);
            app.Tactics.AddNode(new Point2(130, 260), false);
            var owner = tactical.players[0]; var unit = owner.units[0];
            app.Hud.PinInspection(CreatureCatalog.Get(unit.kind), null, unit, owner, Vector2.zero);
            yield return new WaitForSecondsRealtime(.3f);
            Require(Fits(app.Hud.EditorScreenRect) && Fits(app.Hud.PinnedInspectionScreenRect), "tactical editor and pinned inspection stay within screen");
            Require(!app.Hud.EditorScreenRect.Overlaps(app.Hud.CommandDockScreenRect) &&
                !app.Hud.PinnedInspectionScreenRect.Overlaps(app.Hud.CommandDockScreenRect), "side panels leave the command dock unobstructed");
            yield return VerifyOverlayColours("creature-inspection", app.Hud.PinnedInspectionScreenRect);
            app.Hud.ClosePinnedInspection();
            yield return new WaitForEndOfFrame(); Capture("10-tactics-command-dock");

            var battle = Fixture("battle"); Show(battle);
            yield return new WaitForSecondsRealtime(.4f);
            app.Tactics.ArmSkill(false);
            app.VerificationPointer = app.World.ViewCamera.WorldToScreenPoint(app.World.WorldSurfacePoint(0, 200));
            yield return new WaitForSecondsRealtime(.2f);
            yield return new WaitForEndOfFrame(); Capture("11-battle-command");
            app.Tactics.CancelTargeting();

            var recruit = Fixture("recruit"); recruit.round = 4; recruit.battlefield = null;
            recruit.players[0].recruitPending = true;
            recruit.players[0].shop = new[] { "ender_dragon", "wither", "warden" };
            recruit.players[0].recruitOfferId = "interface-fixture";
            Show(recruit); yield return new WaitForSecondsRealtime(.4f);
            Require(app.Hud.RecruitVisible && app.Hud.RecruitEffectsFit, "boss recruitment cards and quantified abilities fit the native viewport");
            yield return new WaitForEndOfFrame(); Capture("12-boss-recruitment");
            yield return VerifyCommanderTooltips("recruitment", false);
            app.Tactics.SkillPickerOpen = false;
            app.Hud.SetRecruitVisible(false);
            yield return new WaitForSecondsRealtime(.2f);
            yield return new WaitForEndOfFrame(); Capture("13-recruitment-hidden");
            app.ShowBestiary = true;
            yield return new WaitForSecondsRealtime(.3f);
            yield return new WaitForEndOfFrame(); Capture("14-bestiary");
            app.ShowBestiary = false; app.Leave();
            report.success = true; WriteReport();
            Debug.Log("ARENA_INTERFACE_OK " + report.checks.Count + " checks / " + report.captures.Count + " nonblack captures");
            Application.Quit(0);
        }
        IEnumerator VerifyCommanderTooltips(string stage, bool all)
        {
            app.Tactics.SkillPickerOpen = true;
            yield return new WaitForSecondsRealtime(.25f);
            for (int i = 0; i < (all ? BattlefieldCatalog.Commanders.Length : 1); i++)
            {
                int choice = all ? i : 3;
                app.VerificationPointer = app.Hud.CommanderChoiceScreenRect(choice).center;
                yield return new WaitForSecondsRealtime(.12f);
                yield return new WaitForEndOfFrame();
                Rect tip = app.Hud.LastHoverScreenRect, picker = app.Hud.CommanderPickerScreenRect, dock = app.Hud.CommandDockScreenRect;
                if(!Fits(tip)){
                    Capture(stage+"-commander-tooltip-failure");
                    throw new InvalidOperationException(stage+" commander tooltip "+choice+" stays within screen: tip="+tip+", picker="+picker+", pointer="+app.VerificationPointer+", selectable="+app.Hud.CommanderSelectableForVerification+", skillPicker="+app.Tactics.SkillPickerOpen);
                }
                Require(Fits(tip), stage + " commander tooltip " + choice + " stays within screen");
                Require(!tip.Overlaps(picker) && (dock.width <= 0 || !tip.Overlaps(dock)),
                    stage + " commander tooltip " + choice + " leaves skill choices and command dock unobstructed");
            }
            app.VerificationPointer = new Vector2(Screen.width * .50f, Screen.height * .57f);
        }
        IEnumerator VerifyOverlayColours(string name, Rect overlay)
        {
            Require(Fits(overlay), name + " stays within screen");
            Require(!overlay.Overlaps(app.Hud.CommandDockScreenRect), name + " leaves the command dock unobstructed");
            app.VerificationPointer = new Vector2(Screen.width * .50f, Screen.height * .57f);
            yield return new WaitForSecondsRealtime(.25f);
            yield return new WaitForEndOfFrame();
            Rect primary = app.Hud.PrimaryActionScreenRect, tool = app.Hud.SecondaryActionScreenRect;
            Color beforePrimary = ButtonColour(primary), beforeTool = ButtonColour(tool);
            app.VerificationPointer = new Vector2(overlay.xMin + overlay.width * .7f, overlay.yMin + overlay.height * .5f);
            yield return new WaitForSecondsRealtime(.25f);
            Require(app.Hud.Contains(app.VerificationPointer.Value), name + " captures the pointer over its own surface");
            yield return new WaitForEndOfFrame();
            Color afterPrimary = ButtonColour(primary), afterTool = ButtonColour(tool);
            float primaryDelta = ColourDistance(beforePrimary, afterPrimary), toolDelta = ColourDistance(beforeTool, afterTool);
            Require(primaryDelta < .018f, name + " hover preserves primary button pixels (delta " + primaryDelta.ToString("F5") + ")");
            Require(toolDelta < .018f, name + " hover preserves owned-skills button pixels (delta " + toolDelta.ToString("F5") + ")");
            Capture("hover-" + name);
            app.VerificationPointer = new Vector2(Screen.width * .5f, Screen.height * .57f);
        }
        Color ButtonColour(Rect r)
        {
            Require(Fits(r), "sampled button stays within screen");
            int size = Mathf.Clamp(Mathf.RoundToInt(r.height * .12f), 3, 7);
            var pixels = new Texture2D(size, size, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(Mathf.RoundToInt(r.xMin + r.width * .76f), Mathf.RoundToInt(r.yMin + r.height * .24f), size, size), 0, 0, false);
            pixels.Apply(); Color sum = Color.black;
            foreach (Color value in pixels.GetPixels()) sum += value;
            Destroy(pixels); return sum / (size * size);
        }
        static float ColourDistance(Color a, Color b) { return Mathf.Max(Mathf.Abs(a.r-b.r), Mathf.Abs(a.g-b.g), Mathf.Abs(a.b-b.b)); }
        static bool Fits(Rect r) { return r.width > 0 && r.height > 0 && r.xMin >= -.5f && r.yMin >= -.5f && r.xMax <= Screen.width+.5f && r.yMax <= Screen.height+.5f; }
        GameState Fixture(string phase)
        {
            var state = new GameState { hostId=app.PlayerId, phase=phase, round=3, timeLeft=phase=="prep"||phase=="recruit"?45:120 };
            if (phase=="tactics" || phase=="battle") { state.battlefield=BattlefieldCatalog.Create("bog", "rain", 19); state.battlefield.revealAge=6; }
            for (int slot=0; slot<4; slot++)
            {
                string id=slot==0?app.PlayerId:"interface-"+slot;
                var owner=new PlayerState { id=id,name=slot==0?"本地指挥官":"军团 "+slot,slot=slot,gold=38,steve=ArenaGame.LocalToWorld(slot,0,360) };
                for (int i=0;i<2;i++)
                {
                    Point2 at=ArenaGame.LocalToWorld(slot,i==0?-65:70, i==0?265:300);
                    var unit=new UnitState { id=id+"-unit-"+i,kind=slot==0&&i==0?"turtle":i==0?"zombie":"skeleton",x=at.x,y=at.y,paidPrice=10 };
                    owner.units.Add(unit); var fighter=ArenaGame.PreviewFighter(owner,unit);
                    fighter.id=id+"-fighter-"+i;fighter.unitId=unit.id;fighter.ownerId=id;fighter.slot=slot;fighter.x=unit.x;fighter.y=unit.y;
                    state.fighters.Add(fighter);
                }
                if (slot==0) foreach (string skill in TradeCatalog.Ids)
                {
                    if (TradeCatalog.Get(skill).type!="skill") continue;
                    if(owner.upgrades.Count==0)owner.upgrades.Add(skill);
                    owner.market.Add(new ShopOffer { id="interface-market-"+owner.market.Count,itemId=skill,price=8 });
                    if(owner.market.Count==4)break;
                }
                state.players.Add(owner);
            }
            return state;
        }
        void Show(GameState state) { app.VerificationState=state;app.ShowRoom=false;app.ShowShop=false;app.World.SetState(state,app.PlayerId); }
        void Capture(string name)
        {
            Texture2D frame=ScreenCapture.CaptureScreenshotAsTexture();
            int visible=0, sampled=0;
            for(int y=0;y<frame.height;y+=Mathf.Max(1,frame.height/40)) for(int x=0;x<frame.width;x+=Mathf.Max(1,frame.width/60))
            {Color c=frame.GetPixel(x,y);if(Mathf.Max(c.r,c.g,c.b)>.035f)visible++;sampled++;}
            Require(visible>sampled*.25f, name+" capture has rendered scene content");
            string file=name+".png";File.WriteAllBytes(Path.Combine(folder,file),frame.EncodeToPNG());Destroy(frame);report.captures.Add(file);
        }
        void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message);report.checks.Add(message); }
        void WriteReport() { File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true)); }
        void Fail(string message) { failed=true;report.error=message;WriteReport();Debug.LogError("ARENA_INTERFACE_FAILED "+message);Application.Quit(2); }
    }
}
