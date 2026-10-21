using System;
using System.Linq;
using System.Text.Json;
using BlockArena.Core;

internal static partial class Program
{
    private static void EffectPair(EffectState effect, FighterState source, FighterState target)
    {
        Equal(source.id, effect.sourceId); Equal(target.id, effect.targetId); Equal(source.kind, effect.kind);
    }

    private static void EffectChecks()
    {
        Check("melee and ranged visual events preserve the seventy-unit boundary and actual damage", () =>
        {
            foreach (float range in new[] { 70f, 70.01f })
            {
                ArenaGame game = AbilityBattle("cow", "cow");
                FighterState source = game.State.fighters.First(f => f.ownerId == "a");
                FighterState target = game.State.fighters.First(f => f.ownerId == "b");
                source.x = -10; target.x = 20; source.radius = target.radius = 0;
                source.range = range; source.attackCooldown = 0;
                target.hp = target.maxHp = 1000; target.armor = target.shield = 0;
                game.Tick(.05f);
                EffectState attack = game.State.effects.Single(e => e.type == (range <= 70 ? "strike" : "arrow"));
                EffectPair(attack, source, target);
                Near(-10, attack.x); Near(0, attack.y); Near(20, attack.targetX); Near(0, attack.targetY);
                Equal(range <= 70 ? 0 : 1, game.State.effects.Count(e => e.type == "arrow"));
                Equal(range <= 70 ? 1 : 0, game.State.effects.Count(e => e.type == "strike"));
                Near(994, target.hp); Equal(1, source.attacks); Near(1 / source.attackSpeed, source.attackCooldown);
                EffectState hit = game.State.effects.Single(e => e.type == "hit");
                EffectPair(hit, source, target); Near(20, hit.x); Near(0, hit.y); Near(6, hit.value);
                // Legacy hit coordinates stay at the victim with unused destination coordinates left at zero.
                Near(0, hit.targetX); Near(0, hit.targetY);
            }
        });

        Check("death identifies the corpse while the final hit identifies the killer and awards one original kill", () =>
        {
            ArenaGame game = AbilityBattle("cow", "cow");
            FighterState killer = game.State.fighters.First(f => f.ownerId == "a");
            FighterState victim = game.State.fighters.First(f => f.ownerId == "b");
            killer.attackCooldown = 0; victim.hp = 2; victim.armor = victim.shield = 0;
            int gold = game.State.players[0].gold;
            game.Tick(.05f);
            Assert(victim.dead);
            EffectState hit = game.State.effects.Single(e => e.type == "hit");
            EffectPair(hit, killer, victim); Near(2, hit.value);
            EffectState death = game.State.effects.Single(e => e.type == "death");
            EffectPair(death, victim, victim); Near(hit.x, death.x); Near(hit.y, death.y);
            PlayerState owner = game.State.players[0];
            Equal(1, owner.roundKills); Equal(1, owner.killIncome);
            Equal(gold + 1 + owner.baseIncome + owner.rankIncome, owner.gold);
        });

        Check("healing has a real source and patient and keeps its original source-to-patient coordinates", () =>
        {
            ArenaGame game = AbilityBattle("allay", "cow");
            FighterState healer = game.State.fighters.First(f => f.ownerId == "a");
            FighterState patient = ArenaGame.PreviewFighter(game.State.players[0], new UnitState { id = "effect-patient", kind = "cow", x = 45, y = 30 });
            patient.hp = 30; patient.speed = 0; patient.attackCooldown = 1000; game.State.fighters.Add(patient);
            game.Tick(.05f);
            EffectState heal = game.State.effects.Single(e => e.type == "heal");
            EffectPair(heal, healer, patient); Near(0, heal.x); Near(0, heal.y); Near(45, heal.targetX); Near(30, heal.targetY);
            Near(7, heal.value); Near(37, patient.hp);
        });

        Check("summon events point from the summoner to each unique fighter rather than its shared roster unit", () =>
        {
            ArenaGame game = AbilityBattle("evoker", "cow");
            FighterState summoner = game.State.fighters.First(f => f.ownerId == "a");
            summoner.attackCooldown = 0; game.Tick(.05f);
            EffectState[] spawns = game.State.effects.Where(e => e.type == "spawn").ToArray();
            Equal(2, spawns.Length); Equal(2, spawns.Select(e => e.targetId).Distinct().Count());
            foreach (EffectState spawn in spawns)
            {
                FighterState child = game.State.fighters.Single(f => f.id == spawn.targetId);
                EffectPair(spawn, summoner, child); Equal("vex", child.kind); Assert(child.summoned);
                Assert(child.id != child.unitId); Near(child.x, spawn.x); Near(child.y, spawn.y);
                Near(0, spawn.targetX); Near(0, spawn.targetY);
            }
        });

        Check("a split death and both child spawns retain the dead parent identity without duplicating rewards", () =>
        {
            ArenaGame game = AbilityBattle("cow", "slime");
            FighterState killer = game.State.fighters.First(f => f.ownerId == "a");
            FighterState parent = game.State.fighters.First(f => f.ownerId == "b");
            parent.hp = 1; killer.attackCooldown = 0;
            game.Tick(.05f);
            Assert(parent.dead); Equal(1, game.State.players[0].roundKills);
            EffectPair(game.State.effects.Single(e => e.type == "death"), parent, parent);
            EffectState[] spawns = game.State.effects.Where(e => e.type == "spawn").ToArray(); Equal(2, spawns.Length);
            foreach (EffectState spawn in spawns)
            {
                FighterState child = game.State.fighters.Single(f => f.id == spawn.targetId);
                EffectPair(spawn, parent, child); Assert(child.summoned && child.mini && !child.dead);
            }
        });

        Check("effect metadata survives independent snapshots and JSON while old packets leave it optional", () =>
        {
            ArenaGame game = AbilityBattle("blaze", "cow");
            game.State.fighters.First(f => f.ownerId == "a").attackCooldown = 0; game.Tick(.05f);
            GameState snapshot = game.SnapshotFor("a");
            GameState wire = JsonSerializer.Deserialize<GameState>(Json(snapshot), JsonOptions);
            foreach (EffectState original in game.State.effects)
            {
                EffectState copied = snapshot.effects.Single(e => e.id == original.id);
                EffectState decoded = wire.effects.Single(e => e.id == original.id);
                Equal(original.sourceId, copied.sourceId); Equal(original.targetId, copied.targetId); Equal(original.kind, copied.kind);
                Equal(original.sourceId, decoded.sourceId); Equal(original.targetId, decoded.targetId); Equal(original.kind, decoded.kind);
                copied.sourceId = "changed"; copied.targetId = "changed"; copied.kind = "changed";
                Assert(original.sourceId != "changed" && original.targetId != "changed" && original.kind != "changed");
            }
            EffectState legacy = JsonSerializer.Deserialize<EffectState>("{\"id\":\"old\",\"type\":\"hit\",\"x\":12}", JsonOptions);
            Equal("hit", legacy.type); Near(12, legacy.x);
            Assert(legacy.sourceId == null && legacy.targetId == null && legacy.kind == null);
        });

        Check("every species emits effect identities that resolve to the actual source and destination fighters", () =>
        {
            foreach (string kind in CreatureCatalog.Kinds)
            {
                ArenaGame game = AbilityBattle(kind, "cow");
                FighterState caster = game.State.fighters.First(f => f.ownerId == "a");
                FighterState target = game.State.fighters.First(f => f.ownerId == "b");
                caster.attackCooldown = 0; caster.hp = caster.maxHp * .6f; caster.radius = target.radius = 0; target.x = caster.x + 10;
                target.hp = target.maxHp = 100000; target.armor = target.shield = 0;
                game.Tick(.05f); Assert(game.State.effects.Count > 0, kind + " emitted no combat event");
                foreach (EffectState effect in game.State.effects)
                {
                    FighterState source = game.State.fighters.SingleOrDefault(f => f.id == effect.sourceId);
                    FighterState destination = game.State.fighters.SingleOrDefault(f => f.id == effect.targetId);
                    Assert(source != null && destination != null, kind + " emitted unresolvable " + effect.type + " identities");
                    Equal(source.kind, effect.kind);
                    if (effect.type == "death") Assert(destination.dead && source == destination);
                }
            }
        });
    }
}
