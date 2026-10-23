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
    // Native rendered verification uses real authenticated TCP peers and synthetic
    // PCM only. It never opens a physical microphone or writes user preferences.
    public sealed class ArenaCommunicationProbe : MonoBehaviour
    {
        [Serializable] sealed class Report
        {
            public bool success;
            public int width, height, receivedFrames, playedNonzeroSamples;
            public string error;
            public List<string> checks = new List<string>();
            public List<string> captures = new List<string>();
        }
        readonly Report report = new Report();
        readonly List<ArenaClient> peers = new List<ArenaClient>();
        ArenaApp app;
        string folder;
        bool failed, preferencesSaved, savedMute;
        float savedVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-communication-test") >= 0)
                new GameObject("Native commander communication verification").AddComponent<ArenaCommunicationProbe>();
        }
        IEnumerator Start()
        {
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                IEnumerator current = stack.Peek(); bool moved = false; object next = null;
                try { moved = current.MoveNext(); if (moved) next = current.Current; }
                catch (Exception exception) { Fail(exception.ToString()); }
                if (failed) yield break;
                if (!moved) { stack.Pop(); continue; }
                if (next is IEnumerator) { stack.Push((IEnumerator)next); continue; }
                yield return next;
            }
        }
        IEnumerator Run()
        {
            folder = Path.Combine(Application.dataPath, "..", "Captures", "Communication");
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-dir") folder = args[i + 1];
            Directory.CreateDirectory(folder); report.width = Screen.width; report.height = Screen.height;
            app = ArenaApp.Instance; app.VerificationInput = true; app.VerificationPointer = new Vector2(-200, -200);
            app.PortText = FreePort().ToString(); app.Host(false);
            yield return Until(() => app.Connected, "native host establishes an authenticated TCP connection");
            app.ShowRoom = false;
            Require(app.Voice != null && !app.Voice.OpenMic && !app.Voice.Capturing, "microphone is closed by default before any voice action");
            savedMute = app.Voice.Muted; savedVolume = app.Voice.Volume; preferencesSaved = true;
            app.Voice.SetMuted(false); app.Voice.SetVolume(.35f);
            string[] names = { "霜林指挥官", "高台守望者", "腐沼探路者" };
            foreach (string name in names)
            {
                var peer = new ArenaClient(new UnityWireCodec()); peers.Add(peer);
                peer.Connect("127.0.0.1", int.Parse(app.PortText), name);
            }
            yield return Until(() => peers.TrueForAll(peer => peer.Connected) && app.State.players.Count == 4,
                "three additional real TCP clients fill all four commander seats");
            foreach (ArenaClient peer in peers) peer.SendChat("大家好，准备争夺高地！中文消息 <测试>");
            yield return Until(() => app.ChatMessages.Count == 3, "Unity HUD receives three authenticated Chinese chat messages");
            foreach (ChatMessage message in app.ChatMessages)
                Require(message.text.Contains("中文消息") && !message.text.Contains("<") && !message.text.Contains(">"),
                    "Chinese text survives wire serialization and remains literal in the HUD");
            int imeFrame = Time.frameCount;
            Require(!app.Hud.AcceptCommunicationEnter(true, "zhongwen", imeFrame)
                && !app.Hud.AcceptCommunicationEnter(true, "", imeFrame + 1)
                && app.Hud.AcceptCommunicationEnter(true, "", imeFrame + 2),
                "Chinese IME candidate confirmation and its commit frame cannot send the draft");

            app.Hud.ToggleCommunicationPanel();
            Require(app.Hud.CommunicationInputActive,"clicking chat immediately starts typing so the next Enter sends");
            yield return Settle();
            app.Hud.SetCommunicationDraftForVerification("收到，右侧包抄。四位指挥官集结！");
            Handle(true, false, false, Vector2.zero);
            yield return Until(() => app.ChatMessages.Count == 4 && peers.TrueForAll(peer => peer.ChatHistory.Count == 4),
                "native Enter handler sends its Chinese draft to the other three real clients");
            Require(!app.Hud.CommunicationInputActive, "sending Enter releases keyboard capture");
            Handle(true, false, false, Vector2.zero);
            yield return Settle();
            Require(app.Hud.CommunicationExpandedForVerification && app.Hud.CommunicationDrawnSpeakers == 4,
                "rendered chat panel includes four commander speaking indicators");
            Require(Fits(app.Hud.CommunicationPanelScreenRect) && Fits(app.Hud.CommunicationControlsScreenRect),
                "chat history and voice controls fit the native viewport");

            int before = app.Voice.ReceivedFrames;
            yield return StreamTone(.7f);
            yield return Until(() => app.Voice.ReceivedFrames >= before + 9, "native Unity receiver decodes synthetic PCM16 from all remote speakers");
            yield return Until(() => app.Voice.PlayedNonzeroSamples > 100, "Unity streaming AudioSource callback consumes nonzero decoded PCM samples");
            Require(app.Voice.StreamingSpeakerCount == 3, "native audio creates exactly three remote streaming sources");
            Require(app.Ambience != null && app.Ambience.VoiceDuckFactor < .4f,
                "real received commander PCM automatically ducks environmental audio for speech clarity");
            yield return StreamTone(.12f);
            yield return new WaitForEndOfFrame();
            foreach (ArenaClient peer in peers) Require(app.Voice.IsSpeaking(peer.PlayerId), "speaking indicator follows recent remote PCM frames");
            Capture("01-four-commanders-chat-and-voice");
            yield return new WaitForSecondsRealtime(1.9f);
            Require(app.Ambience.VoiceDuckFactor > .95f,
                "environmental audio returns smoothly after real remote speech ends");

            app.Voice.SetMuted(true);
            Require(app.Voice.Muted, "voice mute takes effect independently of the game audio bus");
            yield return StreamTone(.3f);
            int silentSamples = app.Voice.PlayedNonzeroSamples;
            yield return StreamTone(.2f);
            Require(app.Voice.PlayedNonzeroSamples == silentSamples, "muted remote voice does not enqueue audible PCM into Unity streams");
            app.Voice.SetMuted(false);
            yield return StreamTone(.3f);
            yield return Until(() => app.Voice.PlayedNonzeroSamples > silentSamples, "unmuting resumes native PCM playback without reconnecting");

            var tactics = TacticalFixture(); app.VerificationState = tactics; app.ShowRoom = false;
            app.World.SetState(tactics, app.PlayerId);
            Handle(false, true, false, Vector2.zero);
            yield return Settle();
            app.Tactics.SetSelectionForVerification(new[] { tactics.players[0].units[0].id });
            Point2 commanderAt = new Point2(app.Me.steve.x, app.Me.steve.y);
            string selected = app.Tactics.SelectedUnitIds[0];
            Vector3 pickedAt;
            Require(app.World.TryGetUnitSelectionPoint(tactics.players[0].units[1].id, out pickedAt), "tactical fixture exposes another selectable creature");
            Vector3 screen = app.World.ViewCamera.WorldToScreenPoint(pickedAt);
            Vector2 outside = new Vector2(screen.x, screen.y);
            Require(!app.Hud.CommunicationPanelScreenRect.Contains(outside), "outside-click fixture lands on a creature beyond the chat panel");
            Handle(true, false, false, outside);
            Require(app.Hud.CommunicationInputActive && app.Hud.KeyboardCaptured && app.BattlefieldInputBlocked,
                "opening chat captures keyboard and battlefield controls");
            bool blocked = app.Tactics.ProcessTacticalPointer(outside, true, true, false, false, false);
            Require(!blocked && app.Tactics.SelectedCount == 1 && app.Tactics.SelectedUnitIds[0] == selected,
                "typing cannot start a creature drag or change the selected tactical group");
            Handle(false, false, true, outside);
            Require(!app.Hud.CommunicationInputActive && app.BattlefieldInputBlocked,
                "closing chat by outside click protects the same frame after keyboard capture ends");
            blocked = app.Tactics.ProcessTacticalPointer(outside, true, true, false, false, true);
            Require(!blocked && app.Tactics.SelectedCount == 1 && app.Tactics.SelectedUnitIds[0] == selected && app.Tactics.RouteNodeCount == 0,
                "outside-click close cannot pass through into a creature selection or route node");
            Require(Mathf.Abs(app.Me.steve.x - commanderAt.x) < .01f && Mathf.Abs(app.Me.steve.y - commanderAt.y) < .01f,
                "chat input transitions leave commander position unchanged");
            yield return null;
            Require(!app.BattlefieldInputBlocked, "one-frame close protection clears naturally on the next frame");
            Handle(true, false, false, outside); Handle(false, true, false, outside);
            Require(!app.Hud.CommunicationInputActive && !app.Hud.CommunicationExpandedForVerification && !app.ShowGameMenu,
                "chat Escape closes the input and panel before the game menu");
            Handle(true, false, false, outside);
            yield return Settle(); Capture("02-chat-over-tactical-battlefield");

            report.receivedFrames = app.Voice.ReceivedFrames; report.playedNonzeroSamples = app.Voice.PlayedNonzeroSamples;
            RestoreVoicePreferences();
            app.Voice.SetOpenMic(true); // VerificationInput suppresses physical microphone capture.
            app.VerificationState = null; app.Leave();
            Require(!app.Connected && app.ChatMessages.Count == 0 && !app.Hud.CommunicationInputActive && !app.Hud.CommunicationExpandedForVerification,
                "leaving clears the native room chat and keyboard focus");
            Require(!app.Voice.OpenMic && !app.Voice.Capturing && app.Voice.StreamingSpeakerCount == 0,
                "leaving disables open mic and disposes all remote audio streams");
            CleanupPeers(); report.success = true; WriteReport();
            Debug.Log("ARENA_COMMUNICATION_OK " + report.checks.Count + " checks / " + report.captures.Count + " nonblack captures / " + report.playedNonzeroSamples + " streamed samples");
            Application.Quit(0);
        }
        bool Handle(bool enter, bool escape, bool pressed, Vector2 point)
        {
            bool consumed = app.Hud.ProcessCommunicationInput(enter, escape, pressed, point);
            app.RecordCommunicationInput(consumed); return consumed;
        }
        GameState TacticalFixture()
        {
            var result = new GameState { phase = "tactics", round = 2, timeLeft = 120, hostId = app.PlayerId };
            result.battlefield = BattlefieldCatalog.CreateExpanded("courtyards", "clear", 53); result.battlefield.revealAge = 6;
            foreach (PlayerState player in app.State.players)
            {
                var copy = new PlayerState { id = player.id, name = player.name, slot = player.slot, hp = 20, connected = true,
                    deployed = true, deploymentSide = player.slot, deploymentOffset = new Point2() };
                copy.steve = ArenaGame.FormationToWorld(copy, 0, 360);
                for (int i = 0; i < 2; i++)
                {
                    Point2 at = ArenaGame.FormationToWorld(copy, i == 0 ? -60 : 60, 270);
                    copy.units.Add(new UnitState { id = "communication-tactics-" + player.slot + "-" + i, kind = "skeleton", x = at.x, y = at.y });
                }
                result.players.Add(copy);
            }
            return result;
        }
        IEnumerator StreamTone(float duration)
        {
            float stop = Time.realtimeSinceStartup + duration, next = 0;
            var pcm = new byte[WireProtocol.VoiceBytesPerFrame];
            for (int i = 0; i < WireProtocol.VoiceSamplesPerFrame; i++)
            {
                short sample = (short)(Math.Sin(i * 2 * Math.PI * 440 / WireProtocol.VoiceSampleRate) * 1800);
                pcm[i * 2] = (byte)sample; pcm[i * 2 + 1] = (byte)(sample >> 8);
            }
            while (Time.realtimeSinceStartup < stop)
            {
                PumpPeers();
                if (Time.realtimeSinceStartup >= next) { foreach (ArenaClient peer in peers) peer.SendVoice(pcm); next = Time.realtimeSinceStartup + .055f; }
                yield return null;
            }
        }
        IEnumerator Until(Func<bool> test, string description)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!test() && Time.realtimeSinceStartup < deadline) { PumpPeers(); yield return null; }
            Require(test(), description);
        }
        void PumpPeers() { foreach (ArenaClient peer in peers) peer.Pump(); }
        IEnumerator Settle() { yield return new WaitForSecondsRealtime(.25f); PumpPeers(); yield return new WaitForEndOfFrame(); }
        static int FreePort() { var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); int port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port; }
        static bool Fits(Rect r) { return r.width > 0 && r.height > 0 && r.xMin >= 0 && r.yMin >= 0 && r.xMax <= Screen.width + .5f && r.yMax <= Screen.height + .5f; }
        void Capture(string name)
        {
            var frame = ScreenCapture.CaptureScreenshotAsTexture(); int count = 0, visible = 0;
            for (int y = 0; y < frame.height; y += Mathf.Max(1, frame.height / 40)) for (int x = 0; x < frame.width; x += Mathf.Max(1, frame.width / 60))
            { Color color = frame.GetPixel(x, y); count++; if (Mathf.Max(color.r, color.g, color.b) > .035f) visible++; }
            Require(visible > count * .25f, name + " contains a rendered scene");
            string file = name + ".png"; File.WriteAllBytes(Path.Combine(folder, file), frame.EncodeToPNG()); Destroy(frame); report.captures.Add(file);
        }
        void RestoreVoicePreferences() { if (preferencesSaved && app != null && app.Voice != null) { app.Voice.SetMuted(savedMute); app.Voice.SetVolume(savedVolume); preferencesSaved = false; } }
        void CleanupPeers() { foreach (ArenaClient peer in peers) peer.Dispose(); peers.Clear(); }
        void Require(bool value, string description) { if (!value) throw new InvalidOperationException(description); report.checks.Add(description); }
        void WriteReport() { if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtility.ToJson(report, true)); }
        void Fail(string message)
        {
            failed = true; report.error = message; RestoreVoicePreferences(); CleanupPeers(); WriteReport();
            if (app != null) { app.VerificationState = null; app.Leave(); }
            Debug.LogError("ARENA_COMMUNICATION_FAILED " + message); Application.Quit(2);
        }
    }
}
