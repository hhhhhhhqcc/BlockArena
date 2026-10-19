using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlockArena.Core;
using BlockArena.Runtime;

internal static partial class Program
{
    private static int passed, failed;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { IncludeFields = true };
    private static void Assert(bool condition, string message = "断言失败") { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Expected " + expected + ", actual " + actual); }
    private static void Near(float expected, float actual, float epsilon = .002f) { Assert(Math.Abs(expected - actual) <= epsilon, "Expected " + expected + ", actual " + actual); }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid command was accepted");
    }
    private static void Check(string name, Action run)
    {
        try { run(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + "\n" + error); }
    }
    private static string Json(GameState state) { return JsonSerializer.Serialize(state, JsonOptions); }
    private static void Advance(ArenaGame game, float seconds)
    {
        int steps = (int)Math.Ceiling(seconds / .05f);
        for (int i = 0; i < steps; i++) game.Tick(.05f);
    }
    private static ArenaGame Two(bool choose = true)
    {
        ArenaGame game = new ArenaGame("a", "玩家甲", 42);
        game.AddPlayer("b", "玩家乙"); game.Apply("a", new GameCommand { type = "start", bots = false });
        if (choose) ChooseAll(game);
        return game;
    }
    private static void ChooseAll(ArenaGame game)
    {
        foreach (PlayerState p in game.State.players)
        {
            if (!p.recruitPending) continue;
            p.shop = new[] { "zombie", "skeleton", "spider" };
            game.Apply(p.id, new GameCommand { type = "choose", index = 0 });
        }
        Equal("prep", game.State.phase);
    }
    private static void Battle(ArenaGame game, bool withEvent = false)
    {
        if (game.State.phase == "recruit") ChooseAll(game);
        if (!withEvent) game.State.roundEvent = new ArenaEventState { kind = "none", round = game.State.round };
        foreach (PlayerState p in game.State.players) if (!p.eliminated) p.ready = true;
        for (int i = 0; i < 30 && game.State.phase == "prep"; i++) game.Tick(.05f);
        Equal("battle", game.State.phase);
    }
    private static ArenaGame Fight(string kindA, string kindB, int countB = 1)
    {
        ArenaGame game = Two();
        game.State.players[0].units = new List<UnitState> { new UnitState { id = "a1", kind = kindA, x = 0, y = 0 } };
        game.State.players[1].units = new List<UnitState>();
        for (int i = 0; i < countB; i++) game.State.players[1].units.Add(new UnitState { id = "b" + i, kind = kindB, x = 20 + i * 8, y = 0 });
        Battle(game);
        foreach (FighterState f in game.State.fighters) f.attackCooldown = f.ownerId == "a" ? 0 : 10;
        return game;
    }
    private static void Timeout(ArenaGame game)
    {
        foreach (FighterState f in game.State.fighters) f.hp = f.ownerId == "a" ? 40 : 10;
        game.State.timeLeft = .001f; game.Tick(.05f);
    }

