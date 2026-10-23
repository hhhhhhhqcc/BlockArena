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
    // Opt-in native checks. Display requests resize the actual Unity player;
    // settings and the live TCP simulation keep their ordinary production paths.
    public sealed class ArenaSettingsProbe : MonoBehaviour
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
        string folder, savedPreferences;
        bool failed, exitOnFinish, cleanedUp, restoreDisplay;
        float startedAt;
        ArenaDisplaySettings.DisplayState originalDisplay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-settings-test") >= 0)
                new GameObject("Native settings verification").AddComponent<ArenaSettingsProbe>();
        }

        void Update()
        {
            if (!failed && startedAt > 0 && Time.realtimeSinceStartup - startedAt > 130)
                Fail("settings verification exceeded its 130 second internal deadline");
        }

        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0 && !failed)
            {
                IEnumerator run = stack.Peek(); bool moved = false; object next = null;
                try { moved = run.MoveNext(); if (moved) next = run.Current; }
                catch (Exception error) { Fail(error.ToString()); }
                if (failed) yield break;
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator) { stack.Push((IEnumerator)next); continue; }
                yield return next;
            }
        }

        IEnumerator Run()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            folder = Path.Combine(Application.dataPath, "..", "Captures", "Settings");
            for (int i = 0; i + 1 < arguments.Length; i++) if (arguments[i] == "-capture-dir") folder = arguments[i + 1];
            exitOnFinish = Array.IndexOf(arguments, "-smoke-exit") >= 0;
            Directory.CreateDirectory(folder); report.width = Screen.width; report.height = Screen.height;
            startedAt = Time.realtimeSinceStartup;
            app = ArenaApp.Instance; app.VerificationInput = true; app.VerificationPointer = new Vector2(-200, -200);
            Require(app.Display != null, "display service is installed in the native player");
            savedPreferences = PreferenceSignature();
            originalDisplay = app.Display.Capture(); restoreDisplay = true;
            yield return Settle();
            Require(!app.Display.PersistenceEnabled && !app.Display.InitializedFromPreferences,
                "verification launch never loads or writes the user's display preferences");
            Require(app.Display.CommandLineOverrides, "native command-line display arguments take precedence at startup");
            Require(Screen.width == 1440 && Screen.height == 900 && Screen.fullScreenMode == FullScreenMode.Windowed,
                "1440 by 900 windowed launch is honored by the actual Unity player");

            app.Hud.ShowSettings = true; yield return Settle();
            Require(app.Hud.SettingsTabForVerification == 0, "settings opens on the sound and assistance page");
            CheckSettings(); Capture("01-main-settings");
            app.Hud.SetSettingsTab(1); yield return Settle();
            Require(app.Hud.SettingsTabForVerification == 1, "display settings tab renders its own page");
            CheckSettings(); Capture("01-display-settings");
            app.Hud.SetSettingsTab(0); yield return Settle();
            Require(app.Hud.SettingsActionScreenRect("help")==Rect.zero, "removed help page has no settings entry");
            app.ShowBestiary = true; yield return Settle();
            app.HandleEscape();
            Require(!app.ShowBestiary && app.Hud.ShowSettings, "Escape closes the settings bestiary before the settings page");
            app.HandleEscape(); yield return Settle();
            Require(!app.Hud.ShowSettings && app.State == null && app.World.FrontendMode == "main",
                "closing main-menu settings restores its original frontend");

            yield return VerifyDisplayChanges();
            yield return VerifyQuickStart();
            yield return VerifyLiveSettings();
            yield return VerifyRitualSettings();

            Require(PreferenceSignature() == savedPreferences && app.Display.PreferenceWriteCount == 0,
                "all native display changes and restoration leave user preferences unchanged");
            Require(SameDisplay(originalDisplay, app.Display.Capture()), "native checks restore the original window dimensions and mode");
            restoreDisplay = false;
            app.VerificationState = null; app.Leave(); yield return Settle();
            Capture("10-main-restored");
            report.success = true; WriteReport();
            Debug.Log("ARENA_SETTINGS_OK " + report.checks.Count + " checks / " + report.captures.Count + " nonblack captures");
            Finish(0);
        }

        IEnumerator VerifyDisplayChanges()
        {
            app.Hud.ShowSettings = true; yield return Settle();
            app.Hud.SetSettingsTab(1); yield return Settle();
            ArenaDisplaySettings display = app.Display;
            int windowIndex = ResolutionIndex(1280, 720);
            Require(windowIndex >= 0, "supported resolution candidates include 1280 by 720");
            var seen = new HashSet<string>();
            foreach (var resolution in display.Resolutions)
            {
                Require(seen.Add(resolution.Width + "x" + resolution.Height), "resolution candidate " + resolution.Label + " is unique");
                Require(resolution.Width <= display.DesktopWidth && resolution.Height <= display.DesktopHeight ||
                    resolution.Width == Screen.width && resolution.Height == Screen.height,
                    "resolution candidate " + resolution.Label + " fits the desktop or preserves the current window");
            }
            Require(display.SetResolutionIndex(windowIndex) && display.SetMode(FullScreenMode.Windowed), "windowed resolution draft can be selected");
            Require(display.HasChanges && SameDisplay(originalDisplay, display.Capture()), "editing a draft does not resize the window");
            display.ResetDraft();
            Require(!display.HasChanges && SameDisplay(originalDisplay, display.Capture()), "canceling a draft restores the actual display selection");
            display.SetResolutionIndex(windowIndex); display.SetMode(FullScreenMode.Windowed);
            Require(display.Apply(), "windowed draft queues a real resolution change");
            Require(!display.Apply(), "duplicate application is rejected while a native resize is pending");
            yield return Await(() => !display.Applying, "windowed resolution request settles", 6);
            Require(display.LastApplySucceeded && Screen.width == 1280 && Screen.height == 720 && Screen.fullScreenMode == FullScreenMode.Windowed,
                "Screen reports a real 1280 by 720 window after applying");
            yield return Settle(); CheckSettings(); Capture("02-windowed-1280x720");

            int writes = display.PreferenceWriteCount;
            for (int i = 0; i < 4; i++) { display.ToggleMode(); display.ResetDraft(); }
            Require(!display.HasChanges && display.PreferenceWriteCount == writes && PreferenceSignature() == savedPreferences,
                "repeated mode drafts and cancellation never persist settings");
            int desktopIndex = ResolutionIndex(display.DesktopWidth, display.DesktopHeight);
            Require(desktopIndex >= 0, "native desktop resolution remains selectable for borderless fullscreen");
            display.SetResolutionIndex(desktopIndex); display.SetMode(FullScreenMode.FullScreenWindow);
            int fullscreenWidth = display.DraftWidth, fullscreenHeight = display.DraftHeight;
            Require(display.Apply(), "borderless fullscreen draft queues a native mode change");
            yield return Await(() => !display.Applying, "borderless fullscreen request settles", 6);
            Require(display.LastApplySucceeded && Screen.fullScreenMode == FullScreenMode.FullScreenWindow &&
                Screen.width == fullscreenWidth && Screen.height == fullscreenHeight,
                "Screen reports real borderless fullscreen at the desktop resolution");
            yield return Settle(); CheckSettings(); Capture("03-borderless-fullscreen");

            Require(display.Restore(originalDisplay), "original native display state can be restored without persistence");
            yield return Await(() => !display.Applying, "original window restoration settles", 6);
            Require(display.LastApplySucceeded && SameDisplay(originalDisplay, display.Capture()), "original native window and mode are restored exactly");
            Require(display.PreferenceWriteCount == 0 && PreferenceSignature() == savedPreferences,
                "native window and fullscreen application never overwrite user preferences");
            yield return Settle(); CheckSettings(); Capture("04-display-restored");
            app.HandleEscape(); yield return Settle();
        }

        IEnumerator VerifyQuickStart()
        {
            app.PortText = FreePort().ToString(); app.Host(true);
            int checkedFrames = 0; float deadline = Time.realtimeSinceStartup + 12;
            while (app.QuickStarting && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForEndOfFrame();
                if (!app.QuickStarting) break;
                if (app.Hud.RoomVisibleForVerification || app.ShowRoom || app.Hud.MenuBackgroundMode != "main" ||
                    app.World.FrontendMode != "main")
                    throw new InvalidOperationException("quick start exposed a waiting room or host backdrop on rendered frame " + checkedFrames);
                checkedFrames++;
            }
            Require(checkedFrames > 0, "quick start is checked on every rendered transition frame");
            Require(!app.QuickStarting && app.Connected && app.State != null && app.State.phase == "recruit",
                "real single-player TCP start proceeds directly to recruitment");
            int computers = 0; foreach (var player in app.State.players) if (player.bot) computers++;
            Require(app.State.players.Count == 4 && computers == 3 && !app.ShowRoom && !app.Hud.RoomVisibleForVerification,
                "single player enters a four-seat match with three computers and no room overlay");
            yield return Settle(); Capture("05-direct-single-player");
            app.Leave(); yield return Settle();
        }

        IEnumerator VerifyLiveSettings()
        {
            app.PortText = FreePort().ToString(); app.Host(true);
            yield return Await(() => !app.QuickStarting && app.Connected && app.State != null && app.State.phase == "recruit",
                "second real TCP match reaches recruitment");
            app.HandleEscape(); Require(app.ShowGameMenu, "Escape opens the live match menu");
            app.Hud.ShowSettings = true; yield return Settle();
            CheckSettings(); Capture("06-live-match-settings");
            Require(Time.timeScale == 1 && app.BattlefieldInputBlocked && app.Hud.KeyboardCaptured,
                "live settings blocks arena pointer and keyboard input without pausing time");
            int meshes = app.World.FrontendMeshObjectCount, roots = app.World.FrontendRootCount;
            Point2 stopped = app.Me.steve;
            app.MoveSteveTo(ArenaGame.LocalToWorld(app.Me.slot, 100, 290));
            GameState last = app.State; int snapshots = 0; float remaining = app.State.timeLeft;
            float until = Time.realtimeSinceStartup + 1.1f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return new WaitForEndOfFrame();
                if (!ReferenceEquals(last, app.State)) { snapshots++; last = app.State; }
                if (app.World.FrontendMode != "settings" || !app.World.FrontendVisible ||
                    app.World.FrontendMeshObjectCount != meshes || app.World.FrontendRootCount != roots || app.World.FrontendActiveSceneCount != 1)
                    throw new InvalidOperationException("a real TCP snapshot replaced or rebuilt the dedicated settings scene");
            }
            Require(snapshots >= 2 && app.State.timeLeft < remaining - .4f,
                "authoritative snapshots and recruitment countdown continue behind settings");
            Require(Distance(stopped, app.Me.steve) < 1, "live settings rejects additional commander navigation");
            Require(roots == 1 && meshes > 0 && !app.World.GameplayWorldVisible,
                "settings reuses one scene root while the gameplay world stays hidden");
            app.HandleEscape(); yield return Settle();
            Require(!app.Hud.ShowSettings && app.ShowGameMenu && !app.World.FrontendVisible && app.World.GameplayWorldVisible,
                "Escape closes live settings back to the game menu and restores gameplay rendering");
            app.HandleEscape(); Require(!app.ShowGameMenu, "a second Escape resumes the live arena");
            yield return Settle(); Capture("07-live-match-restored");
        }

        IEnumerator VerifyRitualSettings()
        {
            GameState before = RitualFixture("battle", false); Show(before); yield return Settle();
            GameState results = RitualFixture("results", true); Show(results);
            yield return Await(() => app.World.RitualActive, "synthetic lethal judgment starts through ordinary world snapshots");
            app.ShowGameMenu = true; app.Hud.ShowSettings = true; yield return Settle();
            CheckSettings(); float age = app.World.RitualAge, clock = Time.unscaledTime;
            yield return new WaitForSecondsRealtime(.85f);
            bool advancing = app.World.FrontendMode == "settings" && app.World.RitualAge >= age + .65f &&
                Mathf.Abs((app.World.RitualAge - age) - (Time.unscaledTime - clock)) < .25f;
            if (!advancing)
                throw new InvalidOperationException("lethal judgment advances on absolute time while settings covers the arena: age=" +
                    age.ToString("F3") + " -> " + app.World.RitualAge.ToString("F3") + ", clock=" + clock.ToString("F3") +
                    " -> " + Time.unscaledTime.ToString("F3") + ", frontend=" + app.World.FrontendMode + ", ritualActive=" +
                    app.World.RitualActive + ", launches=" + app.World.RitualLaunchCount + ", impacts=" + app.World.RitualImpactCount +
                    ", worldEnabled=" + app.World.enabled + ", worldActive=" + app.World.gameObject.activeInHierarchy +
                    ", fixtureRetained=" + ReferenceEquals(app.State, results) + ", fixturePhase=" + results.phase +
                    ", appPhase=" + (app.State == null ? "null" : app.State.phase));
            Require(advancing, "lethal judgment advances on absolute time while settings covers the arena");
            yield return new WaitForEndOfFrame(); Capture("08-judgment-settings");
            float ageBeforeClose = app.World.RitualAge;
            app.HandleEscape(); yield return Settle();
            Require(!app.Hud.ShowSettings && app.World.RitualAge >= ageBeforeClose && !app.World.FrontendVisible,
                "closing settings resumes the existing judgment without restarting its clock");
            app.HandleEscape();
            yield return Await(() => !app.World.RitualActive, "lethal judgment completes after closing settings", 10);
            Require(app.World.RitualLaunchCount == 1 && app.World.RitualImpactCount == 1,
                "the covered lethal judgment launches and hits exactly once");
            app.ShowGameMenu = true; app.Hud.ShowSettings = true; yield return Settle();
            app.HandleEscape(); yield return Settle();
            Require(!app.World.RitualActive && app.World.RitualLaunchCount == 1 && app.World.RitualImpactCount == 1,
                "reopening and closing settings never replays a completed judgment");
            app.ShowGameMenu = false; yield return new WaitForEndOfFrame(); Capture("09-judgment-restored");
        }

        void CheckSettings()
        {
            Require(app.Hud.ShowSettings && app.Hud.MenuBackgroundMode == "settings" && app.World.FrontendMode == "settings" && app.World.FrontendVisible,
                "settings uses its dedicated live frontend scene");
            Require(app.World.FrontendRootCount == 1 && app.World.FrontendActiveSceneCount == 1,
                "exactly one cached settings scene is active");
            Require(Fits(app.Hud.SettingsScreenRect), "settings page fits the actual native viewport");
            Require(Fits(app.Hud.SettingsTabScreenRect(0)) && Fits(app.Hud.SettingsTabScreenRect(1)), "both settings tab actions fit the viewport");
            foreach (string action in new[] { "close", "windowed", "fullscreen", "resolution-prev", "resolution-next", "apply", "music", "effects", "mute", "commander-range", "creature-range", "bestiary", "room" })
            {
                Rect rectangle = app.Hud.SettingsActionScreenRect(action);
                // The room entry is only meaningful when a network session exists.
                if (action == "room" && app.State == null && rectangle.width <= 0) continue;
                Require(Fits(rectangle), "settings control " + action + " fits the viewport");
            }
            foreach (Vector2 corner in new[] { new Vector2(1, 1), new Vector2(Screen.width - 1, 1), new Vector2(1, Screen.height - 1), new Vector2(Screen.width - 1, Screen.height - 1) })
                Require(app.Hud.Contains(corner), "settings captures the full-screen pointer at " + corner);
            Require(app.BattlefieldInputBlocked && app.Hud.KeyboardCaptured, "settings owns battlefield keyboard and pointer input");
        }

        GameState RitualFixture(string phase, bool lethal)
        {
            var state = new GameState { phase = phase, round = 6, timeLeft = 30, hostId = app.PlayerId };
            for (int slot = 0; slot < 4; slot++)
                state.players.Add(new PlayerState { id = slot == 0 ? app.PlayerId : "settings-ritual-" + slot,
                    name = "水晶演示 " + slot, slot = slot, hp = lethal && slot == 1 ? 0 : 20,
                    eliminated = lethal && slot == 1, steve = ArenaGame.LocalToWorld(slot, 0, 365) });
            return state;
        }
        void Show(GameState state) { app.VerificationState = state; app.ShowRoom = false; app.World.SetState(state, app.PlayerId); }
        IEnumerator Settle() { yield return new WaitForSecondsRealtime(.3f); yield return new WaitForEndOfFrame(); }
        IEnumerator Await(Func<bool> condition, string label, float seconds = 10)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Require(condition(), label);
        }
        int ResolutionIndex(int width, int height)
        { for (int i = 0; i < app.Display.Resolutions.Count; i++) if (app.Display.Resolutions[i].Width == width && app.Display.Resolutions[i].Height == height) return i; return -1; }
        static bool SameDisplay(ArenaDisplaySettings.DisplayState a, ArenaDisplaySettings.DisplayState b)
        { return a.Width == b.Width && a.Height == b.Height && a.Mode == b.Mode; }
        static string PreferenceSignature()
        {
            string value = "";
            foreach (string key in new[] { ArenaDisplaySettings.WidthPreference, ArenaDisplaySettings.HeightPreference, ArenaDisplaySettings.ModePreference })
                value += key + ":" + PlayerPrefs.HasKey(key) + ":" + PlayerPrefs.GetInt(key, int.MinValue) + ";";
            return value;
        }
        static int FreePort() { var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start(); int port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port; }
        static float Distance(Point2 a, Point2 b) { float x = a.x - b.x, y = a.y - b.y; return Mathf.Sqrt(x * x + y * y); }
        static bool Fits(Rect r) { return r.width > 0 && r.height > 0 && r.xMin >= -.5f && r.yMin >= -.5f && r.xMax <= Screen.width + .5f && r.yMax <= Screen.height + .5f; }
        void Capture(string name)
        {
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture(); int visible = 0, count = 0, pink = 0;
            for (int y = 0; y < frame.height; y += Math.Max(1, frame.height / 40)) for (int x = 0; x < frame.width; x += Math.Max(1, frame.width / 60))
            { Color c = frame.GetPixel(x, y); count++; if (Mathf.Max(c.r, c.g, c.b) > .035f) visible++; if (c.r > .8f && c.b > .8f && c.g < .15f) pink++; }
            Require(visible > count * .25f && pink < count * .02f, name + " contains rendered content without missing-shader pink");
            string file = name + ".png"; File.WriteAllBytes(Path.Combine(folder, file), frame.EncodeToPNG()); Destroy(frame); report.captures.Add(file);
        }
        void Require(bool value, string label) { if (!value) throw new InvalidOperationException(label); report.checks.Add(label); }
        void WriteReport() { if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtility.ToJson(report, true)); }
        void Cleanup()
        {
            if (cleanedUp) return; cleanedUp = true;
            if (app == null) return;
            if (restoreDisplay && app.Display != null)
            {
                if (!app.Display.Restore(originalDisplay)) Screen.SetResolution(originalDisplay.Width, originalDisplay.Height, originalDisplay.Mode);
                restoreDisplay = false;
            }
            app.VerificationState = null; app.Hud.ShowSettings = false; app.Leave();
            if (!exitOnFinish) { app.VerificationInput = false; app.VerificationPointer = null; }
        }
        void Finish(int code) { Cleanup(); if (exitOnFinish) Application.Quit(code); else Destroy(gameObject); }
        void Fail(string message) { if (failed) return; failed = true; report.error = message; WriteReport(); Debug.LogError("ARENA_SETTINGS_FAILED " + message); Finish(2); }
        void OnDestroy() { Cleanup(); }
    }
}
