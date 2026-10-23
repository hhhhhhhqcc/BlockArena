using System;

namespace BlockArena.Core
{
    /// <summary>The same sight rules drive authoritative snapshots and local fog.</summary>
    public static class BattlefieldVision
    {
        public const float BaseRange = 550;

        public static bool IsScout(BattlefieldState world, FighterState source)
        {
            if (source == null) return false;
            if (BattlefieldCatalog.Scout(source)) return true;
            if (world != null) foreach (var objective in world.objectives)
                if (objective.kind == "tower" && objective.ownerId == source.ownerId) return true;
            return false;
        }

        public static float Range(BattlefieldState world, FighterState source)
        {
            float radius = world != null && world.weather == "fog" ? 385 :
                world != null && world.weather == "sandstorm" ? 440 : BaseRange;
            return radius * (IsScout(world, source) ? 1.3f : 1);
        }

        public static bool CanSee(BattlefieldState world, FighterState source, FighterState target)
        {
            if (source == null || target == null) return false;
            if (world == null) return true;
            float dx = source.x-target.x, dy = source.y-target.y;
            float distanceSquared = dx*dx+dy*dy, radius = Range(world, source);
            if (distanceSquared > radius*radius) return false;
            if (target.hidden && !IsScout(world, source)) {
                float concealedRange = world.weather == "fog" ? 65 : 100;
                if (distanceSquared > concealedRange*concealedRange) return false;
            }
            return true;
        }

        public static float MovementSpeed(FighterState fighter)
        {
            if (fighter == null || fighter.dead || fighter.stunTime > 0 || fighter.rootTime > 0) return 0;
            return Math.Max(0, fighter.speed * fighter.slowMultiplier);
        }
    }
}
