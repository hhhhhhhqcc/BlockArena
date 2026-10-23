using System.Collections.Generic;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Environmental sound stays beneath voices and combat; neither bed borrows
    // the protected combat, countdown or commander AudioSources.
    public sealed class ArenaAmbience : MonoBehaviour
    {
        internal const float CrossfadeSeconds = 1.65f;
        sealed class Bed
        {
            public readonly AudioSource[] sources = new AudioSource[2];
            public readonly float[] weights = new float[2];
            public int active;
            public float gain;
            public float Level { get { return sources[0].volume + sources[1].volume; } }
        }
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        Bed biome, weather;
        AudioSource thunder;
        ArenaApp app;
        float thunderAt = -100, voiceDuck = 1;
        internal int LoadedClips { get { return clips.Count; } }
        internal float BiomeLevel { get { return biome == null ? 0 : biome.Level; } }
        internal float WeatherLevel { get { return weather == null ? 0 : weather.Level; } }
        internal float VoiceDuckFactor { get { return voiceDuck; } }
        internal int CrossfadeSourceCount
        {
            get
            {
                int result = 0;
                if (biome != null) foreach (AudioSource source in biome.sources) if (source.isPlaying && source.volume > .0001f) result++;
                if (weather != null) foreach (AudioSource source in weather.sources) if (source.isPlaying && source.volume > .0001f) result++;
                return result;
            }
        }
        public void Initialize(ArenaApp owner)
        {
            app = owner;
            biome = MakeBed("Biome ambience"); weather = MakeBed("Weather ambience");
            thunder = Source("Distant rolling thunder");
        }
        Bed MakeBed(string label)
        {
            var bed = new Bed();
            for (int i = 0; i < 2; i++) { bed.sources[i] = Source(label + " " + i); bed.sources[i].loop = true; }
            return bed;
        }
        AudioSource Source(string label)
        {
            var child = new GameObject(label); child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0;
            source.volume = 0; source.priority = 175; source.dopplerLevel = 0; source.reverbZoneMix = 0;
            return source;
        }
        AudioClip Clip(string id)
        {
            AudioClip value; if (clips.TryGetValue(id, out value)) return value;
            value = Resources.Load<AudioClip>("ArenaAudio/" + id); if (value != null) clips[id] = value; return value;
        }
        void SetBed(Bed bed, string id, float gain, float mix)
        {
            AudioClip clip = id == null ? null : Clip(id);
            if (clip != null && bed.sources[bed.active].clip != clip)
            {
                int next = 1 - bed.active;
                AudioSource source = bed.sources[next];
                // Reuse a still-fading matching clip on rapid back-and-forth changes.
                if (source.clip != clip)
                {
                    source.Stop(); source.volume = 0; source.clip = clip; bed.weights[next] = 0;
                }
                bed.active = next;
            }
            bed.gain = Mathf.MoveTowards(bed.gain, gain, Time.unscaledDeltaTime * .24f);
            for (int i = 0; i < 2; i++)
            {
                AudioSource source = bed.sources[i];
                float target = clip != null && i == bed.active ? 1 : 0;
                bed.weights[i] = Mathf.MoveTowards(bed.weights[i], target, Time.unscaledDeltaTime / CrossfadeSeconds);
                source.volume = bed.weights[i] * bed.gain * mix;
                if (target > 0 && !source.isPlaying) source.Play();
                else if (target == 0 && bed.weights[i] <= .0001f && source.isPlaying) source.Stop();
            }
        }
        static float WeatherGain(string weatherId)
        {
            switch (weatherId)
            {
                case "rain": return .18f;
                case "storm": return .16f;
                case "snow": return .13f;
                case "fog": return .12f;
                case "heat": return .09f;
                case "sandstorm": return .14f;
                default: return 0;
            }
        }
        bool SomeoneSpeaking()
        {
            if (app.Voice == null) return false;
            if (app.Voice.Talking) return true;
            if (app.Voice.Muted || app.State == null) return false;
            foreach (var player in app.State.players) if (app.Voice.IsSpeaking(player.id)) return true;
            return false;
        }
        void Update()
        {
            if (app == null || biome == null) return;
            var state = app.State;
            bool active = app.Connected && state != null && state.battlefield != null && app.World != null && app.World.FrontendMode == null &&
                (state.phase == "tactics" || state.phase == "battle" || state.phase == "results" || state.phase == "finished" || state.phase == "reveal" && state.revealStage != "arena" && state.revealStage != "map");
            string map = active ? "ambient_" + state.battlefield.mapId : null;
            bool climate = active && (state.phase != "reveal" || state.revealStage == "climate");
            string weatherId = climate && state.battlefield.weather != "clear" ? "weather_" + state.battlefield.weather : null;
            float duckTarget = SomeoneSpeaking() ? .26f : 1;
            voiceDuck = Mathf.MoveTowards(voiceDuck, duckTarget, Time.unscaledDeltaTime * (duckTarget < voiceDuck ? 5 : .55f));
            float level = app.Sound == null || app.Sound.Muted ? 0 : app.Sound.SfxVolume;
            // Reduce bed intensity when fighting/settling so attacks and crystal impacts remain distinct.
            float actionMix = state != null && state.phase == "battle" ? .70f : state != null && (state.phase == "results" || state.phase == "finished") ? .78f : 1;
            float mix = level * voiceDuck * actionMix;
            SetBed(biome, map, .22f, mix);
            SetBed(weather, weatherId, climate ? WeatherGain(state.battlefield.weather) : 0, mix);
            thunder.volume = level * voiceDuck * .31f;
        }
        public void Thunder()
        {
            if (app == null || app.Sound == null || app.Sound.Muted || Time.unscaledTime - thunderAt < .7f) return;
            AudioClip clip = Clip("weather_thunder"); if (clip == null) return;
            thunderAt = Time.unscaledTime; thunder.volume = app.Sound.SfxVolume * voiceDuck * .31f; thunder.PlayOneShot(clip);
        }
        void OnDisable()
        {
            if (biome != null) foreach (AudioSource source in biome.sources) source.Stop();
            if (weather != null) foreach (AudioSource source in weather.sources) source.Stop();
            if (thunder != null) thunder.Stop();
        }
    }
}
