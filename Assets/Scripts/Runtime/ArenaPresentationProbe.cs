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
            if (sound == null || sound.ClipsLoaded != 23 || sound.MusicTrackCount != 3 || !sound.MusicIsPlaying)
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
            Debug.Log("ARENA_PRESENTATION_OK original music / audio voices / mute and restore / phase theme change / rising obsidian and crystal / sequential launch and hit / damage-scaled shake " +
                string.Join(",", shakePeaks) + " / shake settles / duplicate snapshot suppression / reconnect baseline / authoritative health unchanged");
            Application.Quit(0);
        }

        void RestoreAudio()
        {
            if (restoreVolumes && sound != null) sound.SetVolumes(originalMusic, originalEffects, originalMute);
            restoreVolumes = false;
        }
        void OnDestroy() { RestoreAudio(); }
        void Fail(string message) { RestoreAudio(); Debug.LogError("ARENA_PRESENTATION_FAILED " + message); Application.Quit(2); }
    }
}
