using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BlockArena.Core;
using BlockArena.Networking;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Opt-in native verification of real frontend scenes, menu state and TCP lifecycle.
    public sealed class ArenaMenuProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool success;
            public string error;
            public int width, height;
            public List<string> checks = new List<string>();
            public List<string> captures = new List<string>();
        }
        readonly Report report = new Report();
        ArenaApp app;
        ArenaClient friend;
        ArenaServer remote;
        TcpListener silent;
        string folder;
        bool failed;
        void Update() { if (remote != null) remote.Pump(Time.unscaledDeltaTime); if (friend != null) friend.Pump(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-menu-test") >= 0)
                new GameObject("Native frontend verification").AddComponent<ArenaMenuProbe>();
        }
        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                IEnumerator run = stack.Peek(); bool moved = false; object next = null;
                try { moved = run.MoveNext(); if (moved) next = run.Current; }
                catch (Exception e) { Fail(e.ToString()); }
                if (failed) yield break;
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator) { stack.Push((IEnumerator)next); continue; }
                yield return next;
            }
        }
        IEnumerator Run()
        {
            folder = Path.Combine(Application.dataPath, "..", "Captures", "Menus");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-dir") folder = args[i + 1];
            Directory.CreateDirectory(folder); report.width = Screen.width; report.height = Screen.height;
            app = ArenaApp.Instance; app.VerificationInput = true; app.VerificationPointer = new Vector2(-200, -200);
            yield return Settle();
            CheckFrontend("main"); Require(Fits(app.Hud.FrontPanelScreenRect), "main menu fits viewport");
            foreach (string action in new[] { "single", "host", "join", "bestiary", "settings", "help", "exit" })
                Require(Inside(app.Hud.FrontPanelScreenRect, app.Hud.FrontActionScreenRect(action)), "main action " + action + " is visible");
            Color mainColour = BackgroundColour(); Capture("01-main-menu");
            int meshes = app.World.FrontendMeshObjectCount;
            Require(app.Hud.ToolbarScreenRect.width == 0, "persistent corner toolbar is removed");
            app.Hud.ShowSettings = true; yield return Settle();
            CheckFrontend("settings");
            Require(Fits(app.Hud.SettingsScreenRect), "dedicated settings page fits viewport");
            foreach (string action in new[] { "close", "music", "effects", "mute", "commander-range", "creature-range", "bestiary", "help", "room" })
                Require(Inside(app.Hud.SettingsScreenRect, app.Hud.SettingsActionScreenRect(action)), "settings action " + action + " fits viewport");
            Capture("01-settings-sound-assistance");
            app.Hud.SetSettingsTab(1); yield return Settle();
            foreach (string action in new[] { "windowed", "fullscreen", "resolution-prev", "resolution-next", "apply" })
                Require(Inside(app.Hud.SettingsScreenRect, app.Hud.SettingsActionScreenRect(action)), "display action " + action + " fits viewport");
            Capture("01-settings-display");
            app.HandleEscape(); yield return Settle();
            Require(!app.Hud.ShowSettings && !app.HasSession, "Escape returns from main settings without opening a room");
            CheckFrontend("main");
            app.Hud.SetFrontPage("host"); yield return Settle();
            CheckFrontend("host"); CheckConfig(false); Color hostColour = BackgroundColour(); Capture("02-host-setup");
            app.Host(false); yield return Await(() => app.Connected && app.State != null, "local hosting handshake");
            yield return Settle(); CheckFrontend("lobby_host"); CheckRoom(); Capture("03-host-waiting");
            int hostPort = int.Parse(app.PortText);
            friend = new ArenaClient(new UnityWireCodec()); friend.Connect("127.0.0.1", hostPort, "青石来客");
            yield return Await(() => friend.Connected && app.State.players.Count == 2, "friend joins real local lobby");
            Require(app.State.phase == "lobby", "joining does not automatically start the match");
            yield return Settle(); Capture("04-host-with-friend");
            app.HandleEscape(); Require(app.ShowGameMenu, "Escape opens the menu from a waiting lobby");
            app.ShowRoom = true; app.HandleEscape();
            Require(!app.ShowRoom && app.ShowGameMenu, "Escape returns from lobby room details to menu");
            app.HandleEscape(); Require(!app.ShowGameMenu, "Escape resumes the waiting lobby");
            app.Send(new GameCommand { type = "start", bots = true });
            yield return Await(() => app.State != null && app.State.phase == "recruit", "host starts the match with computer seats");
            app.Send(new GameCommand { type = "skip" });
            yield return Await(() => app.Me != null && !app.Me.recruitPending, "recruitment skipped through real TCP");
            app.ShowRoom = false; app.Hud.SetRecruitVisible(false); yield return Settle();
            Require(!app.World.FrontendVisible, "entering gameplay restores the arena");
            Point2 target = ArenaGame.LocalToWorld(app.Me.slot, 65, 360);
            app.MoveSteveTo(target); yield return new WaitForSecondsRealtime(.2f);
            app.HandleEscape(); yield return Settle();
            Require(app.ShowGameMenu && app.BattlefieldInputBlocked, "Escape opens the game menu and blocks arena controls");
            Require(Fits(app.Hud.GameMenuScreenRect), "Escape menu fits viewport");
            foreach (string action in new[] { "continue", "bestiary", "settings", "help", "room", "main", "exit" })
                Require(Inside(app.Hud.GameMenuScreenRect, app.Hud.GameMenuActionScreenRect(action)), "Escape action " + action + " is visible");
            Require(app.Hud.Contains(Vector2.zero), "Escape menu captures the entire arena input area");
            Require(Time.timeScale == 1, "networked game menu never pauses the simulation clock");
            Point2 stopped = app.Me.steve; float remaining = app.State.timeLeft;
            app.MoveSteveTo(ArenaGame.LocalToWorld(app.Me.slot, -90, 320));
            yield return new WaitForSecondsRealtime(.45f);
            Require(Distance(stopped, app.Me.steve) < 1, "opening menu stops server navigation and rejects further walk input");
            Require(app.State.timeLeft < remaining - .15f, "authoritative countdown keeps advancing behind menu");
            Capture("05-escape-menu");
            app.ShowHelp = true;
            for (int tab = 0; tab < 4; tab++)
            {
                app.Hud.SetHelpTab(tab); yield return Settle();
                Require(Fits(app.Hud.HelpPanelScreenRect) && Inside(app.Hud.HelpPanelScreenRect, app.Hud.HelpTabScreenRect(tab)), "help tab " + tab + " fits viewport");
                Capture("06-help-" + tab);
            }
            app.HandleEscape(); Require(!app.ShowHelp && app.ShowGameMenu, "Escape returns from help to game menu");
            app.Hud.ShowSettings = true; yield return Settle();
            Require(Fits(app.Hud.AudioScreenRect), "settings opened from game menu fit viewport"); Capture("07-menu-settings");
            app.HandleEscape(); Require(!app.Hud.ShowSettings && app.ShowGameMenu, "Escape returns from settings to menu");
            app.ShowBestiary = true; yield return Settle(); Capture("08-menu-bestiary");
            app.HandleEscape(); Require(!app.ShowBestiary && app.ShowGameMenu, "Escape returns from bestiary to menu");
            app.ShowRoom = true; yield return Settle(); CheckRoom(); Capture("09-menu-room");
            app.HandleEscape(); Require(!app.ShowRoom && app.ShowGameMenu, "Escape returns from room details to menu");
            app.HandleEscape(); Require(!app.ShowGameMenu && !app.BattlefieldInputBlocked, "Escape resumes normal arena input");

            for (int slot = 0; slot < 4; slot++)
            {
                Show(Fixture("prep", slot, "wither")); yield return Settle();
                foreach (Vector2 key in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down, Vector2.one.normalized })
                {
                    Vector2 direction = app.World.LocalMovementToWorld(key);
                    Vector3 origin = app.World.WorldSurfacePoint(0, 0), a = app.World.ViewCamera.WorldToScreenPoint(origin);
                    Vector3 b = app.World.ViewCamera.WorldToScreenPoint(origin + new Vector3(direction.x, 0, direction.y));
                    Require(direction.sqrMagnitude <= 1.001f && direction.sqrMagnitude > .9f &&
                        (key.x == 0 || (b.x - a.x) * key.x > .01f) && (key.y == 0 || (b.y - a.y) * key.y > .01f),
                        "slot " + slot + " camera-relative input " + key + " follows screen direction");
                }
            }
            Require(ArenaApp.ScreenMovementToWorld(Vector2.zero, Quaternion.identity) == Vector2.zero, "released WASD input stays zero");
            GameState inspection = Fixture("battle", 0, "wither"); Show(inspection); yield return Settle();
            PlayerState owner = inspection.players[0]; FighterState boss = inspection.fighters[0]; UnitState unit = owner.units[0];
            var definition = CreatureCatalog.Get(boss.kind);
            BlockArena.Presentation.VoxelCreature bossView = null;
            foreach (var view in app.World.GetComponentsInChildren<BlockArena.Presentation.VoxelCreature>())
                if (view.Kind == boss.kind && view.UnitId == unit.id) { bossView = view; break; }
            Require(bossView != null, "boss model is present for actual mouse inspection");
            float hoverDeadline = Time.unscaledTime + .45f;
            while (Time.unscaledTime < hoverDeadline)
            {
                app.VerificationPointer = app.World.ViewCamera.WorldToScreenPoint(bossView.transform.position + Vector3.up * (bossView.Height * bossView.Size * .5f));
                app.InspectPointer(app.VerificationPointer.Value);
                yield return null;
            }
            yield return new WaitForEndOfFrame();
            var names = new List<string>(); foreach (string id in boss.skills) names.Add(CreatureAbilities.Get(id).name);
            Require(app.World.HoveredCreature == bossView, "real mouse picking targets the inspected boss");
            Require(app.Hud.InspectionSkillText == string.Join("、", names.ToArray()), "hover inspection continues showing skill names only");
            Capture("10-hover-skill-names");
            Require(app.PinCreatureAt(app.VerificationPointer.Value), "clicking the boss opens fixed inspection");
            app.VerificationPointer = new Vector2(-200, -200); yield return Settle();
            CheckPinned(boss); Capture("11-pinned-boss-effects");
            var many = new List<string>();
            foreach (string kind in CreatureCatalog.Kinds) foreach (string id in CreatureCatalog.Get(kind).skills)
                if (!many.Contains(id) && many.Count < 18) many.Add(id);
            boss.skills = many.ToArray();
            app.Hud.PinInspection(definition, boss, unit, owner, Vector2.zero); yield return Settle();
            CheckPinned(boss); Require(app.Hud.PinnedInspectionSkillsScrollable, "large skill collection scrolls inside fixed panel");
            app.Hud.PinnedInspectionSkillScroll = new Vector2(0, 10000); yield return Settle(); Capture("12-pinned-many-effects");
            app.Hud.ClosePinnedInspection();
            inspection.phase = "tactics"; Show(inspection); yield return Settle();
            app.Tactics.SelectAll(false);
            Require(app.Tactics.SelectedCount > 0, "tactical selection remains available outside menu");
            app.ShowGameMenu = true;
            app.Tactics.ProcessTacticalPointer(new Vector2(Screen.width * .5f, Screen.height * .5f), true, true, false, true, false);
            Require(!app.Tactics.BoxSelecting && !app.Tactics.Targeting, "opening menu clears and blocks tactical pointer gestures");
            app.ShowGameMenu = false;
            friend.Dispose(); friend = null; app.VerificationState = null; app.Leave(); yield return Settle();
            app.Hud.SetFrontPage("join"); yield return Settle();
            CheckFrontend("join"); CheckConfig(true); Color joinColour = BackgroundColour(); Capture("13-join-setup");
            Require(ColourDistance(mainColour, hostColour) > .012f && ColourDistance(hostColour, joinColour) > .012f,
                "main, host and join render visibly distinct background scenes");
            silent = new TcpListener(IPAddress.Loopback, 0); silent.Start();
            app.Address = "127.0.0.1"; app.PortText = ((IPEndPoint)silent.LocalEndpoint).Port.ToString(); app.Join();
            yield return Settle(); Require(app.ConnectionPending && !app.Connected, "pending handshake has a real joining state");
            Require(Fits(app.Hud.FrontCancelScreenRect), "pending connection cancellation is visible"); Capture("14-join-connecting");
            app.HandleEscape();
            Require(!app.HasSession && app.Hud.FrontPage == "join", "Escape cancels connection while preserving join form");
            silent.Stop(); silent = null;
            remote = new ArenaServer(new UnityWireCodec()); remote.Start(0);
            friend = new ArenaClient(new UnityWireCodec()); friend.Connect("127.0.0.1", remote.Port, "远端房主");
            yield return Await(() => friend.Connected && friend.State != null, "independent remote room already has a host");
            app.PortText = remote.Port.ToString(); app.Join(); yield return Await(() => app.Connected && app.State != null, "guest joins independent local server");
            yield return Await(() => app.State.players.Count == 2, "guest sees the host and their own waiting seat");
            Require(app.State.hostId != app.PlayerId, "joining a friend's room preserves their host role");
            yield return Settle(); CheckFrontend("lobby_guest"); CheckRoom(); Capture("15-guest-waiting");
            Require(!app.Hosting, "joined room does not create another local host");
            app.Leave(); friend.Dispose(); friend = null; remote.Dispose(); remote = null; app.Hud.SetFrontPage("join");
            int closedPort = FreePort(); app.PortText = closedPort.ToString(); app.Join();
            yield return Await(() => !app.ConnectionPending, "connection refusal returns control");
            Require(!app.Connected && !string.IsNullOrEmpty(app.Status), "failed connection shows a recoverable status");
            yield return Settle(); Capture("16-join-error");
            app.CancelConnection(); app.HandleEscape(); yield return Settle();
            Require(app.Hud.FrontPage == "main" && !app.HasSession && !app.Hosting, "return to main clears all network sessions");
            for (int i = 0; i < 3; i++) { app.Hud.SetFrontPage("host"); yield return null; app.Hud.SetFrontPage("join"); yield return null; app.Hud.SetFrontPage("main"); yield return null; }
            yield return Settle(); CheckFrontend("main");
            Require(app.World.FrontendMeshObjectCount == meshes, "repeated menu navigation reuses scene mesh objects");
            Capture("17-main-restored");
            report.success = true; WriteReport();
            Debug.Log("ARENA_MENUS_OK " + report.checks.Count + " checks / " + report.captures.Count + " nonblack captures");
            Cleanup(); Application.Quit(0);
        }
        void CheckConfig(bool joining)
        {
            Rect panel = app.Hud.FrontConfigScreenRect;
            Require(Fits(panel) && Inside(panel, app.Hud.FrontNameScreenRect) && Inside(panel, app.Hud.FrontPortScreenRect), "connection form and identity fields fit viewport");
            Require(Inside(panel, app.Hud.FrontPrimaryScreenRect) && Inside(panel, app.Hud.FrontCancelScreenRect), "connection form actions fit viewport");
            if (joining) Require(Inside(panel, app.Hud.FrontAddressScreenRect), "server address field fits join form");
        }
        void CheckFrontend(string mode)
        {
            string scene = mode == "lobby_host" ? "host" : mode == "lobby_guest" ? "join" : mode;
            Require(app.World.FrontendVisible && app.World.FrontendMode == scene, "dedicated frontend mode " + mode + " is active");
            Require(app.World.FrontendRootCount == 1 && app.World.FrontendActiveSceneCount == 1, "exactly one cached frontend scene is visible");
            Vector3 subject = app.World.FrontendSubjectViewport;
            Require(subject.z > 0 && Vector2.Distance(new Vector2(subject.x, subject.y), app.World.FrontendSubjectViewportTarget) < .025f,
                "frontend subject stays framed to the right of the menu");
        }
        void CheckRoom()
        {
            Rect room = app.Hud.RoomPanelScreenRect;
            Require(Fits(room) && Inside(room, app.Hud.RoomCopyScreenRect) && Inside(room, app.Hud.RoomLeaveScreenRect), "compact room and address actions fit viewport");
            Require(Inside(room, app.Hud.RoomStartScreenRect), "room primary action fits viewport");
        }
        void CheckPinned(FighterState fighter)
        {
            Require(Fits(app.Hud.PinnedInspectionScreenRect) && !app.Hud.PinnedInspectionScreenRect.Overlaps(app.Hud.CommandDockScreenRect), "fixed inspection leaves command dock unobstructed");
            Require(Inside(app.Hud.PinnedInspectionScreenRect, app.Hud.PinnedInspectionSkillsScreenRect), "fixed skill effects stay in their own viewport");
            Require(app.Hud.PinnedInspectionSkillCount == fighter.skills.Length, "fixed inspection retains all creature skills");
            foreach (string id in fighter.skills)
            {
                var skill = CreatureAbilities.Get(id);
                Require(app.Hud.PinnedInspectionSkillText.Contains(skill.name + " · " + ArenaDescription.Brief(skill)), "fixed inspection shows concise effect for " + skill.name);
            }
        }
        GameState Fixture(string phase, int slot, string kind)
        {
            var state = new GameState { phase = phase, round = 3, timeLeft = 120, hostId = app.PlayerId };
            var owner = new PlayerState { id = app.PlayerId, name = "本地指挥官", slot = slot, gold = 35, hp = 20, steve = ArenaGame.LocalToWorld(slot, 0, 360) };
            for (int i = 0; i < 2; i++)
            {
                Point2 at = ArenaGame.LocalToWorld(slot, i == 0 ? -65 : 65, 270);
                var unit = new UnitState { id = "menu-fixture-" + i, kind = i == 0 ? kind : "skeleton", x = at.x, y = at.y, paidPrice = 14 };
                owner.units.Add(unit); var fighter = ArenaGame.PreviewFighter(owner, unit);
                fighter.id = "fighter-" + i; fighter.unitId = unit.id; fighter.ownerId = owner.id; fighter.slot = slot; fighter.x = at.x; fighter.y = at.y; state.fighters.Add(fighter);
            }
            state.players.Add(owner); return state;
        }
        void Show(GameState state) { app.VerificationState = state; app.ShowRoom = false; app.World.SetState(state, app.PlayerId); }
        IEnumerator Settle() { yield return new WaitForSecondsRealtime(.25f); yield return new WaitForEndOfFrame(); }
        IEnumerator Await(Func<bool> condition, string label)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(condition(), label);
        }
        static int FreePort() { var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port; }
        static float Distance(Point2 a, Point2 b) { float x = a.x - b.x, y = a.y - b.y; return Mathf.Sqrt(x*x + y*y); }
        static bool Fits(Rect r) { return r.width > 0 && r.height > 0 && r.xMin >= -.5f && r.yMin >= -.5f && r.xMax <= Screen.width+.5f && r.yMax <= Screen.height+.5f; }
        static bool Inside(Rect outer, Rect inner) { return inner.width > 0 && inner.height > 0 && outer.Contains(inner.min + Vector2.one) && outer.Contains(inner.max - Vector2.one); }
        static float ColourDistance(Color a, Color b) { return Mathf.Abs(a.r-b.r) + Mathf.Abs(a.g-b.g) + Mathf.Abs(a.b-b.b); }
        Color BackgroundColour()
        {
            var frame = ScreenCapture.CaptureScreenshotAsTexture(); Color total = Color.black; int count = 0;
            for (int y = frame.height/4; y < frame.height*3/4; y += 13) for (int x = frame.width*3/5; x < frame.width*19/20; x += 13) { total += frame.GetPixel(x,y); count++; }
            Destroy(frame); return total/count;
        }
        void Capture(string name)
        {
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture(); int visible = 0, count = 0, pink = 0;
            for (int y = 0; y < frame.height; y += Math.Max(1, frame.height/40)) for (int x = 0; x < frame.width; x += Math.Max(1, frame.width/60))
            { Color c = frame.GetPixel(x,y); count++; if (Mathf.Max(c.r,c.g,c.b) > .035f) visible++; if (c.r>.8f && c.b>.8f && c.g<.15f) pink++; }
            Require(visible > count*.25f && pink < count*.02f, name + " contains rendered content without missing-shader pink");
            string file = name + ".png"; File.WriteAllBytes(Path.Combine(folder,file),frame.EncodeToPNG()); Destroy(frame); report.captures.Add(file);
        }
        void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); report.checks.Add(label); }
        void WriteReport() { File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtility.ToJson(report, true)); }
        void Cleanup() { if(friend!=null){friend.Dispose();friend=null;} if(remote!=null){remote.Dispose();remote=null;} if(silent!=null){silent.Stop();silent=null;} }
        void OnDestroy() { Cleanup(); }
        void Fail(string message) { failed = true; report.error = message; WriteReport(); Cleanup(); Debug.LogError("ARENA_MENUS_FAILED " + message); Application.Quit(2); }
    }
}
