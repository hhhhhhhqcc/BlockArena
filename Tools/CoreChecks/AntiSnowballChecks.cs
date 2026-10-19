using System;
using System.Collections.Generic;
using System.Linq;
using BlockArena.Core;

internal static partial class Program
{
    static ArenaGame EventBattle(string kind)
    {
        ArenaGame game = Two();
        game.State.players[0].units = new List<UnitState> { new UnitState { id = "event-a", kind = "skeleton", x = -200 } };
        game.State.players[1].units = new List<UnitState> { new UnitState { id = "event-b", kind = "cow", x = 200 } };
        game.State.roundEvent = new ArenaEventState { id = "forced-event", kind = kind, round = game.State.round,
            reward = kind == "boss" ? 4 : 3 };
        Battle(game, true);
        FreezeCombat(game);
        return game;
    }

    static void FreezeCombat(ArenaGame game)
    {
        foreach (FighterState fighter in game.State.fighters)
        {
            fighter.speed = 0; fighter.attackCooldown = 10000; fighter.targetId = null; fighter.think = 10000;
        }
    }

    static void Strike(ArenaGame game, FighterState attacker, FighterState victim)
    {
        victim.hp = 1; victim.shield = victim.armor = 0;
        attacker.attack = 10000; attacker.range = 2000; attacker.attackCooldown = 0;
        attacker.targetId = victim.id; attacker.think = 10000;
        game.Tick(.05f);
        Assert(victim.dead, "The controlled strike missed its victim.");
    }

