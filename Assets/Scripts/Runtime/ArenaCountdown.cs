using System;
using BlockArena.Core;

namespace BlockArena.Runtime
{
    // Consume authoritative snapshots: late packets never replay missed warning sounds.
    internal sealed class ArenaCountdown
    {
        string phase;
        int round, previousSecond, announced;

        internal void Reset() { phase = null; announced = 0; }

        internal int Observe(GameState state, string playerId)
        {
            PlayerState player = state == null ? null : state.players.Find(p => p.id == playerId);
            bool eligible = player != null && !player.eliminated &&
                (state.phase == "battle" || state.phase == "prep" || state.phase == "tactics" || state.phase == "recruit");
            if (!eligible || float.IsNaN(state.timeLeft) || float.IsInfinity(state.timeLeft)) { Reset(); return 0; }
            int second = (int)Math.Ceiling(Math.Max(0, state.timeLeft));
            string timerPhase = state.phase == "recruit" ? "prep" : state.phase;
            if (phase != timerPhase || round != state.round)
            {
                // Joining/reconnecting establishes a baseline without a historical warning.
                phase = timerPhase; round = state.round; previousSecond = second; announced = 0;
                return 0;
            }
            int before = previousSecond;
            previousSecond = second;
            // Cancelling early readiness can restore the full preparation timer.
            if (second > 5) { announced = 0; return 0; }
            if (second < 1 || second >= before || (announced & (1 << second)) != 0) return 0;
            announced |= 1 << second;
            return second;
        }
    }
}
