using System;
using System.Collections;
using System.IO;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Opt-in native rendering check. It never runs during normal play.
    public sealed class ArenaPresentationProbe : MonoBehaviour
    {
        ArenaAudio sound;
        float originalMusic, originalEffects;
        bool originalMute, restoreVolumes;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-presentation-test") >= 0)
                new GameObject("Crystal and audio verification").AddComponent<ArenaPresentationProbe>();
        }

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            string folder = Path.Combine(Application.dataPath, "..", "Captures", "Presentation");
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-dir") folder = args[i + 1];
            Directory.CreateDirectory(folder);
            ArenaApp app = ArenaApp.Instance;
            app.VerificationInput = true;
            yield return new WaitForSecondsRealtime(1.5f);
            sound = app.Sound;
            if (sound == null || sound.ClipsLoaded != 27 || sound.MusicTrackCount != 3 || !sound.MusicIsPlaying)
            { Fail("music and effect resources failed to load"); yield break; }
            originalMusic = sound.MusicVolume; originalEffects = sound.SfxVolume; originalMute = sound.Muted;
            restoreVolumes = true;
            sound.SetVolumes(.35f, .6f);
            sound.SetPhase("prep");
            yield return new WaitForSecondsRealtime(.6f);
            string preparationTrack = sound.CurrentMusic;
            int soundsBefore = sound.SoundCount;
            sound.Play("purchase", Vector3.zero, .6f);
            if (sound.SoundCount <= soundsBefore) { Fail("purchase cue did not reach an audio voice"); yield break; }
            if (!VerifyCountdown()) { Fail("countdown snapshot deduplication, eligibility or reconnect baseline failed"); yield break; }
            soundsBefore = sound.SoundCount;
            sound.Play("countdown", Vector3.zero, .8f);
            sound.Play("countdown_urgent", Vector3.zero, .8f);
            if (sound.SoundCount != soundsBefore + 2) { Fail("countdown cues did not reach protected audio voices"); yield break; }
            sound.SetVolumes(0, 0);
            if (sound.MusicVolume != 0 || sound.SfxVolume != 0) { Fail("volume controls did not mute both buses"); yield break; }
            sound.SetVolumes(.35f, .6f);
            sound.SetPhase("battle");
            yield return new WaitForSecondsRealtime(1.3f);
            if (sound.CurrentMusic == preparationTrack) { Fail("battle theme was not selected"); yield break; }
            float[] mix = new float[2048];
            // Unity creates its listener analysis buffer on the first request.
            // Sample subsequent DSP blocks instead of treating that initial empty buffer as silence.
            AudioListener.GetOutputData(mix, 0);
            float mixRms = 0, sampleUntil = Time.realtimeSinceStartup + 1;
            do
            {
                yield return null;
                AudioListener.GetOutputData(mix, 0);
                float mixEnergy = 0;
                for (int i = 0; i < mix.Length; i++) mixEnergy += mix[i] * mix[i];
                mixRms = Mathf.Max(mixRms, Mathf.Sqrt(mixEnergy / mix.Length));
            } while (mixRms < .00001f && Time.realtimeSinceStartup < sampleUntil);
            if (mixRms < .00001f) { Fail("the Unity listener produced silence with music enabled"); yield break; }
            Debug.Log("ARENA_AUDIO_MIX RMS=" + mixRms + " sampleRate=" + AudioSettings.outputSampleRate + " clips=" + sound.ClipsLoaded);
            RestoreAudio();
            app.enabled = false;
            var world = app.World;
            world.Clear(); world.ResetView();
            GameState snapshot = new GameState { phase = "battle", round = 1, timeLeft = 10 };
            for (int slot = 0; slot < 4; slot++)
                snapshot.players.Add(new PlayerState { id = "ritual-" + slot, name = "水晶试演", slot = slot,
                    steve = ArenaGame.LocalToWorld(slot, 0, 385), hp = 20 });
            world.SetState(snapshot, "ritual-0");
            yield return new WaitForSecondsRealtime(.8f);
            snapshot.phase = "results"; snapshot.timeLeft = 6;
            snapshot.players[1].hp -= 1;
            snapshot.players[2].hp -= 5;
            snapshot.players[3].hp -= 10;
            world.SetState(snapshot, "ritual-0");
            if (!world.RitualActive || world.CommanderHitCount != 3)
            { Fail("damage snapshot did not schedule exactly three crystal attacks"); yield break; }
            world.SetState(snapshot, "ritual-0");
            if (world.CommanderHitCount != 3) { Fail("same snapshot rearmed the crystal"); yield break; }
            float deadline = Time.realtimeSinceStartup + 7;
            float[] shakePeaks = new float[3];
            bool rise = false, launch = false, hit = false;
            while (world.RitualActive && Time.realtimeSinceStartup < deadline)
            {
                int impact = world.RitualImpactCount;
                if (impact > 0 && impact <= 3) shakePeaks[impact - 1] = Mathf.Max(shakePeaks[impact - 1], world.CameraShakeStrength);
                if (!rise && world.RitualAge >= .7f)
                { rise = true; ScreenCapture.CaptureScreenshot(Path.Combine(folder, "01-pillar-rise.png")); }
                if (!launch && world.RitualAge >= 1.75f)
                { launch = true; ScreenCapture.CaptureScreenshot(Path.Combine(folder, "02-crystal-projectile.png")); }
                if (!hit && world.RitualImpactCount == 3)
                { hit = true; ScreenCapture.CaptureScreenshot(Path.Combine(folder, "03-heavy-impact.png")); }
                yield return null;
            }
            if (world.RitualActive || world.RitualImpactCount != 3 || world.RitualLaunchCount != 3)
            { Fail("ritual did not complete three sequential launches and hits in time"); yield break; }
            if (!(shakePeaks[0] > 0 && shakePeaks[1] > shakePeaks[0] && shakePeaks[2] > shakePeaks[1]))
            { Fail("screen shake did not increase with 1/5/10 damage: " + string.Join(",", shakePeaks)); yield break; }
            yield return new WaitForSecondsRealtime(.5f);
            if (world.CameraShakeStrength > .001f) { Fail("screen shake did not settle"); yield break; }
            world.Clear();
            world.SetState(snapshot, "ritual-0");
            if (world.RitualActive || world.CommanderHitCount != 0)
            { Fail("joining a result snapshot replayed historical damage"); yield break; }
            if (snapshot.players[1].hp != 19 || snapshot.players[2].hp != 15 || snapshot.players[3].hp != 10)
            { Fail("presentation changed authoritative health"); yield break; }
            world.Clear();
            snapshot.phase = "battle"; snapshot.round = 2;
            foreach (PlayerState player in snapshot.players) { player.hp = 4; player.eliminated = false; }
            world.SetState(snapshot, "ritual-0");
            // Preserve a real player-adjusted view, including its pan, across the cinematic.
            world.Zoom(1.4f, new Vector2(Screen.width * .58f, Screen.height * .54f));
            world.Pan(new Vector2(2.3f, -1.1f));
            yield return new WaitForSecondsRealtime(1.2f);
            float executionViewSize = world.ViewCamera.orthographicSize;
            float executionViewZoom = world.ZoomFactor;
            Vector3 executionViewPosition = world.ViewCamera.transform.position;
            Debug.Log("ARENA_EXECUTION_CAMERA baselineSize=" + executionViewSize.ToString("F3") + " playerZoom=" + executionViewZoom.ToString("F3") + " position=" + executionViewPosition);
            snapshot.phase = "finished";
            foreach (PlayerState player in snapshot.players) { player.hp = 0; player.eliminated = true; }
            world.SetState(snapshot, "ritual-0");
            if (world.PlannedExecutionCount != 4 || world.ExecutionCount != 0)
            { Fail("lethal crystal strikes were not marked before impact"); yield break; }
            deadline = Time.realtimeSinceStartup + 25;
            bool[,] executionStages = new bool[4, 5];
            float[,] executionStageTimes = new float[4, 5];
            bool[] executionCaptured = new bool[5];
            bool[] executionCloseups = new bool[4], executionWideReturns = new bool[3];
            float[] executionCaptureDelays = { .03f, .30f, .08f, .08f, .35f };
            string[] executionStageNames = { "beam", "seal", "explosion", "shatter", "soul" };
            string[] executionScreenshots = { "04-execution-beam.png", "05-execution-seal.png", "06-execution-explosion.png", "07-execution-shatter.png", "08-execution-soul.png" };
            float executionWideSize = executionViewSize / executionViewZoom;
            int lastExecutionFocus = -1;
            bool secondCloseupCaptured = false;
            while (world.RitualActive && Time.realtimeSinceStartup < deadline)
            {
                int visibleTargets = 0;
                for (int slot = 0; slot < 4; slot++)
                {
                    bool targetVisible = false;
                    for (int stage = 0; stage < 5; stage++)
                    {
                        if (!ExecutionStageVisible(world, slot, stage)) continue;
                        targetVisible = true;
                        if (executionStages[slot, stage]) continue;
                        if (stage > 0 && !executionStages[slot, stage - 1])
                        { Fail("slot " + slot + " showed " + executionStageNames[stage] + " before " + executionStageNames[stage - 1]); yield break; }
                        if (stage == 0 && slot > 0 && (!executionStages[slot - 1, 4] ||
                            world.ExecutionSoulVisible(slot - 1) || !executionWideReturns[slot - 1]))
                        { Fail("slot " + slot + " connected its beam before the previous soul finished and the camera returned to the full arena"); yield break; }
                        executionStages[slot, stage] = true;
                        executionStageTimes[slot, stage] = world.RitualAge;
                        Debug.Log("ARENA_EXECUTION_STAGE slot=" + slot + " stage=" + executionStageNames[stage] + " observedAt=" + world.RitualAge.ToString("F3"));
                    }
                    if (targetVisible) visibleTargets++;
                }
                if (visibleTargets > 1)
                { Fail("serial execution displayed attack or death stages for multiple players at the same time"); yield break; }
                for (int slot = 0; slot < 3; slot++)
                {
                    if (executionWideReturns[slot] || !executionStages[slot, 4] || world.ExecutionSoulVisible(slot) ||
                        executionStages[slot + 1, 0] || world.ExecutionCameraWeight > .01f ||
                        world.ViewCamera.orthographicSize < executionWideSize * .94f) continue;
                    executionWideReturns[slot] = true;
                    Debug.Log("ARENA_EXECUTION_WIDE afterSlot=" + slot + " observedAt=" + world.RitualAge.ToString("F3") + " orthoSize=" + world.ViewCamera.orthographicSize.ToString("F3") + " defaultWideSize=" + executionWideSize.ToString("F3"));
                    if (slot == 0) ScreenCapture.CaptureScreenshot(Path.Combine(folder, "09-execution-wide-return.png"));
                }
                if (world.ExecutionCameraWeight > .05f)
                {
                    int focus = world.ExecutionFocusSlot;
                    if (focus < 0 || focus > 3 || (focus != lastExecutionFocus && focus != lastExecutionFocus + 1))
                    { Fail("serial execution camera did not visit players in attack order"); yield break; }
                    if (focus != lastExecutionFocus)
                    {
                        if (focus > 0 && !executionWideReturns[focus - 1])
                        { Fail("camera began the next player's closeup before returning to the full arena"); yield break; }
                        lastExecutionFocus = focus;
                        Debug.Log("ARENA_EXECUTION_FOCUS slot=" + focus + " observedAt=" + world.RitualAge.ToString("F3"));
                    }
                }
                // Assert transitions immediately, then let each effect grow into a readable screenshot.
                // Remember observed transitions so a following stage cannot cancel a pending capture.
                for (int stage = 0; stage < 5; stage++)
                {
                    if (executionCaptured[stage] || !executionStages[0, stage] ||
                        world.RitualAge - executionStageTimes[0, stage] < executionCaptureDelays[stage]) continue;
                    executionCaptured[stage] = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, executionScreenshots[stage]));
                    Debug.Log("ARENA_EXECUTION_CAPTURE stage=" + executionStageNames[stage] + " capturedAt=" + world.RitualAge.ToString("F3") + " orthoSize=" + world.ViewCamera.orthographicSize.ToString("F3"));
                }
                if (!secondCloseupCaptured && executionStages[1, 2] && world.RitualAge - executionStageTimes[1, 2] >= .08f)
                {
                    secondCloseupCaptured = true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder, "10-execution-second-closeup.png"));
                }
                for (int slot = 0; slot < 4; slot++)
                {
                    if (executionCloseups[slot] || !world.ExecutionExploding(slot)) continue;
                    executionCloseups[slot] = true;
                    Debug.Log("ARENA_EXECUTION_CAMERA slot=" + slot + " burstSize=" + world.ViewCamera.orthographicSize.ToString("F3") + " baselineSize=" + executionViewSize.ToString("F3") + " movement=" + Vector3.Distance(world.ViewCamera.transform.position, executionViewPosition).ToString("F3"));
                    if (world.ExecutionFocusSlot != slot || world.ViewCamera.orthographicSize < executionWideSize * .45f ||
                        world.ViewCamera.orthographicSize > executionWideSize * .88f ||
                        Vector3.Distance(world.ViewCamera.transform.position, executionViewPosition) < 2)
                    { Fail("slot " + slot + " execution camera did not provide a moderate target closeup within 45-88% of the full arena size"); yield break; }
                    string frameDetails;
                    if (!ExecutionBoundsVisible(world.ViewCamera, snapshot.players[slot].steve, out frameDetails))
                    { Fail("slot " + slot + " execution camera clipped the crystal platform or target effects: " + frameDetails); yield break; }
                    Debug.Log("ARENA_EXECUTION_BOUNDS slot=" + slot + " " + frameDetails);
                }
                yield return null;
            }
            if (world.RitualActive || world.ExecutionActive || world.ExecutionCount != 4 || world.NormalCommanderImpactCount != 0)
            { Fail("four lethal strikes did not complete their execution animation within the ritual"); yield break; }
            for (int slot = 0; slot < 4; slot++)
                for (int stage = 0; stage < 5; stage++)
                    if (!executionStages[slot, stage])
                    { Fail("slot " + slot + " never presented execution " + executionStageNames[stage]); yield break; }
            for (int stage = 0; stage < 5; stage++)
                if (!executionCaptured[stage])
                { Fail("execution screenshot was never captured for " + executionStageNames[stage]); yield break; }
            for (int slot = 0; slot < 4; slot++)
                if (!executionCloseups[slot]) { Fail("slot " + slot + " execution closeup was never observed"); yield break; }
            for (int slot = 0; slot < 3; slot++)
                if (!executionWideReturns[slot]) { Fail("full arena was not shown between executions " + slot + " and " + (slot + 1)); yield break; }
            if (!secondCloseupCaptured || lastExecutionFocus != 3)
            { Fail("serial execution did not present every player's closeup and the second closeup screenshot"); yield break; }
            foreach (var commander in world.GetComponentsInChildren<BlockArena.Presentation.VoxelCreature>())
                if (commander.IsExecuting) { Fail("commander execution survived the ritual deadline"); yield break; }
            world.SetState(snapshot, "ritual-0");
            if (world.ExecutionCount != 4 || world.RitualActive) { Fail("same result replayed executions"); yield break; }
            yield return new WaitForSecondsRealtime(1.2f);
            if (world.CameraShakeStrength > .001f) { Fail("execution camera shake did not settle"); yield break; }
            Debug.Log("ARENA_EXECUTION_CAMERA restoredSize=" + world.ViewCamera.orthographicSize.ToString("F3") + " baselineSize=" + executionViewSize.ToString("F3") + " positionError=" + Vector3.Distance(world.ViewCamera.transform.position, executionViewPosition).ToString("F3") + " playerZoom=" + world.ZoomFactor.ToString("F3"));
            if (!CameraRestored(world, executionViewSize, executionViewPosition, executionViewZoom))
            { Fail("execution did not restore the player's previous zoom and panned camera"); yield break; }
            ScreenCapture.CaptureScreenshot(Path.Combine(folder, "11-execution-camera-restored.png"));
            world.Clear(); world.SetState(snapshot, "ritual-0");
            if (world.RitualActive || world.ExecutionActive || world.ExecutionCount != 0 || HasExecutionStage(world))
            { Fail("reconnected result replayed an execution"); yield break; }
            foreach (PlayerState player in snapshot.players) if (player.hp != 0)
            { Fail("execution changed authoritative health"); yield break; }
            world.Clear(); snapshot.phase = "battle"; snapshot.round = 3;
            foreach (PlayerState player in snapshot.players) { player.hp = 4; player.eliminated = false; }
            world.SetState(snapshot, "ritual-0");
            // One lethal attack keeps the stall check short while exercising all overdue cues.
            snapshot.phase = "results";
            snapshot.players[0].hp = 0; snapshot.players[0].eliminated = true;
            world.SetState(snapshot, "ritual-0");
            if (world.PlannedExecutionCount != 1)
            { Fail("single-target stalled execution scheduled extra attacks"); yield break; }
            int stalledSoundCount = sound.SoundCount;
            world.enabled = false;
            yield return new WaitForSecondsRealtime(3.6f); // Simulate a stalled presentation frame.
            world.enabled = true;
            yield return null;
            yield return null;
            if (sound.SoundCount != stalledSoundCount)
            { Fail("resuming a stalled execution replayed overdue charge, launch or hit audio"); yield break; }
            deadline = Time.realtimeSinceStartup + 3;
            while (world.RitualActive && Time.realtimeSinceStartup < deadline) yield return null;
            if (world.RitualActive || world.ExecutionActive || world.ExecutionCount != 1)
            { Fail("late frame failed to finish the scheduled execution"); yield break; }
            foreach (var commander in world.GetComponentsInChildren<BlockArena.Presentation.VoxelCreature>())
                if (commander.IsExecuting) { Fail("late execution restarted a full animation beyond its planned deadline"); yield break; }
            world.Clear();
            // A remote lethal target must still get a cinematic when the local commander survives.
            snapshot.phase = "battle"; snapshot.round = 4;
            foreach (PlayerState player in snapshot.players) { player.hp = 4; player.eliminated = false; }
            world.SetState(snapshot, "ritual-0");
            yield return new WaitForSecondsRealtime(.8f);
            snapshot.phase = "results"; snapshot.players[2].hp = 0; snapshot.players[2].eliminated = true;
            world.SetState(snapshot, "ritual-0");
            deadline = Time.realtimeSinceStartup + 3;
            while (!world.ExecutionSealed(2) && world.RitualActive && Time.realtimeSinceStartup < deadline) yield return null;
            if (!world.ExecutionSealed(2) || world.ExecutionFocusSlot != 2)
            { Fail("remote-only execution did not focus its actual target"); yield break; }
            world.Clear();
            if (world.RitualActive || world.ExecutionActive || HasExecutionStage(world) || world.ExecutionFocusSlot != -1)
            { Fail("clearing a sealed execution left active geometry or cinematic focus"); yield break; }
            yield return new WaitForSecondsRealtime(1.2f);
            if (!CameraRestored(world, executionViewSize, executionViewPosition, executionViewZoom))
            { Fail("interrupting an execution did not restore the player's previous camera"); yield break; }
            var highlightState = new GameState { phase = "battle", round = 5 };
            var owner = new PlayerState { id = "summoner", slot = 0, steve = new Point2(0,390) };
            highlightState.players.Add(owner);
            var caster = new UnitState { id = "shared-origin", kind = "evoker" }; owner.units.Add(caster);
            var evoker = ArenaGame.PreviewFighter(owner, caster); evoker.id = "caster";
            var vex = ArenaGame.PreviewFighter(owner, new UnitState { id = "shared-origin", kind = "vex" }); vex.id = "summon"; vex.summoned = true;
            highlightState.fighters.Add(evoker); highlightState.fighters.Add(vex);
            world.SetState(highlightState, owner.id);
            world.SetSkillHighlight("illager_banner", owner.id);
            if (world.SkillHighlightCount != 2) { Fail("illager skill did not include both catalog-tagged illager species"); yield break; }
            world.SetSkillHighlight("aerial_hunt", owner.id);
            if (world.SkillHighlightCount != 1) { Fail("flying skill did not highlight the actual summoned species"); yield break; }
            world.SetSkillHighlight(null, owner.id);
            if (world.SkillHighlightCount != 0) { Fail("summon skill highlight did not clear"); yield break; }
            Debug.Log("ARENA_PRESENTATION_OK original music / 27 clips / protected countdown cues / countdown deduplication and reconnect baseline / mute and restore / phase theme change / rising obsidian and crystal / sequential launch and hit / damage-scaled shake " +
                string.Join(",", shakePeaks) + " / four serial beam-seal-explosion-shatter-soul executions / target-by-target cinematic focus / full arena between every execution / actual camera closeups and player view restoration / interrupted execution cleanup / execution duplicate suppression and reconnect baseline / late frame execution deadlines and skipped overdue audio / summon species skill highlight / shake settles / authoritative health unchanged");
            Application.Quit(0);
        }

        void RestoreAudio()
        {
            if (restoreVolumes && sound != null) sound.SetVolumes(originalMusic, originalEffects, originalMute);
            restoreVolumes = false;
        }
        void OnDestroy() { RestoreAudio(); }
        static bool ExecutionStageVisible(BlockArena.Presentation.ArenaWorld world, int slot, int stage)
        {
            switch (stage)
            {
                case 0: return world.ExecutionBeamConnected(slot);
                case 1: return world.ExecutionSealed(slot);
                case 2: return world.ExecutionExploding(slot);
                case 3: return world.ExecutionShattered(slot);
                case 4: return world.ExecutionSoulVisible(slot);
                default: return false;
            }
        }
        static bool HasExecutionStage(BlockArena.Presentation.ArenaWorld world)
        {
            for (int slot = 0; slot < 4; slot++)
                for (int stage = 0; stage < 5; stage++)
                    if (ExecutionStageVisible(world, slot, stage)) return true;
            return false;
        }
        static bool CameraRestored(BlockArena.Presentation.ArenaWorld world, float size, Vector3 position, float zoom)
        {
            return Mathf.Abs(world.ZoomFactor - zoom) < .0001f &&
                Mathf.Abs(world.ViewCamera.orthographicSize - size) < Mathf.Max(.2f, size * .02f) &&
                Vector3.Distance(world.ViewCamera.transform.position, position) < .45f &&
                world.ExecutionCameraWeight < .001f;
        }
        static bool ExecutionBoundsVisible(Camera camera, Point2 commander, out string details)
        {
            // Independent world-space limits cover the whole firing platform and the target's effects.
            // Include the ground sigil, high crystal corners, expanding shockwave and full soul ascent.
            Vector3 target = new Vector3(commander.x / 25f, .06f, commander.y / 25f);
            Vector3[] points = new Vector3[13];
            int point = 0;
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    points[point++] = new Vector3(x * 2.9f, .08f, z * 2.9f);
                    points[point++] = new Vector3(x * 1.5f, 6.9f, z * 1.5f);
                    points[point++] = target + new Vector3(x * 2.7f, 0, z * 2.7f);
                }
            points[point] = target + Vector3.up * 4.5f;
            Vector2 minimum = Vector2.one, maximum = Vector2.zero;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 viewport = camera.WorldToViewportPoint(points[i]);
                if (viewport.z <= 0 || viewport.x < .025f || viewport.x > .975f || viewport.y < .025f || viewport.y > .975f)
                {
                    details = "worldPoint=" + points[i].ToString("F3") + " viewport=" + viewport.ToString("F3");
                    return false;
                }
                minimum = Vector2.Min(minimum, new Vector2(viewport.x, viewport.y));
                maximum = Vector2.Max(maximum, new Vector2(viewport.x, viewport.y));
            }
            details = "viewportMin=" + minimum.ToString("F3") + " viewportMax=" + maximum.ToString("F3") + " points=" + points.Length;
            return true;
        }
        static bool VerifyCountdown()
        {
            var countdown = new ArenaCountdown();
            var state = new GameState { phase = "recruit", round = 1, timeLeft = 25 };
            var player = new PlayerState { id = "timer", recruitPending = true }; state.players.Add(player);
            if (countdown.Observe(state, "timer") != 0) return false;
            state.timeLeft = 4.9f;
            if (countdown.Observe(state, "timer") != 5 || countdown.Observe(state, "timer") != 0) return false;
            state.timeLeft = 3.9f;
            if (countdown.Observe(state, "timer") != 4) return false;
            state.timeLeft = 4.9f;
            if (countdown.Observe(state, "timer") != 0) return false;
            state.timeLeft = 3.9f;
            if (countdown.Observe(state, "timer") != 0) return false;
            state.timeLeft = 1.8f; // A delayed packet skips warnings rather than queueing a burst.
            if (countdown.Observe(state, "timer") != 2 || countdown.Observe(state, "timer") != 0) return false;
            state.timeLeft = .8f;
            if (countdown.Observe(state, "timer") != 1) return false;
            state.timeLeft = 0;
            if (countdown.Observe(state, "timer") != 0) return false;
            countdown.Reset(); state.timeLeft = 2.5f;
            if (countdown.Observe(state, "timer") != 0) return false;
            state.timeLeft = 1.5f;
            if (countdown.Observe(state, "timer") != 2) return false;
            player.recruitPending = false; state.timeLeft = .5f;
            if (countdown.Observe(state, "timer") != 0) return false;
            state.phase = "prep"; state.timeLeft = 8;
            if (countdown.Observe(state, "timer") != 0) return false;
            state.timeLeft = .9f;
            if (countdown.Observe(state, "timer") != 1) return false;
            player.eliminated = true; state.phase = "battle"; state.timeLeft = 4;
            if (countdown.Observe(state, "timer") != 0) return false;
            state.phase = "results"; player.eliminated = false; state.timeLeft = 2;
            return countdown.Observe(state, "timer") == 0;
        }
        void Fail(string message) { RestoreAudio(); Debug.LogError("ARENA_PRESENTATION_FAILED " + message); Application.Quit(2); }
    }
}
