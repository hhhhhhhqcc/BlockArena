using System;
using System.Linq;

namespace BlockArena.Core
{
    public sealed partial class ArenaGame
    {
        void AnnounceRoundEvent()
        {
            int roll = random.Next(100);
            string kind = roll < 50 ? "none" : roll < 80 ? "boss" : "gold";
            State.roundEvent = new ArenaEventState { id = Id("event"), kind = kind, round = State.round,
                reward = kind == "boss" ? 4 : kind == "gold" ? 3 : 0, x = 0, y = 0 };
        }

        void SpawnRoundEvent()
        {
            ArenaEventState roundEvent = State.roundEvent;
            if (roundEvent == null || roundEvent.kind == "none") return;
            if (roundEvent.kind != "boss" && roundEvent.kind != "gold") return;
            bool boss = roundEvent.kind == "boss";
            int round = Math.Max(1, Math.Min(20, State.round));
            string id = Id("event-unit");
            var fighter = new FighterState { id = id, unitId = id, ownerId = "neutral", slot = -1,
                kind = boss ? "ravager" : "iron_golem", neutral = true, summoned = false,
                x = roundEvent.x, y = roundEvent.y,
                hp = boss ? 105 + round * 15 : 28 + round * 3,
                maxHp = boss ? 105 + round * 15 : 28 + round * 3,
                attack = boss ? 9 + round * 1.5f : 0, attackSpeed = boss ? .7f : 1,
                range = boss ? 34 : 0, speed = boss ? 30 : 0,
                scale = boss ? .95f + Math.Min(.25f, State.round * .015f) : .65f,
                radius = boss ? 22 : 14, side = 1, attackCooldown = .5f };
            if (boss) CreatureAbilities.OnSpawn(fighter);
            State.fighters.Add(fighter);
            roundEvent.fighterId = id; roundEvent.active = true; roundEvent.claimedBy = null;
            Effect("event_spawn", fighter.x, fighter.y, -1, roundEvent.reward);
        }

        string BossTargetOwner()
        {
            string chosen = null;
            double greatest = -1;
            foreach (string ownerId in participants)
            {
                double health = 0;
                foreach (FighterState fighter in State.fighters)
                    if (!fighter.dead && !fighter.neutral && fighter.ownerId == ownerId) health += fighter.hp;
                if (health <= 0) continue;
                if (health > greatest) { greatest = health; chosen = ownerId; }
            }
            if (State.roundEvent != null) State.roundEvent.targetPlayerId = chosen;
            return chosen;
        }

        void UpdateRoundEvent()
        {
            ArenaEventState roundEvent = State.roundEvent;
            if (roundEvent == null || !roundEvent.active) return;
            FighterState fighter = State.fighters.Find(candidate => candidate.id == roundEvent.fighterId);
            if (fighter == null || fighter.dead) { roundEvent.active = false; return; }
            roundEvent.x = fighter.x; roundEvent.y = fighter.y;
        }

        static ArenaEventState CopyRoundEvent(ArenaEventState source)
        {
            if (source == null) return null;
            return new ArenaEventState { id = source.id, kind = source.kind, round = source.round,
                reward = source.reward, claimedBy = source.claimedBy, active = source.active,
                x = source.x, y = source.y, fighterId = source.fighterId, targetPlayerId = source.targetPlayerId };
        }
    }
}
