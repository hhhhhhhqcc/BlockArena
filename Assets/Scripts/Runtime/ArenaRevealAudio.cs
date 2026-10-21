using System;
using BlockArena.Core;

namespace BlockArena.Runtime
{
    // Snapshot-driven roulette audio. Reconnects establish a silent baseline;
    // a delayed packet emits one current tick rather than replaying past ticks.
    public sealed class ArenaRevealAudio
    {
        string phase, stage;
        int round, lastStep;
        float snapshotAge, snapshotAt;
        bool initialized, armed;
        public int DrawTickCount { get; private set; }
        public int DrawStartCount { get; private set; }

        static float Duration(string value)
        { return value == "map" ? ArenaGame.RevealMapSeconds : ArenaGame.RevealWeatherSeconds; }

        static int DrawStep(string value, float age)
        {
            float progress = Math.Max(0, Math.Min(1, age / Duration(value)));
            double eased = 1 - Math.Pow(1 - progress, 1.6);
            return (int)Math.Floor(eased * (value == "map" ? 12 : 16));
        }

        public static int DrawIndex(string value, float age, int choices)
        { return choices <= 0 ? 0 : DrawStep(value, age) % choices; }

        public void Reset()
        {
            phase = stage = null; initialized = armed = false;
            lastStep = round = 0; DrawTickCount = DrawStartCount = 0;
        }

        public void Observe(GameState state, float now, Action<string> play)
        {
            if (state == null) { Reset(); return; }
            string nextStage = state.revealStage;
            bool changedRound = initialized && state.round != round;
            if (!initialized || changedRound)
            {
                initialized = true;
                // A fresh connection inside a roulette must not replay its opening.
                armed = state.phase == "reveal" && nextStage == "arena";
                phase = state.phase; stage = nextStage; round = state.round;
                snapshotAge = state.revealStageAge; snapshotAt = now;
                lastStep = DrawStep(stage, snapshotAge);
                return;
            }
            bool changed = phase != state.phase || stage != nextStage;
            if (state.phase == "reveal" && nextStage == "arena") armed = true;
            if (state.phase != "reveal") armed = false;
            if (changed)
            {
                lastStep = DrawStep(nextStage, state.revealStageAge);
                if (armed && state.phase == "reveal" &&
                    (nextStage == "map" || nextStage == "weather") && state.revealStageAge < .6f)
                {
                    if (play != null) play(nextStage == "map" ? "map_draw" : "weather_draw");
                    DrawStartCount++;
                }
            }
            phase = state.phase; stage = nextStage; round = state.round;
            snapshotAge = state.revealStageAge; snapshotAt = now;
        }

        public void Tick(float now, Action<string> play)
        {
            if (!initialized || !armed || phase != "reveal" || (stage != "map" && stage != "weather")) return;
            float elapsed = Math.Max(0, now - snapshotAt);
            if (elapsed > .5f) return;
            float age = Math.Min(Duration(stage) - .001f, snapshotAge + Math.Min(.25f, elapsed));
            int current = DrawStep(stage, age);
            if (current <= lastStep) return;
            lastStep = current;
            if (play != null) play("draw_tick");
            DrawTickCount++;
        }
    }
}