    static void AntiSnowballChecks()
    {
        Check("paid recruit refresh changes three distinct choices and protects stale and foreign offer revisions", () =>
        {
            ArenaGame game = Two(false); PlayerState player = game.State.players[0]; player.gold = 100;
            for (int i = 0; i < 12; i++)
            {
                string revision = player.recruitOfferId;
                string[] choices = (string[])player.shop.Clone(); int gold = player.gold;
                game.Apply(player.id, new GameCommand { type = "recruit_refresh", offerId = revision });
                Equal(gold - ArenaGame.RecruitRefreshPrice, player.gold);
                Equal(3, player.shop.Distinct().Count());
                Assert(!new HashSet<string>(choices).SetEquals(player.shop));
                Assert(player.recruitOfferId != revision && player.recruitPending);
                string before = Json(game.State);
                Reject(() => game.Apply(player.id, new GameCommand { type = "choose", index = 0, offerId = revision }));
                Reject(() => game.Apply(player.id, new GameCommand { type = "recruit_refresh", offerId = revision }));
                Equal(before, Json(game.State));
            }
            string current = Json(game.State);
            Reject(() => game.Apply(player.id, new GameCommand { type = "recruit_refresh", offerId = game.State.players[1].recruitOfferId }));
            Equal(current, Json(game.State));
            player.gold = 1; current = Json(game.State);
            Reject(() => game.Apply(player.id, new GameCommand { type = "recruit_refresh", offerId = player.recruitOfferId }));
            Equal(current, Json(game.State));
            player.gold = 100;
            string picked = player.shop[0]; int price = CreatureCatalog.Get(picked).price;
            game.Apply(player.id, new GameCommand { type = "choose", index = 0, offerId = player.recruitOfferId });
            Equal(price, player.units.Last().paidPrice); Equal(100 - price, player.gold);
            current = Json(game.State);
            Reject(() => game.Apply(player.id, new GameCommand { type = "recruit_refresh" })); Equal(current, Json(game.State));
        });
        Check("recruit offers remain private and snapshot copies preserve paid price without aliasing", () =>
        {
            ArenaGame game = Two(false); PlayerState player = game.State.players[0];
            GameState own = game.SnapshotFor(player.id); GameState other = game.SnapshotFor("b");
            Equal(player.recruitOfferId, own.players[0].recruitOfferId);
            Assert(other.players[0].recruitOfferId == null && other.players[0].shop == null);
            game.Apply(player.id, new GameCommand { type = "choose", index = 0 });
            own = game.SnapshotFor(player.id);
            Equal(player.units.Last().paidPrice, own.players[0].units.Last().paidPrice);
            own.players[0].units.Last().paidPrice = 999;
            Assert(player.units.Last().paidPrice != 999);
        });
        Check("dismissal refunds half actual paid price while free starters, ownership and final unit stay protected", () =>
        {
            ArenaGame game = Two(false); PlayerState player = game.State.players[0];
            Assert(player.units.All(unit => unit.paidPrice == 0));
            int gold = player.gold;
            game.Apply(player.id, new GameCommand { type = "dismiss", unitId = player.units[0].id });
            Equal(gold, player.gold); Equal(1, player.units.Count); Assert(player.recruitPending);
            string before = Json(game.State);
            Reject(() => game.Apply(player.id, new GameCommand { type = "dismiss", unitId = player.units[0].id }));
            Reject(() => game.Apply(player.id, new GameCommand { type = "dismiss", unitId = game.State.players[1].units[0].id }));
            Equal(before, Json(game.State));
            var paid = new UnitState { id = "paid-dismiss", kind = "cow", paidPrice = 9 };
            player.units.Add(paid); player.gold = 0; player.ready = true;
            game.Apply(player.id, new GameCommand { type = "dismiss", unitId = paid.id });
            Equal(4, player.gold); Assert(!player.ready && player.recruitPending); Equal(1, player.units.Count);
            player.shop = new[] { "chicken", "cow", "pig" };
            Assert(CreatureCatalog.Get("chicken").price <= player.gold);
            game.Apply(player.id, new GameCommand { type = "choose", index = 0, offerId = player.recruitOfferId });
            Assert(!player.recruitPending); Equal(2, player.units.Count);
            game.Apply("b", new GameCommand { type = "skip" });
            int price = player.units.Last().paidPrice; gold = player.gold;
            game.Apply(player.id, new GameCommand { type = "dismiss", unitId = player.units.Last().id });
            Equal(gold + price / 2, player.gold);
            Battle(game); before = Json(game.State);
            Reject(() => game.Apply(player.id, new GameCommand { type = "dismiss", unitId = player.units[0].id }));
            Equal(before, Json(game.State));
        });
        Check("leader uses commander health, previous rank and army power and never breaks an exact tie by seat", () =>
        {
            ArenaGame game = Two(); Battle(game); Assert(game.State.leaderId == null);
            game = Two(); game.State.players[0].hp = 19; Battle(game); Equal("b", game.State.leaderId);
            string leader = game.State.leaderId;
            game.State.players[0].hp = 100; game.State.players[1].hp = 1;
            game.Tick(.05f); Equal(leader, game.State.leaderId);
            game = Two(); game.State.players[0].lastRank = 2; game.State.players[1].lastRank = 1;
            game.State.players[0].units.Add(new UnitState { id = "stronger", kind = "ender_dragon" });
            Battle(game); Equal("b", game.State.leaderId);
            game = Two(); game.State.players[0].units.Add(new UnitState { id = "more-power", kind = "cow" });
            Assert(ArenaGame.ArmyPower(game.State.players[0]) > ArenaGame.ArmyPower(game.State.players[1]));
            Battle(game); Equal("a", game.State.leaderId);
        });
        Check("original kill gold caps at five while leader bounty is paid per three kills and per recipient up to three", () =>
        {
            ArenaGame game = new ArenaGame("a", "甲", 55);
            game.AddPlayer("b", "乙"); game.AddPlayer("c", "丙");
            game.Apply("a", new GameCommand { type = "start", bots = false }); ChooseAll(game);
            game.State.players[0].hp = 19; game.State.players[2].hp = 18;
            game.State.players[0].units = new List<UnitState> { new UnitState { id = "killer-a", kind = "skeleton" } };
            game.State.players[2].units = new List<UnitState> { new UnitState { id = "killer-c", kind = "skeleton" } };
            game.State.players[1].units.Clear();
            for (int i = 0; i < 18; i++) game.State.players[1].units.Add(new UnitState { id = "leader-" + i, kind = "cow", x = 200 });
            Battle(game); FreezeCombat(game); Equal("b", game.State.leaderId);
            PlayerState a = game.State.players[0], c = game.State.players[2];
            FighterState attackerA = game.State.fighters.First(f => f.ownerId == "a");
            FighterState attackerC = game.State.fighters.First(f => f.ownerId == "c");
            FighterState[] leaderUnits = game.State.fighters.Where(f => f.ownerId == "b").ToArray();
            int startingA = a.gold, startingC = c.gold;
            for (int i = 0; i < 12; i++)
            {
                Strike(game, attackerA, leaderUnits[i]);
                Equal(i + 1, a.roundKills); Equal(Math.Min(5, i + 1), a.killIncome);
                Equal(Math.Min(3, (i + 1) / 3), a.bountyIncome);
                Equal(startingA + a.killIncome + a.bountyIncome, a.gold);
            }
            Equal(12, a.totalKills); Equal(12, a.bountyKills); Equal(5, a.killIncome); Equal(3, a.bountyIncome);
            for (int i = 12; i < 15; i++) Strike(game, attackerC, leaderUnits[i]);
            Equal(3, c.bountyKills); Equal(1, c.bountyIncome); Equal(startingC + 4, c.gold);
            FighterState summoned = ArenaGame.PreviewFighter(game.State.players[1], new UnitState { id = "summoned-leader", kind = "cow" });
            summoned.summoned = true; summoned.id = "summoned-actor"; summoned.attackCooldown = 1000;
            game.State.fighters.Add(summoned); Strike(game, attackerA, summoned);
            Equal(12, a.roundKills); Equal(12, a.bountyKills); Equal(startingA + 8, a.gold);
            game.State.timeLeft = .001f; FreezeCombat(game); game.Tick(.05f);
            Equal(6 + a.rankIncome + 5 + 3, a.lastIncome);
            Equal(startingA + a.lastIncome, a.gold);
            PlacementState placement = game.State.placements.First(p => p.playerId == "a");
            Equal(5, placement.killIncome); Equal(3, placement.bountyIncome); Equal(12, placement.bountyKills);
        });
        Check("fourth place now earns two rank gold and settlement never re-awards instant event or bounty income", () =>
        {
            ArenaGame game = new ArenaGame("a", "甲", 9);
            foreach (string id in new[] { "b", "c", "d" }) game.AddPlayer(id, id);
            game.Apply("a", new GameCommand { type = "start", bots = false }); ChooseAll(game); Battle(game); FreezeCombat(game);
            foreach (FighterState fighter in game.State.fighters) fighter.hp = (4 - fighter.slot) * 10;
            int[] gold = game.State.players.Select(player => player.gold).ToArray();
            game.State.timeLeft = .001f; game.Tick(.05f);
            int[] expected = { 4, 3, 2, 2 };
            for (int i = 0; i < 4; i++)
            {
                Equal(expected[i], game.State.players[i].rankIncome);
                Equal(gold[i] + 6 + expected[i], game.State.players[i].gold);
            }
        });
        Check("round events are announced once during recruitment and explicit seeds reproduce their shared selection", () =>
        {
            var kinds = new HashSet<string>();
            for (int seed = 0; seed < 60; seed++)
            {
                ArenaGame first = new ArenaGame("a", "甲", seed); ArenaGame second = new ArenaGame("a", "甲", seed);
                first.Apply("a", new GameCommand { type = "start" }); second.Apply("a", new GameCommand { type = "start" });
                Equal(Json(first.State), Json(second.State));
                ArenaEventState announced = first.State.roundEvent;
                kinds.Add(announced.kind); Equal(first.State.round, announced.round); Assert(!announced.active);
                Equal(announced.id, first.SnapshotFor("a").roundEvent.id);
                Equal(announced.id, first.SnapshotFor("bot-1").roundEvent.id);
            }
            Assert(kinds.SetEquals(new[] { "none", "boss", "gold" }));
        });
        Check("boss and gold last hits award only their event reward once including a summoned attacker's owner", () =>
        {
            foreach (string kind in new[] { "boss", "gold" })
            {
                ArenaGame game = EventBattle(kind); PlayerState owner = game.State.players[0];
                FighterState actor = game.State.fighters.First(f => f.neutral);
                FighterState attacker = game.State.fighters.First(f => f.ownerId == owner.id);
                Assert(actor.slot == -1 && actor.ownerId == "neutral" && !actor.summoned);
                int unitCount = owner.units.Count, gold = owner.gold;
                if (kind == "gold") attacker.summoned = true;
                Strike(game, attacker, actor);
                Equal(kind == "boss" ? 4 : 3, owner.eventIncome);
                Equal(gold + owner.eventIncome, owner.gold); Equal(0, owner.roundKills); Equal(0, owner.killIncome); Equal(0, owner.bountyKills);
                Assert(!game.State.roundEvent.active); Equal(owner.id, game.State.roundEvent.claimedBy);
                Equal(unitCount, owner.units.Count); Equal("battle", game.State.phase);
                FreezeCombat(game); Advance(game, .5f); Equal(gold + owner.eventIncome, owner.gold);
                game.State.timeLeft = .001f; game.Tick(.05f);
                Equal(6 + owner.rankIncome + owner.eventIncome, owner.lastIncome);
                Equal(gold + owner.lastIncome, owner.gold);
                Equal(owner.eventIncome, game.State.placements.First(p => p.playerId == owner.id).eventIncome);
            }
        });
        Check("neutral gold is stationary and cannot attack or acquire golem spawn armor", () =>
        {
            ArenaGame game = EventBattle("gold"); FighterState gold = game.State.fighters.First(f => f.neutral);
            Near(0, gold.attack); Near(0, gold.speed); Near(0, gold.armor); Near(0, gold.shield);
            gold.attackCooldown = 0;
            foreach (FighterState fighter in game.State.fighters.Where(f => !f.neutral)) { fighter.x = 1; fighter.y = 1; }
            float[] health = game.State.fighters.Where(f => !f.neutral).Select(f => f.hp).ToArray();
            Advance(game, .5f); Near(0, gold.x); Near(0, gold.y); Equal(0, gold.attacks);
            int index = 0; foreach (FighterState fighter in game.State.fighters.Where(f => !f.neutral)) Near(health[index++], fighter.hp);
        });
        Check("gold resource can be finished by poison and credits the original caster once", () =>
        {
            ArenaGame game = EventBattle("gold"); FighterState resource = game.State.fighters.First(f => f.neutral);
            FighterState caster = game.State.fighters.First(f => f.ownerId == "a");
            resource.hp = 1; resource.poisonDps = 10; resource.poisonTime = .5f; resource.poisonSource = caster;
            int gold = game.State.players[0].gold;
            Advance(game, .6f); Assert(resource.dead); Equal(3, game.State.players[0].eventIncome);
            Equal(gold + 3, game.State.players[0].gold); Equal(0, game.State.players[0].roundKills);
        });
        Check("boss reevaluates the army with most current living health rather than commander health or fixed max health", () =>
        {
            ArenaGame game = EventBattle("boss"); FighterState boss = game.State.fighters.First(f => f.neutral);
            FighterState a = game.State.fighters.First(f => f.ownerId == "a"), b = game.State.fighters.First(f => f.ownerId == "b");
            a.hp = 100; b.hp = 80; game.State.players[0].hp = 1; game.Tick(.05f);
            Equal(a.id, boss.targetId); Equal("a", game.State.roundEvent.targetPlayerId);
            a.hp = 20; b.hp = 70; game.Tick(.05f); Equal(b.id, boss.targetId);
            FighterState dead = ArenaGame.PreviewFighter(game.State.players[0], new UnitState { id = "dead-body", kind = "ender_dragon" });
            dead.hp = 10000; dead.dead = true; game.State.fighters.Add(dead);
            game.Tick(.05f); Equal(b.id, boss.targetId);
        });
        Check("a neutral boss cannot earn kill gold or keep a one-army battle alive or enter settlement ranks", () =>
        {
            ArenaGame game = EventBattle("boss"); FighterState boss = game.State.fighters.First(f => f.neutral);
            FighterState a = game.State.fighters.First(f => f.ownerId == "a"), b = game.State.fighters.First(f => f.ownerId == "b");
            a.hp = 10; b.hp = 100; b.shield = b.armor = 0;
            boss.attack = 10000; boss.range = 2000; boss.attackCooldown = 0;
            game.Tick(.05f); Assert(b.dead); Assert(!boss.dead); Equal("results", game.State.phase);
            Equal(2, game.State.placements.Count); Assert(game.State.placements.All(p => p.playerId != "neutral"));
            Assert(game.State.players.All(player => player.roundKills == 0 && player.bountyIncome == 0 && player.eventIncome == 0));
            Assert(!game.State.roundEvent.active && game.State.roundEvent.claimedBy == null);
        });
        Check("event DTO, leader, live incomes and neutral fields are deeply copied in every snapshot", () =>
        {
            ArenaGame game = EventBattle("boss"); game.State.leaderId = "b";
            game.State.players[0].bountyKills = 6; game.State.players[0].bountyIncome = 2; game.State.players[0].eventIncome = 4;
            GameState copy = game.SnapshotFor("a"); Equal("b", copy.leaderId);
            Equal(6, copy.players[0].bountyKills); Equal(2, copy.players[0].bountyIncome); Equal(4, copy.players[0].eventIncome);
            Assert(copy.fighters.Any(f => f.neutral && f.slot == -1));
            string original = Json(game.State); copy.roundEvent.reward = 99; copy.roundEvent.claimedBy = "fake";
            copy.players[0].bountyIncome = 100; copy.fighters.First(f => f.neutral).hp = 0;
            Equal(original, Json(game.State));
        });
    }
}
