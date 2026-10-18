using System;
using System.Collections.Generic;
using System.Linq;
using BlockArena.Core;

internal static partial class Program
{
    static void EconomyChecks()
    {
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
        Check("equipment purchase checks ownership, duplicates, gold and three distinct slots atomically", () =>
        {
            var g=Two();var p=g.State.players[0];p.gold=100;var u=p.units[0];
            foreach(string id in new[]{"iron_sword","diamond_armor","swift_boots","spyglass"})p.market.Add(new ShopOffer{id=id,itemId=id,kind="equipment",price=TradeCatalog.Get(id).price});
            string before=Json(g.State);
            Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId="iron_sword",unitId=g.State.players[1].units[0].id}));Equal(before,Json(g.State));
            g.Apply(p.id,new GameCommand{type="purchase",offerId="iron_sword",unitId=u.id});Equal(95,p.gold);Assert(u.equipment.Contains("iron_sword"));
            before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId="iron_sword",unitId=u.id}));Equal(before,Json(g.State));
            foreach(string id in new[]{"diamond_armor","swift_boots"})g.Apply(p.id,new GameCommand{type="purchase",offerId=id,unitId=u.id});
            before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId="spyglass",unitId=u.id}));Equal(before,Json(g.State));
            var fighter=ArenaGame.PreviewFighter(p,u);Assert(fighter.attack>CreatureCatalog.Get(u.kind).attack&&fighter.maxHp>CreatureCatalog.Get(u.kind).hp&&fighter.armor>0);
            Battle(g);var actual=g.State.fighters.First(f=>f.unitId==u.id);Near(fighter.attack,actual.attack);Near(fighter.maxHp,actual.maxHp);Equal(3,actual.equipment.Length);
            before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId="spyglass",unitId=u.id}));Equal(before,Json(g.State));
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
        Check("automatic and paid refreshes create fresh offer IDs and reject obsolete purchases", () =>
        {
            var g=Two();var p=g.State.players[0];p.gold=10;string old=p.market[0].id;
            g.Apply(p.id,new GameCommand{type="refresh"});Equal(8,p.gold);Equal(4,p.market.Count);Assert(p.market.All(o=>o.id!=old));
            string before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="purchase",offerId=old,unitId=p.units[0].id}));Equal(before,Json(g.State));
            old=p.market[0].id;Advance(g,TradeCatalog.RefreshSeconds+.1f);Assert(p.market.All(o=>o.id!=old));Equal(8,p.gold);
            p.gold=1;before=Json(g.State);Reject(()=>g.Apply(p.id,new GameCommand{type="refresh"}));Equal(before,Json(g.State));
        });
        Check("income itemizes base, real kills and army survival rank exactly once", () =>
        {
            var g=Two();var a=g.State.players[0];var b=g.State.players[1];a.roundKills=3;b.roundKills=1;
            Battle(g);a.roundKills=3;b.roundKills=1;int goldA=a.gold,goldB=b.gold;Timeout(g);
            Equal(13,a.lastIncome);Equal(10,b.lastIncome);Equal(goldA+13,a.gold);Equal(goldB+10,b.gold);
            var result=g.State.placements.First(p=>p.playerId==a.id);Equal(3,result.kills);Equal(6,result.baseIncome);Equal(4,result.rankIncome);Equal(13,result.income);
            Advance(g,1);Equal(goldA+13,a.gold);Equal(goldB+10,b.gold);
        });
        Check("splitting and summoned creatures do not multiply kill income", () =>
        {
            var g=Fight("golem","slime");var attacker=g.State.fighters.First(f=>f.ownerId=="a");attacker.attack=10000;
            g.State.fighters.First(f=>f.kind=="slime").hp=1;g.Tick(.05f);Equal(1,g.State.players[0].roundKills);
            attacker.attackCooldown=0;foreach(var f in g.State.fighters.Where(f=>!f.dead&&f.ownerId=="b")){f.hp=1;f.x=20;f.y=0;}
            g.Tick(.05f);Equal(1,g.State.players[0].roundKills);Equal(1,g.State.players[0].totalKills);
        });
        Check("private markets, equipment and skill collections are isolated in snapshots", () =>
        {
            var g=Two();var p=g.State.players[0];p.units[0].equipment.Add("iron_sword");p.upgrades.Add("undead_legion");
            var s=g.SnapshotFor("a");Equal(0,s.players[1].market.Count);Equal(4,s.players[0].market.Count);
            s.players[0].market[0].sold=true;s.players[0].upgrades.Clear();s.players[0].units[0].equipment.Clear();
            Assert(!p.market[0].sold&&p.upgrades.Count==1&&p.units[0].equipment.Count==1);
            Battle(g);s=g.SnapshotFor("a");s.fighters[0].skills[0]="fake";s.fighters[0].equipment[0]="fake";
            Assert(g.State.fighters[0].skills[0]!="fake"&&g.State.fighters[0].equipment[0]!="fake");
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
