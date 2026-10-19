using System;
using System.Collections.Generic;

namespace BlockArena.Core
{
    /// <summary>Shared presentation timing so results remain visible until queued crystal attacks finish.</summary>
    public static class CommanderRitualTimeline
    {
        public const float RiseSeconds = 1.2f;
        public const float NormalCharge = .3f, NormalFlight = .45f, NormalSpacing = .32f;
        public const float CameraInSeconds = .65f, CameraOutSeconds = .65f, WideHoldSeconds = .25f;
        public const float SoulEndSeconds = 1.5f, LethalCharge = .12f, LethalFlight = .95f;
        public const float MinimumDuration = 4.5f, NetworkMarginSeconds = .65f;

        public struct Shot
        {
            public float chargeAt, launchAt, hitAt, cameraInAt, cameraOutAt, cameraWideAt;
            public bool lethal;
        }

        public static Shot Next(bool lethal, Shot? previous)
        {
            float chargeAt = RiseSeconds;
            if (previous.HasValue)
            {
                Shot before = previous.Value;
                if (before.lethal)
                {
                    // Every subsequent shot waits for the soul, full camera return, and a wide pause.
                    chargeAt = before.cameraWideAt + WideHoldSeconds + (lethal ? CameraInSeconds : 0);
                }
                else if (lethal)
                    chargeAt = before.hitAt + WideHoldSeconds + CameraInSeconds;
                else chargeAt = before.chargeAt + NormalSpacing;
            }
            float launchAt = chargeAt + (lethal ? LethalCharge : NormalCharge);
            float hitAt = launchAt + (lethal ? LethalFlight : NormalFlight);
            return new Shot {
                lethal = lethal, chargeAt = chargeAt, launchAt = launchAt, hitAt = hitAt,
                cameraInAt = lethal ? chargeAt - CameraInSeconds : -1,
                cameraOutAt = lethal ? hitAt + SoulEndSeconds : -1,
                cameraWideAt = lethal ? hitAt + SoulEndSeconds + CameraOutSeconds : -1
            };
        }

        public static float EndAt(Shot last)
        {
            return Math.Max(MinimumDuration, last.lethal ? last.cameraWideAt + WideHoldSeconds : last.hitAt + .8f);
        }

        public static float Duration(IEnumerable<bool> attacks)
        {
            Shot? last = null;
            foreach (bool lethal in attacks) last = Next(lethal, last);
            return last.HasValue ? EndAt(last.Value) : 0;
        }

        public static float DurationFor(GameState state)
        {
            Shot? last = null;
            // Match the client's next.players iteration, rather than placement/ranking order.
            foreach (PlayerState player in state.players)
            {
                PlacementState placement = state.placements.Find(p => p.playerId == player.id);
                if (placement != null && placement.damage > 0) last = Next(player.hp == 0, last);
            }
            return last.HasValue ? EndAt(last.Value) : 0;
        }
    }
}
