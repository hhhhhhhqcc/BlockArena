using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BlockArena.Core;

internal static partial class Program
{
    static void EconomyChecks()
    {
        Check("fresh rooms use independent random seeds while explicit seeds reproduce recruitment and markets", () =>
        {
            var seeds = new HashSet<int>(); var choices = new HashSet<string>();
            for (int i = 0; i < 24; i++)
            {
                var game = new ArenaGame("a", "甲"); game.Apply("a", new GameCommand { type = "start" });
                seeds.Add(game.Seed); choices.Add(string.Join(",", game.State.players[0].shop));
            }
            Assert(seeds.Count > 1, "Default rooms reused one fixed seed.");
            Assert(choices.Count > 1, "Independent first rounds repeated the same three choices.");
            foreach (int seed in new[] { 0, 42, -19, Int32.MaxValue })
            {
                var first = new ArenaGame("a", "甲", seed); var second = new ArenaGame("a", "甲", seed);
                first.Apply("a", new GameCommand { type = "start" }); second.Apply("a", new GameCommand { type = "start" });
                Equal(seed, first.Seed); Equal(Json(first.State), Json(second.State));
                first.Apply("a", new GameCommand { type = "skip" }); second.Apply("a", new GameCommand { type = "skip" });
                first.Apply("a", new GameCommand { type = "refresh" }); second.Apply("a", new GameCommand { type = "refresh" });
                Equal(Json(first.State), Json(second.State));
            }
        });
        Check("all species have distinct identities, executable skills, prices and visual archetypes", () =>
        {
            Equal(89,CreatureCatalog.Kinds.Distinct().Count());
            foreach(string kind in CreatureCatalog.Kinds)
            {
                var c=CreatureCatalog.Get(kind);Equal(kind,c.id);
                Assert(c.price>0&&c.tier>=1&&c.tier<=6&&c.visualScale>0&&!string.IsNullOrEmpty(c.model),kind);
                Assert(c.skills.Length>=1&&c.skills.Length<=3,kind+" skills");
                foreach(string skill in c.skills){var a=CreatureAbilities.Get(skill);Assert(!string.IsNullOrEmpty(a.effect)&&!string.IsNullOrEmpty(a.description),skill);}
            }
            foreach(string boss in new[]{"ender_dragon","wither"}){var c=CreatureCatalog.Get(boss);Assert(c.price>=55&&c.tier==6);}
            Equal("iron_golem",CreatureCatalog.Get("golem").id);
        });
        Check("insufficient gold cannot recruit and skipping never spends or auto-buys", () =>
        {
            var g=Two(false);var p=g.State.players[0];p.gold=0;string before=Json(g.State);
            Reject(()=>g.Apply(p.id,new GameCommand{type="choose",index=0}));Equal(before,Json(g.State));
            g.Apply(p.id,new GameCommand{type="skip"});Equal(0,p.gold);Equal(2,p.units.Count);Assert(!p.recruitPending&&p.recruitChoice==null);
            g.State.players[1].gold=0;Advance(g,25.2f);Equal("prep",g.State.phase);Equal(2,g.State.players[1].units.Count);
        });
        Check("skill-only purchases need no creature selection and reject invalid offers without charging", () =>
        {
            var g=Two();var p=g.State.players[0];p.gold=100;
            Assert(TradeCatalog.Ids.All(id=>TradeCatalog.Get(id).type=="skill")); Reject(()=>TradeCatalog.Get("iron_sword"));
            Equal(4,p.market.Count);Equal(4,p.market.Select(o=>o.itemId).Distinct().Count());Assert(p.market.All(o=>o.kind=="skill"));
            var offer=p.market[0];g.Apply(p.id,new GameCommand{type="purchase",offerId=offer.id});
            Equal(100-TradeCatalog.Get(offer.itemId).price,p.gold);Assert(p.upgrades.Contains(offer.itemId));Equal(3,p.market.Count);
            Assert(p.units.All(u=>u.equipment.Count==0));
            string before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId=offer.id}));Equal(before,Json(g.State));
            p.market.Add(new ShopOffer{id="duplicate",itemId=offer.itemId,kind="skill",price=1});
            before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId="duplicate"}));Equal(before,Json(g.State));
            p.market.Add(new ShopOffer{id="retired-equipment",itemId="iron_sword",kind="equipment",price=1});
            before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId="retired-equipment",unitId=p.units[0].id}));Equal(before,Json(g.State));
            var remaining=p.market.First(o=>o.kind=="skill"&&!p.upgrades.Contains(o.itemId));p.gold=0;
            before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId=remaining.id}));Equal(before,Json(g.State));
        });
        Check("retired equipment never affects previews, real combat or snapshots", () =>
        {
            var g=Two();var p=g.State.players[0];var unit=p.units[0];var baseline=ArenaGame.PreviewFighter(p,unit);
            unit.equipment.AddRange(new[]{"iron_sword","diamond_armor","unknown-legacy-item"});
            var preview=ArenaGame.PreviewFighter(p,unit);Near(baseline.maxHp,preview.maxHp);Near(baseline.attack,preview.attack);Near(baseline.armor,preview.armor);
            Equal(0,preview.equipment.Length);Equal(0,g.SnapshotFor(p.id).players[0].units[0].equipment.Count);
            Battle(g);var actual=g.State.fighters.First(f=>f.unitId==unit.id);
            Near(baseline.maxHp,actual.maxHp);Near(baseline.attack,actual.attack);Equal(0,actual.equipment.Length);
            Equal(0,g.SnapshotFor(p.id).fighters.First(f=>f.unitId==unit.id).equipment.Length);
        });
        Check("remaining skill pools shrink without duplicates and exhausted refreshes never spend gold", () =>
        {
            var g=Two();var p=g.State.players[0];p.gold=1000;
            while(TradeCatalog.AvailableSkills(p).Length>0)
            {
                Assert(p.market.Count>0&&p.market.Count<=TradeCatalog.MarketSlots);
                Equal(p.market.Count,p.market.Select(o=>o.itemId).Distinct().Count());
                Assert(p.market.All(o=>o.kind=="skill"&&!p.upgrades.Contains(o.itemId)));
                foreach(var offer in p.market.ToArray())g.Apply(p.id,new GameCommand{type="purchase",offerId=offer.id});
                Equal(0,p.market.Count);
                if(TradeCatalog.AvailableSkills(p).Length>0)
                {
                    int gold=p.gold;g.Apply(p.id,new GameCommand{type="refresh"});Equal(gold-TradeCatalog.RefreshPrice,p.gold);
                    Equal(Math.Min(TradeCatalog.MarketSlots,TradeCatalog.AvailableSkills(p).Length),p.market.Count);
                }
            }
            Equal(TradeCatalog.Ids.Length,p.upgrades.Count);Equal(p.upgrades.Count,p.upgrades.Distinct().Count());
            string before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="refresh"}));Equal(before,Json(g.State));
            p.gold=0;before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="refresh"}));Equal(before,Json(g.State));
            Battle(g);Timeout(g);int earned=p.gold;Advance(g,ArenaGame.ResultsSeconds+.1f);
            Equal("recruit",g.State.phase);Equal(0,p.market.Count);Equal(earned,p.gold);
        });
        Check("permanent category and species skills apply only to matching current and future recruits", () =>
        {
            var g=Two();var p=g.State.players[0];p.gold=100;
            foreach(string id in new[]{"undead_legion","zombie_resilience"})
            {p.market.Add(new ShopOffer{id=id,itemId=id,kind="skill",price=TradeCatalog.Get(id).price});g.Apply(p.id,new GameCommand{type="purchase",offerId=id});}
            var zombie=ArenaGame.PreviewFighter(p,p.units[0]);var skeleton=ArenaGame.PreviewFighter(p,p.units[1]);
            Assert(zombie.maxHp>CreatureCatalog.Get("zombie").hp+25);Assert(skeleton.maxHp>=CreatureCatalog.Get("skeleton").hp+25);
            Assert(zombie.skills.Contains("lifesteal")&&zombie.skills.Contains("thorns"));
            var future=ArenaGame.PreviewFighter(p,new UnitState{id="future",kind="zombie"});Near(zombie.maxHp,future.maxHp);
            var unrelated=ArenaGame.PreviewFighter(p,new UnitState{id="future-cow",kind="cow"});Near(CreatureCatalog.Get("cow").hp,unrelated.maxHp);
            string before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId="undead_legion"}));Equal(before,Json(g.State));
        });
        Check("paid refreshes replace offers once and never trigger a later automatic refresh", () =>
        {
            var g=Two();var p=g.State.players[0];p.gold=10;string old=p.market[0].id;
            g.Apply(p.id,new GameCommand{type="refresh"});Equal(8,p.gold);Equal(4,p.market.Count);Assert(p.market.All(o=>o.id!=old));
            string before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId=old,unitId=p.units[0].id}));Equal(before,Json(g.State));
            string offers=JsonSerializer.Serialize(p.market,JsonOptions);
            Advance(g,25);Equal("prep",g.State.phase);Equal(offers,JsonSerializer.Serialize(p.market,JsonOptions));Equal(8,p.gold);Near(0,p.shopRefreshLeft);
            p.gold=1;before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="refresh"}));Equal(before,Json(g.State));
        });
        Check("offers stay unchanged throughout recruitment waiting and the entire preparation phase", () =>
        {
            var g=Two(false);var p=g.State.players[0];g.Apply(p.id,new GameCommand{type="choose",index=0});
            string offers=JsonSerializer.Serialize(p.market,JsonOptions);int gold=p.gold;
            Advance(g,24);Equal("recruit",g.State.phase);Equal(offers,JsonSerializer.Serialize(p.market,JsonOptions));
            g.Apply("b",new GameCommand{type="choose",index=0});Equal("prep",g.State.phase);
            Advance(g,ArenaGame.PrepSeconds-.2f);Equal("prep",g.State.phase);Equal(offers,JsonSerializer.Serialize(p.market,JsonOptions));
            Advance(g,.4f);Equal("battle",g.State.phase);Equal(offers,JsonSerializer.Serialize(p.market,JsonOptions));Equal(gold,p.gold);Near(0,p.shopRefreshLeft);
        });
        Check("each new round replaces offers without charging a refresh fee", () =>
        {
            var g=Two();var p=g.State.players[0];string[] offers=p.market.Select(o=>o.id).ToArray();
            Battle(g);Timeout(g);int gold=p.gold;Advance(g,ArenaGame.ResultsSeconds+.1f);
            Equal("recruit",g.State.phase);Equal(4,p.market.Count);Assert(p.market.All(o=>!offers.Contains(o.id)));Equal(gold,p.gold);Near(0,p.shopRefreshLeft);
        });
        Check("real kills pay immediately and settlement pays only base and rank while itemizing the full round", () =>
        {
            var g=Fight("skeleton","cow",2);var a=g.State.players[0];var b=g.State.players[1];
            var attacker=g.State.fighters.First(f=>f.ownerId=="a");var targets=g.State.fighters.Where(f=>f.ownerId=="b").ToArray();
            int goldA=a.gold,goldB=b.gold;targets[0].hp=1;g.Tick(.05f);attacker.attackCooldown=1000;
            Equal("battle",g.State.phase);Assert(targets[0].dead);Equal(goldA+1,a.gold);Equal(1,a.roundKills);Equal(goldA+1,g.SnapshotFor("a").players[0].gold);
            Advance(g,.2f);Equal(goldA+1,a.gold);
            targets[1].hp=1;attacker.attackCooldown=0;g.Tick(.05f);
            Equal("results",g.State.phase);Equal(2,a.roundKills);Equal(2,a.killIncome);Equal(12,a.lastIncome);Equal(9,b.lastIncome);
            Equal(goldA+12,a.gold);Equal(goldB+9,b.gold);
            var result=g.State.placements.First(p=>p.playerId==a.id);Equal(2,result.kills);Equal(6,result.baseIncome);Equal(4,result.rankIncome);Equal(2,result.killIncome);Equal(12,result.income);
            Advance(g,1);Equal(goldA+12,a.gold);Equal(goldB+9,b.gold);
        });
        Check("splitting and summoned creatures do not multiply kill income", () =>
        {
            var g=Fight("golem","slime");var attacker=g.State.fighters.First(f=>f.ownerId=="a");attacker.attack=10000;
            var reserve=ArenaGame.PreviewFighter(g.State.players[1],new UnitState{id="slime-reserve",kind="cow",x=300,y=300});reserve.attackCooldown=1000;reserve.speed=0;g.State.fighters.Add(reserve);
            int gold=g.State.players[0].gold;
            g.State.fighters.First(f=>f.kind=="slime").hp=1;g.Tick(.05f);Equal(1,g.State.players[0].roundKills);
            Equal("battle",g.State.phase);Equal(gold+1,g.State.players[0].gold);
            attacker.attackCooldown=0;foreach(var f in g.State.fighters.Where(f=>!f.dead&&f.kind=="slime")){f.hp=1;f.x=20;f.y=0;}
            g.Tick(.05f);Equal(1,g.State.players[0].roundKills);Equal(1,g.State.players[0].totalKills);
            Equal(gold+1,g.State.players[0].gold);
        });
        Check("summoned attackers credit their owner immediately when killing an original enemy", () =>
        {
            var g=Fight("evoker","cow",2);var caster=g.State.fighters.First(f=>f.ownerId=="a");
            g.Tick(.05f);caster.attackCooldown=1000;
            var vexes=g.State.fighters.Where(f=>f.kind=="vex").ToArray();Equal(2,vexes.Length);
            foreach(var f in g.State.fighters){f.speed=0;f.attackCooldown=1000;}
            var victim=g.State.fighters.First(f=>f.ownerId=="b");victim.hp=1;vexes[0].x=victim.x-10;vexes[0].y=victim.y;vexes[0].attackCooldown=0;
            int gold=g.State.players[0].gold;g.Tick(.05f);
            Assert(victim.dead);Equal("battle",g.State.phase);Equal(1,g.State.players[0].roundKills);Equal(gold+1,g.State.players[0].gold);
        });
        Check("private markets and permanent skills are isolated while legacy equipment stays empty", () =>
        {
            var g=Two();var p=g.State.players[0];p.units[0].equipment.Add("iron_sword");p.upgrades.Add("undead_legion");
            var s=g.SnapshotFor("a");Equal(0,s.players[1].market.Count);Equal(4,s.players[0].market.Count);
            Equal(0,s.players[0].units[0].equipment.Count);
            s.players[0].market[0].sold=true;s.players[0].upgrades.Clear();s.players[0].units[0].equipment.Add("fake");
            Assert(!p.market[0].sold&&p.upgrades.Count==1&&p.units[0].equipment.Count==1);
            Battle(g);s=g.SnapshotFor("a");s.fighters[0].skills[0]="fake";Equal(0,s.fighters[0].equipment.Length);
            Assert(g.State.fighters[0].skills[0]!="fake"&&g.State.fighters[0].equipment.Length==0);
        });
        Check("every catalog species enters real combat and executes without invalid numeric state", () =>
        {
            foreach(string kind in CreatureCatalog.Kinds)
            {
                var g=Fight(kind,"iron_golem");bool active=false;
                for(int i=0;i<40;i++){g.Tick(.05f);active|=g.State.effects.Count>0;}
                Assert(g.State.fighters.All(f=>!float.IsNaN(f.hp)&&!float.IsNaN(f.x)&&!float.IsNaN(f.y)&&f.hp>=0),kind);
                Assert(active,kind+" produced no combat activity");
            }
        });
    }
}