    private static int Main()
    {
        Check("catalog contains eighty-nine independent species with combat values", () =>
        {
            Equal(89, CreatureCatalog.Kinds.Length);
            foreach (string kind in CreatureCatalog.Kinds)
            {
                CreatureDefinition c = CreatureCatalog.Get(kind);
                Equal(kind, c.id); Assert(c.hp > 0 && c.attack > 0 && c.attackSpeed > 0 && c.range > 0 && c.speed > 0);
            }
            Assert(CreatureCatalog.Get("skeleton").range > CreatureCatalog.Get("zombie").range * 3);
            Assert(CreatureCatalog.Get("spider").speed > CreatureCatalog.Get("zombie").speed);
            Assert(CreatureCatalog.Get("golem").hp > CreatureCatalog.Get("zombie").hp * 2);
            Reject(() => CreatureCatalog.Get("unknown"));
        });
        Check("names preserve Unicode scalar boundaries and discard invalid surrogate code units", () =>
        {
            string expected = new string('甲', 15) + "😀";
            ArenaGame game = new ArenaGame("unicode", expected + "tail");
            Equal(expected, game.State.players[0].name);
            new System.Text.UTF8Encoding(false, true).GetBytes(game.State.players[0].name);
            game.AddPlayer("bad", "\uD800ok\uDC00");
            Equal("ok", game.State.players[1].name);
        });
        Check("lobby validates unique identities, four seats, and host transfer", () =>
        {
            ArenaGame g = new ArenaGame("a", "<甲>\n"); Equal("甲", g.State.players[0].name);
            g.AddPlayer("b", "乙"); g.AddPlayer("c", "丙"); g.AddPlayer("d", "丁");
            Reject(() => g.AddPlayer("e", "戊")); Reject(() => g.AddPlayer("a", "重复"));
            g.RemovePlayer("a"); Equal("b", g.State.hostId); g.AddPlayer("e", "戊");
            Assert(g.State.players.Select(p => p.slot).OrderBy(s => s).SequenceEqual(new[] { 0, 1, 2, 3 }));
        });
        Check("start is host-only and creates twelve gold and paid distinct recruits", () =>
        {
            ArenaGame g = new ArenaGame("a", "甲"); string before = Json(g.State);
            Reject(() => g.Apply("a", new GameCommand { type = "start", bots = false })); Equal(before, Json(g.State));
            g.AddPlayer("b", "乙"); Reject(() => g.Apply("b", new GameCommand { type = "start" }));
            g.Apply("a", new GameCommand { type = "start" }); Equal("recruit", g.State.phase); Equal(1, g.State.round); Near(25, g.State.timeLeft);
            Equal(4, g.State.players.Count); Equal(2, g.State.players.Count(p => p.bot));
            foreach (PlayerState p in g.State.players) { Equal(12, p.gold); Equal(20, p.hp); Equal(2, p.units.Count); Assert(p.recruitPending && p.recruitChoice == null); Equal(3, p.shop.Distinct().Count()); Assert(p.shop.All(k=>CreatureCatalog.Get(k).tier<=2)); }
            Reject(() => g.AddPlayer("late", "迟到"));
        });
        Check("recruit charges its authoritative price exactly once and invalid requests leave state unchanged", () =>
        {
            ArenaGame g = Two(false); PlayerState p = g.State.players[0]; string before = Json(g.State);
            foreach (int index in new[] { -1, 3, Int32.MaxValue }) Reject(() => g.Apply("a", new GameCommand { type = "choose", index = index }));
            Reject(() => g.Apply("stranger", new GameCommand { type = "choose", index = 0 })); Equal(before, Json(g.State));
            string chosen = p.shop[1]; g.Apply("a", new GameCommand { type = "choose", index = 1 });
            Equal(12-CreatureCatalog.Get(chosen).price,p.gold);
            Equal(3, p.units.Count); Equal(chosen, p.units.Last().kind); Equal(chosen, p.recruitChoice); Assert(!p.recruitPending);
            before = Json(g.State); Reject(() => g.Apply("a", new GameCommand { type = "choose", index = 0 })); Equal(before, Json(g.State));
            g.Apply("b", new GameCommand { type = "choose", index = 0 }); Equal("prep", g.State.phase); Near(ArenaGame.PrepSeconds, g.State.timeLeft);
            Reject(() => g.Apply("a", new GameCommand { type = "choose", index = 0 }));
        });
        Check("selected players deploy and ready while unselected players wait", () =>
        {
            ArenaGame g = Two(false); PlayerState a = g.State.players[0];
            Reject(() => g.Apply("a", new GameCommand { type = "ready" }));
            Reject(() => g.Apply("a", new GameCommand { type = "place", unitId = a.units[0].id, y = 220 }));
            g.Apply("a", new GameCommand { type = "choose", index = 0 });
            g.Apply("a", new GameCommand { type = "place", unitId = a.units[0].id, x = 123.456f, y = 222.789f });
            g.Apply("a", new GameCommand { type = "ready" }); Equal("recruit", g.State.phase); Assert(a.ready);
            PlayerState refreshed = g.SnapshotFor("a").players[0]; Equal(a.recruitChoice, refreshed.recruitChoice); Assert(!refreshed.recruitPending); Near(123.456f, refreshed.units[0].x);
            g.Apply("b", new GameCommand { type = "choose", index = 0 }); Assert(a.ready); g.Apply("b", new GameCommand { type = "ready" });
            Advance(g, .4f); Equal("prep", g.State.phase); Advance(g, .75f); Equal("battle", g.State.phase);
        });
        Check("recruit timeout auto-selects once without requiring player input", () =>
        {
            ArenaGame g = Two(false); g.Apply("a", new GameCommand { type = "choose", index = 0 }); string chosen = g.State.players[0].recruitChoice;
            Advance(g, 25.1f); Equal("prep", g.State.phase); Equal(1, g.State.round);
            foreach (PlayerState p in g.State.players) { Assert(!p.recruitPending && p.recruitChoice != null); Equal(3, p.units.Count); }
            Equal(chosen, g.State.players[0].recruitChoice); Assert(g.State.timeLeft > 29);
        });
        Check("paid recruiting retains unlimited rosters and rejects unsupported commands", () =>
        {
            ArenaGame g = Two(false); PlayerState p = g.State.players[0]; p.units.Clear();
            for (int i = 0; i < 500; i++) p.units.Add(new UnitState { id = "existing" + i, kind = "zombie", x = i % 180, y = 220 });
            g.Apply("a", new GameCommand { type = "choose", index = 0 }); Equal(501, p.units.Count); Equal(501, p.units.Select(u => u.id).Distinct().Count());
            foreach (string type in new[] { "buy", "reroll", "lock", "sell" }) { string before = Json(g.State); Reject(() => g.Apply("a", new GameCommand { type = type, index = 0, unitId = p.units[0].id })); Equal(before, Json(g.State)); }
            Assert(p.gold>=0);
        });
        Check("continuous placement rotates to all four sides and rejects foreign or invalid units", () =>
        {
            ArenaGame g = new ArenaGame("a", "甲"); g.Apply("a", new GameCommand { type = "start" }); ChooseAll(g);
            foreach (PlayerState p in g.State.players)
            {
                UnitState u = p.units[0]; Point2 at = ArenaGame.LocalToWorld(p.slot, 123.456f, 222.789f);
                g.Apply(p.id, new GameCommand { type = "place", unitId = u.id, x = at.x, y = at.y }); Near(at.x, u.x); Near(at.y, u.y);
                Point2 local = ArenaGame.WorldToLocal(p.slot, u.x, u.y); Near(123.456f, local.x); Near(222.789f, local.y);
                Point2 outside = ArenaGame.LocalToWorld(p.slot, 186, 220);
                Reject(() => g.Apply(p.id, new GameCommand { type = "place", unitId = u.id, x = outside.x, y = outside.y }));
                Reject(() => g.Apply(p.id, new GameCommand { type = "place", unitId = u.id, x = Single.NaN, y = 220 }));
            }
            Reject(() => g.Apply("a", new GameCommand { type = "place", unitId = g.State.players[1].units[0].id, y = 220 }));
        });
        Check("snapshots hide other shops and deeply isolate all mutable collections", () =>
        {
            ArenaGame g = Two(); GameState s = g.SnapshotFor("a"); Assert(s.players[1].shop == null); Equal(3, s.players[0].shop.Length);
            s.players[0].shop[0] = "fake"; s.players[0].units[0].x = 9999; s.players[0].steve = new Point2(9999, 9999);
            Assert(g.State.players[0].shop[0] != "fake" && g.State.players[0].units[0].x != 9999 && g.State.players[0].steve.x != 9999);
            Battle(g); g.State.effects.Add(new EffectState { id = "test", type = "hit", x = 1 });
            s = g.SnapshotFor("a"); s.fighters[0].hp = -1; s.effects[0].x = 9999;
            Assert(g.State.fighters[0].hp > 0 && g.State.effects[0].x != 9999);
            string json = Json(s); Assert(!json.Contains("attackCooldown") && !json.Contains("moveUntil") && !json.Contains("token"));
        });
        Check("placement IDs acknowledge the latest accepted command without changing rejected state", () =>
        {
            ArenaGame g = Two(); UnitState unit = g.State.players[0].units[0];
            string longestId = new string('a', 64);
            g.Apply("a", new GameCommand { type = "place", unitId = unit.id, x = 21, y = 230, placementId = longestId });
            Equal(longestId, unit.placementId);
            GameState oldSnapshot = g.SnapshotFor("a");
            Equal(longestId, oldSnapshot.players[0].units[0].placementId);
            Equal(longestId, g.SnapshotFor("b").players[0].units[0].placementId);
            foreach (GameCommand invalid in new[] {
                new GameCommand { type = "place", unitId = unit.id, x = 22, y = 231, placementId = new string('b', 65) },
                new GameCommand { type = "place", unitId = unit.id, x = 900, y = 230, placementId = "outside" },
                new GameCommand { type = "place", unitId = unit.id, x = Single.NaN, y = 230, placementId = "nan" },
                new GameCommand { type = "place", unitId = g.State.players[1].units[0].id, x = 0, y = 230, placementId = "foreign" } })
            {
                string before = Json(g.State); Reject(() => g.Apply("a", invalid)); Equal(before, Json(g.State));
            }
            g.Apply("a", new GameCommand { type = "place", unitId = unit.id, x = 71, y = 260, placementId = "second" });
            g.Apply("a", new GameCommand { type = "place", unitId = unit.id, x = 21, y = 230, placementId = "third" });
            UnitState latest = g.SnapshotFor("a").players[0].units[0]; Equal("third", latest.placementId); Near(21, latest.x);
            Equal(longestId, oldSnapshot.players[0].units[0].placementId);
            latest.placementId = "mutated-copy"; Equal("third", unit.placementId);
            g.Apply("a", new GameCommand { type = "place", unitId = unit.id, x = 10, y = 240, placementId = "" });
            Equal("", unit.placementId);
            g.Apply("a", new GameCommand { type = "place", unitId = unit.id, x = 11, y = 241 });
            Equal<string>(null, unit.placementId); Near(11, unit.x);
        });
        Check("released placement remains predicted through delayed authoritative snapshots", () =>
        {
            ArenaGame g = Two(); PlayerState owner = g.SnapshotFor("a").players[0]; UnitState unit = owner.units[0];
            var preview = new PlacementPreview(); Point2 destination = new Point2(123.45f, 222.78f), actual;
            GameCommand command = preview.Submit(unit.id, destination, 10);
            Equal("place", command.type); Equal(unit.id, command.unitId); Near(destination.x, command.x); Near(destination.y, command.y);
            Assert(!string.IsNullOrEmpty(command.placementId) && command.placementId.Length <= 64); Equal(1, preview.Count);
            Assert(!preview.Reconcile(owner, 10.2f)); Assert(preview.TryGet(unit.id, out actual)); Near(destination.x, actual.x); Near(destination.y, actual.y);
            g.Apply("a", command); Assert(!preview.Reconcile(g.SnapshotFor("a").players[0], 10.4f));
            Equal(0, preview.Count); Assert(!preview.TryGet(unit.id, out actual));
        });
        Check("rapid A-B-A placement requires the latest ID even when an old snapshot has matching coordinates", () =>
        {
            ArenaGame g = Two(); UnitState unit = g.State.players[0].units[0]; var preview = new PlacementPreview(); Point2 actual;
            Point2 a = new Point2(20, 230), b = new Point2(80, 280);
            GameCommand first = preview.Submit(unit.id, a, 0); g.Apply("a", first); PlayerState snapshotA = g.SnapshotFor("a").players[0];
            GameCommand second = preview.Submit(unit.id, b, .1f); g.Apply("a", second); PlayerState snapshotB = g.SnapshotFor("a").players[0];
            GameCommand third = preview.Submit(unit.id, a, .2f);
            Equal(3, new[] { first.placementId, second.placementId, third.placementId }.Distinct().Count());
            Assert(!preview.Reconcile(snapshotA, .3f)); Assert(preview.TryGet(unit.id, out actual)); Near(a.x, actual.x);
            Assert(!preview.Reconcile(snapshotB, .4f)); Assert(preview.TryGet(unit.id, out actual)); Near(a.x, actual.x);
            Assert(!preview.Reject(first.placementId) && !preview.Reject(second.placementId)); Equal(1, preview.Count);
            g.Apply("a", third); Assert(!preview.Reconcile(g.SnapshotFor("a").players[0], .5f)); Equal(0, preview.Count);
        });
        Check("multiple units reconcile independently and only a matching rejection removes a pending placement", () =>
        {
            ArenaGame g = Two(); PlayerState owner = g.State.players[0]; var preview = new PlacementPreview(); Point2 actual;
            GameCommand first = preview.Submit(owner.units[0].id, new Point2(20, 230), 0);
            GameCommand replaced = preview.Submit(owner.units[1].id, new Point2(40, 240), .1f);
            GameCommand latest = preview.Submit(owner.units[1].id, new Point2(70, 270), .2f); Equal(2, preview.Count);
            Assert(!preview.Reject(replaced.placementId) && !preview.Reject(null) && !preview.Reject("unrelated")); Equal(2, preview.Count);
            g.Apply("a", first); Assert(!preview.Reconcile(g.SnapshotFor("a").players[0], .3f)); Equal(1, preview.Count);
            Assert(!preview.TryGet(first.unitId, out actual)); Assert(preview.TryGet(latest.unitId, out actual)); Near(70, actual.x);
            Assert(preview.Reject(latest.placementId)); Assert(!preview.Reject(latest.placementId)); Equal(0, preview.Count);
        });
        Check("placement acknowledgement accepts server-adjusted coordinates and expires only unconfirmed units", () =>
        {
            ArenaGame g = Two(); PlayerState owner = g.SnapshotFor("a").players[0]; var preview = new PlacementPreview(); Point2 actual;
            GameCommand command = preview.Submit(owner.units[0].id, new Point2(190, 360), 10);
            owner.units[0].placementId = command.placementId; owner.units[0].x = 185; owner.units[0].y = 355;
            Assert(!preview.Reconcile(owner, 10.1f)); Equal(0, preview.Count);
            preview.Submit(owner.units[0].id, new Point2(20, 230), 20);
            preview.Submit(owner.units[1].id, new Point2(30, 240), 25);
            Assert(!preview.Reconcile(owner, 27.99f)); Equal(2, preview.Count);
            Assert(preview.Reconcile(owner, 28)); Equal(1, preview.Count); Assert(!preview.TryGet(owner.units[0].id, out actual));
            Assert(preview.TryGet(owner.units[1].id, out actual)); Near(30, actual.x);
            Assert(preview.Reconcile(owner, 33)); Equal(0, preview.Count); Assert(!preview.Reconcile(owner, 1000));
        });
        Check("removed units, missing owner and explicit clear discard placement previews", () =>
        {
            ArenaGame g = Two(); PlayerState owner = g.SnapshotFor("a").players[0]; var preview = new PlacementPreview();
            preview.Submit(owner.units[0].id, new Point2(20, 230), 0); preview.Submit(owner.units[1].id, new Point2(30, 240), 0);
            owner.units.RemoveAt(0); Assert(!preview.Reconcile(owner, 1)); Equal(1, preview.Count);
            Assert(!preview.Reconcile(null, 2)); Equal(0, preview.Count);
            GameCommand pending = preview.Submit(owner.units[0].id, new Point2(10, 220), 3);
            preview.Clear(); Equal(0, preview.Count); Assert(!preview.Reject(pending.placementId)); Assert(!preview.Reconcile(owner, 1000));
        });
        Check("fighter snapshots serialize actual runtime stats including split mini-slimes", () =>
        {
            ArenaGame g = Fight("golem", "slime");
            foreach (FighterState f in g.SnapshotFor("a").fighters)
            {
                CreatureDefinition stats = CreatureCatalog.Get(f.kind);
                Near(stats.attack, f.attack); Near(stats.attackSpeed, f.attackSpeed); Near(stats.range, f.range); Near(stats.speed, f.speed);
            }
            g.State.fighters.Find(f => f.kind == "slime").hp = 1; g.Tick(.05f);
            GameState snapshot = JsonSerializer.Deserialize<GameState>(Json(g.SnapshotFor("a")), JsonOptions);
            FighterState[] children = snapshot.fighters.Where(f => f.kind == "slime" && !f.dead).ToArray(); Equal(2, children.Length);
            foreach (FighterState child in children)
            {
                Near(4, child.attack); Near(1.1f, child.attackSpeed); Near(22, child.range); Near(48, child.speed); Near(20, child.maxHp);
            }
            FighterState runtime = g.State.fighters[0]; runtime.attack = 123;
            snapshot = g.SnapshotFor("a"); Near(123, snapshot.fighters[0].attack);
            snapshot.fighters[0].attack = 999; Near(123, runtime.attack);
        });
        Check("Steve movement is normalized, bounded and expires before recruitment", () =>
        {
            ArenaGame g = Two(false); PlayerState p = g.State.players[0];
            g.Apply("a", new GameCommand { type = "move", dx = 1000 }); g.Tick(.05f); Near(6.5f, p.steve.x);
            Advance(g, .6f); Point2 stopped = p.steve; Advance(g, .6f); Near(stopped.x, p.steve.x); Near(stopped.y, p.steve.y);
            for (int i = 0; i < 100; i++) { g.Apply("a", new GameCommand { type = "move", dx = 1, dy = 1 }); g.Tick(.1f); }
            Point2 local = ArenaGame.WorldToLocal(p.slot, p.steve.x, p.steve.y); Assert(local.x <= 220 && local.y <= 400);
            Reject(() => g.Apply("a", new GameCommand { type = "move", dx = Single.PositiveInfinity }));
        });
        Check("disconnect AI recruits, deploys and readies; explicit leave preserves roster", () =>
        {
            ArenaGame g = Two(false); g.SetConnected("b", false); g.Apply("a", new GameCommand { type = "choose", index = 0 }); g.Apply("a", new GameCommand { type = "ready" });
            Advance(g, 5); Equal("battle", g.State.phase); Equal(3, g.State.players[1].units.Count); Assert(!g.State.players[1].bot);
            g.SetConnected("b", true); Assert(g.State.players[1].connected);
            int count = g.State.players[0].units.Count; g.RemovePlayer("a"); Equal(count, g.State.players[0].units.Count); Assert(g.State.players[0].bot && !g.State.players[0].connected);
        });

        Check("ranged attacks damage enemies and expose an animation pulse without hurting Steve", () =>
        {
            ArenaGame g = Fight("skeleton", "zombie"); FighterState a = g.State.fighters.Find(f => f.ownerId == "a"); FighterState b = g.State.fighters.Find(f => f.ownerId == "b");
            b.x = 120; g.Tick(.05f); Assert(b.hp < b.maxHp); Assert(a.attackTime > 0); Assert(g.State.effects.Any(e => e.type == "arrow"));
            Equal(20, g.State.players[0].hp); Equal(20, g.State.players[1].hp); Near(0, a.x);
        });
        Check("spider favors a nearby ranged target and moves rapidly", () =>
        {
            ArenaGame g = Fight("spider", "zombie"); FighterState spider = g.State.fighters.Find(f => f.ownerId == "a");
            FighterState zombie = g.State.fighters.Find(f => f.ownerId == "b"); zombie.x = 100;
            PlayerState enemy = g.State.players[1]; CreatureDefinition stats = CreatureCatalog.Get("skeleton");
            FighterState skeleton = new FighterState { id = "ranged", unitId = "ranged-unit", ownerId = enemy.id, slot = enemy.slot, kind = "skeleton", x = 140, y = 0,
                hp = stats.hp, maxHp = stats.hp, attackCooldown = 10, attack = stats.attack, attackSpeed = stats.attackSpeed, range = stats.range, speed = stats.speed, radius = 15, side = 1 };
            g.State.fighters.Add(skeleton); g.Tick(.05f); Equal(skeleton.id, spider.targetId); Assert(spider.x > 2);
        });
        Check("creeper explosion damages multiple enemies and consumes the creeper", () =>
        {
            ArenaGame g = Fight("creeper", "golem", 2); g.Tick(.05f);
            Assert(g.State.fighters.Find(f => f.ownerId == "a").dead);
            Assert(g.State.fighters.Where(f => f.ownerId == "b").All(f => f.hp < f.maxHp));
            Assert(g.State.effects.Any(e => e.type == "explode"));
        });
        Check("golem deals primary and secondary splash damage", () =>
        {
            ArenaGame g = Fight("golem", "zombie", 2); g.Tick(.05f);
            FighterState[] enemies = g.State.fighters.Where(f => f.ownerId == "b").ToArray();
            Assert(enemies.All(f => f.hp < f.maxHp)); Assert(enemies[0].hp != enemies[1].hp);
        });
        Check("slime splits into two allied children only once", () =>
        {
            ArenaGame g = Fight("golem", "slime"); g.State.fighters.Find(f => f.kind == "slime").hp = 1; g.Tick(.05f);
            FighterState[] children = g.State.fighters.Where(f => f.kind == "slime" && !f.dead).ToArray(); Equal(2, children.Length);
            Assert(children.All(f => f.ownerId == "b" && f.scale < 1)); Equal("battle", g.State.phase);
            foreach (FighterState child in children) child.hp = 1;
            g.State.fighters.Find(f => f.kind == "golem").attackCooldown = 0; g.Tick(.05f);
            Equal(3, g.State.fighters.Count(f => f.kind == "slime")); Equal(0, g.State.fighters.Count(f => f.kind == "slime" && !f.dead));
        });
        Check("overlapping allied armies separate and advance without grid snapping", () =>
        {
            ArenaGame g = Two(); g.State.players[0].units.Clear();
            for (int i = 0; i < 12; i++) g.State.players[0].units.Add(new UnitState { id = "crowd" + i, kind = "zombie", x = 0, y = 200 });
            g.State.players[1].units = new List<UnitState> { new UnitState { id = "target", kind = "golem", x = 0, y = -200 } };
            Battle(g); Advance(g, 1); FighterState[] allies = g.State.fighters.Where(f => f.ownerId == "a").ToArray();
            Assert(allies.Any(f => f.y < 185)); Assert(allies.Select(f => f.x.ToString("F2") + "," + f.y.ToString("F2")).Distinct().Count() > 1);
            Assert(allies.All(f => !Single.IsNaN(f.x) && !Single.IsNaN(f.y)));
        });
        Check("round damage, results, persistent paid recruitment and full-health reset", () =>
        {
            ArenaGame g = Two(); string[] ids = g.State.players[0].units.Select(u => u.id).ToArray(); Battle(g); Timeout(g);
            Equal("results", g.State.phase); Near(ArenaGame.ResultsSeconds, g.State.timeLeft); Equal(1, g.State.round); Equal(20, g.State.players[0].hp); Equal(19, g.State.players[1].hp);
            Equal(0, g.State.placements.Find(p => p.playerId == "a").damage); Equal(1, g.State.placements.Find(p => p.playerId == "b").damage);
            Advance(g, ArenaGame.ResultsSeconds+.1f); Equal("recruit", g.State.phase); Equal(2, g.State.round);
            PlayerState player = g.State.players[0]; Assert(player.recruitPending && player.recruitChoice == null); Assert(player.units.Select(u => u.id).SequenceEqual(ids));
            ChooseAll(g); Equal(ids.Length + 1, player.units.Count); Battle(g); Assert(g.State.fighters.All(f => f.hp == f.maxHp));
        });
        Check("timeout draws cost health and later rounds accelerate damage", () =>
        {
            ArenaGame g = Two(); Battle(g); g.State.round = 9; g.State.timeLeft = .001f; g.Tick(.05f);
            Equal(18, g.State.players[0].hp); Equal(18, g.State.players[1].hp); Assert(g.State.placements.All(p => p.rank == 1 && p.damage == 2));
        });
        Check("eliminated players receive no next-round recruitment", () =>
        {
            ArenaGame g = new ArenaGame("a", "甲"); g.Apply("a", new GameCommand { type = "start" }); ChooseAll(g);
            PlayerState eliminated = g.State.players[1]; eliminated.hp = 1; Battle(g);
            foreach (FighterState f in g.State.fighters) f.hp = (4 - f.slot) * 10;
            g.State.timeLeft = .001f; g.Tick(.05f); Assert(eliminated.eliminated); int count = eliminated.units.Count;
            Advance(g, g.State.timeLeft+.1f); Equal("recruit", g.State.phase); Assert(!eliminated.recruitPending && eliminated.recruitChoice == null); Equal(0, eliminated.shop.Length); Equal(count, eliminated.units.Count);
            Reject(() => g.Apply(eliminated.id, new GameCommand { type = "choose", index = 0 }));
        });
        Check("last surviving player wins; only host can restart into a fresh recruit", () =>
        {
            ArenaGame g = Two(); g.State.players[1].hp = 1; Battle(g); Timeout(g); Equal("finished", g.State.phase); Equal("a", g.State.winnerId);
            Reject(() => g.Apply("b", new GameCommand { type = "move", dx = 1 })); Reject(() => g.Apply("b", new GameCommand { type = "restart" }));
            g.Apply("a", new GameCommand { type = "restart" }); Equal("recruit", g.State.phase); Equal(1, g.State.round);
            Assert(g.State.players.All(p => p.hp == 20 && !p.eliminated && p.recruitPending && p.units.Count == 2));
        });
        EconomyChecks();
        AbilityChecks();
        RitualChecks();
        AntiSnowballChecks();
        Check("seeded four-player AI match completes without hanging", () =>
        {
            ArenaGame g = new ArenaGame("host", "测试", 2026); g.Apply("host", new GameCommand { type = "start" }); g.SetConnected("host", false);
            int ticks = 0; for (; ticks < 40000 && g.State.phase != "finished"; ticks++) g.Tick(.05f);
            Equal("finished", g.State.phase); Assert(g.State.round > 1 && g.State.round < 50); Assert(g.State.players.Count(p => !p.eliminated) <= 1);
            Console.WriteLine("  AI match: round " + g.State.round + ", simulated " + (ticks * .05f) + " s, winner " + (g.State.winnerId ?? "draw"));
        });
        Console.WriteLine("Core checks: " + passed + " passed, " + failed + " failed.");
        return failed == 0 ? 0 : 1;
    }
}
