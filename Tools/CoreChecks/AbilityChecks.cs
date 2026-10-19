using System;
using System.Collections.Generic;
using System.Linq;
using BlockArena.Core;

internal static partial class Program
{
    private static FighterState AbilityPreview(string kind, params string[] upgrades)
    {
        var owner = new PlayerState { id = "ability-owner" };
        owner.upgrades.AddRange(upgrades);
        return ArenaGame.PreviewFighter(owner, new UnitState { id = "ability-" + kind, kind = kind });
    }

    // Real arena combat with movement and automatic attacks held until each test releases them.
    private static ArenaGame AbilityBattle(string kindA, string kindB, string[] upgrades = null)
    {
        var game = Two();
        for (int i = 0; i < 2; i++)
        {
            var player = game.State.players[i];
            player.units = new List<UnitState> { new UnitState { id = "ability-unit-" + player.id, kind = i == 0 ? kindA : kindB, x = i * 20, y = 0 } };
            if (upgrades != null) player.upgrades.AddRange(upgrades);
        }
        Battle(game);
        foreach (var fighter in game.State.fighters) { fighter.speed = 0; fighter.attackCooldown = 1000; }
        return game;
    }

    private static void AbilityChecks()
    {
        Check("armor reduces incoming damage before shields and piercing subtracts percentage points", () =>
        {
            var plain = AbilityPreview("cow");
            var copper = AbilityPreview("copper_golem");
            Near(.18f, copper.armor); Near(18, copper.shield);
            Near(0, CreatureAbilities.BeforeDamage(copper, 20, plain)); Near(1.6f, copper.shield);
            Near(14.8f, CreatureAbilities.BeforeDamage(copper, 20, plain)); Near(0, copper.shield);
            var archer = AbilityPreview("skeleton", "skeleton_school");
            var turtle = AbilityPreview("turtle");
            Near(68, CreatureAbilities.BeforeDamage(turtle, 100, plain));
            Near(100, CreatureAbilities.BeforeDamage(turtle, 100, archer));
            copper = AbilityPreview("copper_golem");
            Near(2, CreatureAbilities.BeforeDamage(copper, 20, archer)); Near(0, copper.shield);
            turtle.armor = 1.2f; Near(20, CreatureAbilities.BeforeDamage(turtle, 100, plain));
        });

        Check("dodge uses accumulated incoming hits and stacked dodge never exceeds fifty percent", () =>
        {
            var attacker = AbilityPreview("cow"); var bat = AbilityPreview("bat");
            var damage = Enumerable.Range(0, 8).Select(_ => CreatureAbilities.BeforeDamage(bat, 10, attacker)).ToArray();
            Equal(2, damage.Count(value => value == 0)); Near(60, damage.Sum()); Equal(8, bat.hits);
            bat = AbilityPreview("bat"); bat.skills = new[] { "bat_dodge", "cat_dodge", "allay_dodge" };
            damage = Enumerable.Range(0, 8).Select(_ => CreatureAbilities.BeforeDamage(bat, 10, attacker)).ToArray();
            Equal(4, damage.Count(value => value == 0)); Near(40, damage.Sum());
            bat.shield = 25; bat.hits = 1;
            Near(0, CreatureAbilities.BeforeDamage(bat, 10, attacker)); Near(25, bat.shield);
        });

        Check("poison and burning deliver exact full-duration damage and stop after expiry", () =>
        {
            foreach (var scenario in new[] { (kind: "cave_spider", damage: 10f), (kind: "blaze", damage: 16f) })
            {
                var game = AbilityBattle(scenario.kind, "cow");
                var caster = game.State.fighters.First(f => f.ownerId == "a"); var target = game.State.fighters.First(f => f.ownerId == "b");
                target.armor = 0; target.hp = target.maxHp = 1000; caster.attackCooldown = 0;
                game.Tick(.05f); caster.attackCooldown = 1000;
                if (scenario.kind == "cave_spider") { Near(2.5f, target.poisonDps); Assert(ReferenceEquals(caster, target.poisonSource)); }
                else { Near(4, target.burnDps); Assert(ReferenceEquals(caster, target.burnSource)); }
                Advance(game, 4.35f); Near(1000 - caster.attack - scenario.damage, target.hp);
                Near(0, target.poisonTime); Near(0, target.burnTime);
                Assert(target.poisonSource == null && target.burnSource == null);
                float afterExpiry = target.hp; Advance(game, .7f); Near(afterExpiry, target.hp);
            }
        });

        Check("poison and burn keep their dead caster source and award the original player a real kill", () =>
        {
            foreach (string kind in new[] { "cave_spider", "blaze" })
            {
                var game = AbilityBattle(kind, "cow");
                var owner = game.State.players[0]; var caster = game.State.fighters.First(f => f.ownerId == "a");
                var target = game.State.fighters.First(f => f.ownerId == "b");
                var reserve = ArenaGame.PreviewFighter(owner, new UnitState { id = "ability-reserve", kind = "cow", x = -350, y = 350 });
                reserve.speed = 0; reserve.attackCooldown = 1000; game.State.fighters.Add(reserve);
                var enemyReserve = ArenaGame.PreviewFighter(game.State.players[1], new UnitState { id = "ability-enemy-reserve", kind = "cow", x = 350, y = 350 });
                enemyReserve.speed = 0; enemyReserve.attackCooldown = 1000; game.State.fighters.Add(enemyReserve);
                target.armor = 0; target.hp = target.maxHp = 1000; caster.attackCooldown = 0;
                game.Tick(.05f); caster.attackCooldown = 1000; caster.hp = 1;
                target.attack = 100; target.attackCooldown = 0; game.Tick(.05f); target.attackCooldown = 1000;
                Assert(caster.dead); Equal("battle", game.State.phase); Equal(1, game.State.players[1].roundKills);
                Assert(ReferenceEquals(caster, kind == "cave_spider" ? target.poisonSource : target.burnSource));
                int gold = owner.gold; target.hp = .1f; Advance(game, .65f);
                Assert(target.dead, kind + " stopped dealing damage after caster death");
                Equal("a", target.lastAttackerId); Equal(1, owner.roundKills); Equal(1, owner.totalKills);
                Equal("battle", game.State.phase); Equal(gold + 1, owner.gold); Equal(gold + 1, game.SnapshotFor(owner.id).players[0].gold);
            }
        });

        Check("healing selects the lowest health ratio in range, respects cooldown and can heal self", () =>
        {
            var healer = AbilityPreview("allay"); healer.x = 0;
            var patient = AbilityPreview("cow"); patient.x = 20; patient.hp = 43;
            var smaller = AbilityPreview("armadillo"); smaller.x = 30; smaller.hp = 32;
            var outside = AbilityPreview("cow"); outside.x = 111; outside.hp = 1;
            var enemy = AbilityPreview("cow"); enemy.ownerId = "enemy"; enemy.x = 20; enemy.hp = 1;
            var fighters = new List<FighterState> { healer, patient, smaller, outside, enemy };
            CreatureAbilities.Tick(healer, fighters, .1f, null, null);
            Near(50, patient.hp); Near(32, smaller.hp); Near(1, outside.hp); Near(1, enemy.hp);
            CreatureAbilities.Tick(healer, fighters, 3.9f, null, null); Near(32, smaller.hp);
            CreatureAbilities.Tick(healer, fighters, .11f, null, null); Near(39, smaller.hp);
            patient.hp = patient.maxHp; smaller.hp = smaller.maxHp; healer.hp = healer.maxHp - 1;
            CreatureAbilities.Tick(healer, fighters, 4.1f, null, null); Near(healer.maxHp, healer.hp);
        });

        Check("regeneration continues during stun, clamps to maximum health and never revives dead units", () =>
        {
            var panda = AbilityPreview("panda"); panda.hp = 100; panda.stunTime = 10;
            var fighters = new List<FighterState> { panda };
            CreatureAbilities.Tick(panda, fighters, 2, null, null); Near(104.4f, panda.hp); Near(8, panda.stunTime);
            CreatureAbilities.Tick(panda, fighters, 20, null, null); Near(panda.maxHp, panda.hp);
            panda.dead = true; panda.hp = 0;
            CreatureAbilities.Tick(panda, fighters, 2, null, null); Near(0, panda.hp);
        });

        Check("shield-only hits do not grant lifesteal", () =>
        {
            var game = AbilityBattle("enderman", "copper_golem");
            var attacker = game.State.fighters.First(f => f.ownerId == "a"); var target = game.State.fighters.First(f => f.ownerId == "b");
            attacker.hp = 40; attacker.attackCooldown = 0; game.Tick(.05f);
            Near(40, attacker.hp); Near(target.maxHp, target.hp); Near(4.06f, target.shield);
        });

        Check("lifesteal uses actual life removed rather than the attack's overkill amount", () =>
        {
            var game = AbilityBattle("enderman", "cow");
            var attacker = game.State.fighters.First(f => f.ownerId == "a"); var target = game.State.fighters.First(f => f.ownerId == "b");
            attacker.hp = 40; target.armor = 0; target.hp = 2; attacker.attackCooldown = 0;
            game.Tick(.05f); Assert(target.dead); Near(40.4f, attacker.hp); Equal(1, game.State.players[0].roundKills);
        });

        Check("lifesteal and thorns use real damage and reflected hits never trigger a recursive chain", () =>
        {
            var game = AbilityBattle("zombie", "zombie", new[] { "undead_legion", "zombie_resilience" });
            var attacker = game.State.fighters.First(f => f.ownerId == "a"); var target = game.State.fighters.First(f => f.ownerId == "b");
            attacker.hp = target.hp = 64; attacker.attackCooldown = 0;
            game.Tick(.05f);
            // 13 attack, 10% armor: 11.7 actual; 12% lifesteal; 18% reflection, then attacker's 10% armor.
            Near(63.5486f, attacker.hp); Near(52.34f, target.hp);
            Equal(1, game.State.effects.Count(e => e.type == "thorns")); Equal(1, attacker.attacks); Equal(0, target.attacks);
        });

        Check("stun prevents real attacks until expiry and cannot be reapplied before its independent cooldown", () =>
        {
            var game = AbilityBattle("ravager", "cow");
            var caster = game.State.fighters.First(f => f.ownerId == "a"); var target = game.State.fighters.First(f => f.ownerId == "b");
            caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000; target.attackCooldown = 0;
            Assert(target.stunTime >= .49f && target.stunTime <= .551f); float before = caster.hp;
            Advance(game, .35f); Equal(0, target.attacks); Near(before, caster.hp);
            caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000;
            Assert(target.stunTime < .25f); Advance(game, .3f);
            Near(0, target.stunTime); Equal(1, target.attacks); Assert(caster.hp < before);
            Assert(caster.abilityCooldowns["ravager_stun"] > 5);
        });

        Check("slow changes actual movement speed and restores full movement after expiry", () =>
        {
            var game = AbilityBattle("stray", "cow");
            var caster = game.State.fighters.First(f => f.ownerId == "a"); var target = game.State.fighters.First(f => f.ownerId == "b");
            caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000;
            Near(.6f, target.slowMultiplier); Assert(target.slowTime > 2.9f);
            caster.x = 200; target.x = 0; target.speed = CreatureCatalog.Get("cow").speed;
            game.Tick(.1f); Near(2.04f, target.x);
            Advance(game, 3.1f); Near(0, target.slowTime); Near(1, target.slowMultiplier);
            caster.x = 200; target.x = 0; game.Tick(.1f); Near(3.4f, target.x);
        });

        Check("evoker creates two allied vexes per nine-second cooldown with real summoned stats", () =>
        {
            var game = AbilityBattle("evoker", "cow");
            var caster = game.State.fighters.First(f => f.ownerId == "a"); var target = game.State.fighters.First(f => f.ownerId == "b");
            target.hp = target.maxHp = 10000; caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000;
            var summoned = game.State.fighters.Where(f => f.kind == "vex").ToArray(); Equal(2, summoned.Length);
            foreach (var vex in summoned)
            {
                Assert(vex.summoned && vex.ownerId == "a"); Near(24.7f, vex.maxHp); Near(.8f, vex.scale);
                Assert(vex.skills.Contains("vex_pierce") && vex.skills.Contains("vex_dodge")); vex.speed = 0; vex.attackCooldown = 1000;
            }
            caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000;
            Equal(2, game.State.fighters.Count(f => f.kind == "vex"));
            Advance(game, 8.8f); Assert(caster.abilityCooldowns["evoker_summon_vex"] > 0);
            caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000;
            Equal(2, game.State.fighters.Count(f => f.kind == "vex"));
            Advance(game, .3f); caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000;
            Equal(4, game.State.fighters.Count(f => f.kind == "vex"));
            caster.summoned = true; caster.abilityCooldowns["evoker_summon_vex"] = 0; caster.attackCooldown = 0; game.Tick(.05f);
            Equal(4, game.State.fighters.Count(f => f.kind == "vex"));
        });

        Check("killing real summoned vexes never grants extra kill income", () =>
        {
            var game = AbilityBattle("evoker", "iron_golem");
            var caster = game.State.fighters.First(f => f.ownerId == "a"); var killer = game.State.fighters.First(f => f.ownerId == "b");
            caster.attackCooldown = 0; game.Tick(.05f); caster.attackCooldown = 1000;
            Equal(2, game.State.fighters.Count(f => f.kind == "vex"));
            foreach (var fighter in game.State.fighters.Where(f => f.ownerId == "a")) { fighter.hp = 1; fighter.attackCooldown = 1000; fighter.speed = 0; }
            killer.attack = 10000; killer.attackCooldown = 0; game.Tick(.05f);
            Assert(game.State.fighters.Where(f => f.ownerId == "a").All(f => f.dead));
            Equal(1, game.State.players[1].roundKills); Equal(1, game.State.players[1].totalKills);
        });

        Check("magma split children inherit equipment and permanent skills but cannot split again", () =>
        {
            var game = Two();
            game.State.players[0].units = new List<UnitState> { new UnitState { id = "split-killer", kind = "iron_golem", x = 0 } };
            var owner = game.State.players[1]; owner.upgrades.Add("nether_forge");
            owner.units = new List<UnitState> { new UnitState { id = "split-parent", kind = "magma_cube", x = 20, equipment = new List<string> { "diamond_armor", "iron_sword" } } };
            Battle(game); foreach (var f in game.State.fighters) { f.speed = 0; f.attackCooldown = 1000; }
            var killer = game.State.fighters.First(f => f.ownerId == "a"); killer.attackCooldown = 0;
            game.State.fighters.First(f => f.kind == "magma_cube").hp = 1; game.Tick(.05f);
            var children = game.State.fighters.Where(f => f.kind == "magma_cube" && !f.dead).ToArray(); Equal(2, children.Length);
            foreach (var child in children)
            {
                Assert(child.mini && child.summoned && child.ownerId == owner.id); Near(.62f, child.scale);
                Near(68, child.maxHp); Near(16, child.attack); Near(.28f, child.armor); Equal(2, child.equipment.Length);
                Assert(child.skills.Contains("magma_cube_split") && child.skills.Contains("magma_cube_burn"));
                child.hp = 1; child.attackCooldown = 1000; child.speed = 0;
            }
            killer.attack = 10000; killer.attackCooldown = 0; game.Tick(.05f);
            Equal(3, game.State.fighters.Count(f => f.kind == "magma_cube"));
            Assert(game.State.fighters.Where(f => f.kind == "magma_cube").All(f => f.dead)); Equal(1, game.State.players[0].roundKills);
        });

        Check("death-triggered splitting is idempotent and never runs for mini or summoned children", () =>
        {
            foreach (string kind in new[] { "slime", "magma_cube" })
            {
                int requested = 0; var parent = AbilityPreview(kind); parent.dead = true; var fighters = new List<FighterState> { parent };
                Action<FighterState, string, int> observe = (source, childKind, count) => { Equal(kind, childKind); Equal(2, count); requested++; };
                CreatureAbilities.OnDeath(parent, fighters, null, null, observe);
                CreatureAbilities.OnDeath(parent, fighters, null, null, observe); Equal(1, requested);
                var mini = AbilityPreview(kind); mini.mini = true; mini.dead = true;
                var summoned = AbilityPreview(kind); summoned.summoned = true; summoned.dead = true;
                CreatureAbilities.OnDeath(mini, fighters, null, null, observe);
                CreatureAbilities.OnDeath(summoned, fighters, null, null, observe); Equal(1, requested);
            }
        });
    }
}
