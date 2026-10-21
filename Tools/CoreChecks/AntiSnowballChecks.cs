using System;
using System.Collections.Generic;
using System.Linq;
using BlockArena.Core;

internal static partial class Program
{
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
        Check("central guardian events never generate while fixed map objectives remain", () =>
        {
            for (int seed=0;seed<30;seed++)
            {
                var game=new ArenaGame("a","甲",seed);game.AddPlayer("b","乙");
                game.Apply("a",new GameCommand{type="start",bots=false});
                Assert(game.State.roundEvent==null);ChooseAll(game);
                // Retired client/save data must not reactivate the removed central encounter.
                game.State.roundEvent=new ArenaEventState{id="legacy",kind="boss",reward=4};
                foreach(var player in game.State.players)player.ready=true;
                while(game.State.phase=="prep")game.Tick(.05f);
                FinishDeployment(game);
                Assert(game.State.roundEvent==null);Assert(!game.State.fighters.Any(f=>f.neutral));
                Assert(game.State.battlefield.objectives.Count>0);
                Assert(game.SnapshotFor("a").roundEvent==null);
            }
        });
    }
}
