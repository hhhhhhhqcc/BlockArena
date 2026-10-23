using System;
using System.Linq;
using System.Text.Json;
using BlockArena.Core;

internal static partial class Program
{
    static void VisionChecks()
    {
        Check("shared army sight uses consistent clear, fog and sandstorm radii",()=>{
            var world=BattlefieldCatalog.CreateExpanded("bog","clear",31);
            var source=new FighterState{kind="zombie",ownerId="a"};
            Near(550,BattlefieldVision.Range(world,source));
            world.weather="fog";Near(385,BattlefieldVision.Range(world,source));
            world.weather="sandstorm";Near(440,BattlefieldVision.Range(world,source));
            source.kind="bat";
            Assert(BattlefieldCatalog.Scout(source));Near(572,BattlefieldVision.Range(world,source));
        });
        Check("watchtower expands only its owner's sight and retains concealment rules",()=>{
            var world=BattlefieldCatalog.CreateExpanded("bog","fog",32);
            var source=new FighterState{kind="zombie",ownerId="a"};
            var target=new FighterState{kind="zombie",ownerId="b",x=450,hidden=true};
            Assert(!BattlefieldVision.CanSee(world,source,target));
            world.objectives.Find(o=>o.kind=="tower").ownerId="b";
            Assert(!BattlefieldVision.CanSee(world,source,target));
            world.objectives.Find(o=>o.kind=="tower").ownerId="a";
            Assert(BattlefieldVision.CanSee(world,source,target));
            Near(500.5f,BattlefieldVision.Range(world,source));
            target.x=502;Assert(!BattlefieldVision.CanSee(world,source,target));
        });
        Check("ordinary distant enemies and their effects stay visible without changing AI sight",()=>{
            ArenaGame game=VisionFixture();
            var first=game.State.fighters[0];var enemy=game.State.fighters[1];
            game.State.effects.Add(new EffectState{id="hidden-attack",type="hit",sourceId=enemy.id,targetId=enemy.id});
            var snapshot=game.SnapshotFor("a");
            Assert(snapshot.fighters.Any(f=>f.ownerId=="b"));Equal(1,snapshot.effects.Count);
            Assert(!BattlefieldVision.CanSee(game.State.battlefield,first,enemy));
            enemy.hidden=true;
            snapshot=game.SnapshotFor("a");
            Assert(snapshot.fighters.All(f=>f.ownerId=="a"));Equal(0,snapshot.effects.Count);
            game.State.fighters.Add(new FighterState{id="ally-forward",kind="zombie",ownerId="a",x=500,hp=40,maxHp=40});
            snapshot=game.SnapshotFor("a");
            Assert(snapshot.fighters.Any(f=>f.id==enemy.id));Equal(1,snapshot.effects.Count);
            game.State.fighters.Last().dead=true;
            Assert(game.SnapshotFor("a").fighters.All(f=>f.ownerId=="a"));
        });
        Check("defeated and eliminated commanders retain the existing concealed-enemy spectator rules",()=>{
            var game=VisionFixture();game.State.fighters[1].hidden=true;game.State.fighters[0].dead=true;
            Assert(game.SnapshotFor("a").fighters.Any(f=>f.ownerId=="b"));
            game.State.fighters[0].dead=false;game.State.players[0].eliminated=true;
            Assert(game.SnapshotFor("a").fighters.Any(f=>f.ownerId=="b"));
            game.State.players[0].eliminated=false;game.State.phase="tactics";
            Assert(game.SnapshotFor("a").fighters.Any(f=>f.ownerId=="b"));
        });
        Check("concealment does not reveal hidden enemies merely because terrain is in sight",()=>{
            var world=BattlefieldCatalog.CreateExpanded("bog","fog",33);
            var source=new FighterState{kind="zombie",ownerId="a"};
            var target=new FighterState{kind="zombie",ownerId="b",x=100,hidden=true};
            Assert(!BattlefieldVision.CanSee(world,source,target));
            target.x=60;Assert(BattlefieldVision.CanSee(world,source,target));
            target.hidden=false;target.x=380;Assert(BattlefieldVision.CanSee(world,source,target));
        });
        Check("effective movement speed survives snapshot and JSON including slows and roots",()=>{
            var game=VisionFixture();var fighter=game.State.fighters[0];
            fighter.speed=68;fighter.slowMultiplier=.4f;fighter.slowTime=3;
            var snapshot=game.SnapshotFor("a");
            var decoded=JsonSerializer.Deserialize<GameState>(Json(snapshot),JsonOptions);
            Near(27.2f,BattlefieldVision.MovementSpeed(decoded.fighters[0]));
            fighter.rootTime=2;Near(0,BattlefieldVision.MovementSpeed(game.SnapshotFor("a").fighters[0]));
            fighter.rootTime=0;fighter.stunTime=1;Near(0,BattlefieldVision.MovementSpeed(fighter));
            fighter.stunTime=0;fighter.dead=true;Near(0,BattlefieldVision.MovementSpeed(fighter));
            Near(.4f,fighter.slowMultiplier);
        });
        Check("forest and smoke provenance stack while attacks expose for the same two host seconds",()=>{
            var game=WorldBattle();var source=game.State.fighters[0];var enemy=game.State.fighters[1];
            var forest=game.State.battlefield.terrain.First(t=>t.kind=="forest");
            source.x=forest.x;source.y=forest.y;
            game.State.battlefield.zones.Add(new CommandZoneState{id="smoke",kind="smoke",ownerId="a",x=source.x,y=source.y,radius=100,remaining=10});
            WorldTick(game);Assert(source.hidden);Equal(3,source.concealmentSources);
            WorldCall(game,"Attack",source,enemy);Assert(!source.hidden);
            Near(2,game.SnapshotFor("a").fighters.First(f=>f.id==source.id).attackRevealRemaining);
            WorldTick(game,.6f);Assert(!source.hidden);
            var snapshot=JsonSerializer.Deserialize<GameState>(Json(game.SnapshotFor("a")),JsonOptions);
            var sent=snapshot.fighters.First(f=>f.id==source.id);
            Equal(3,sent.concealmentSources);Near(1.4f,sent.attackRevealRemaining,.001f);
            WorldTick(game,1.3f);Assert(!source.hidden);WorldTick(game,.11f);Assert(source.hidden);
            Near(0,game.SnapshotFor("a").fighters.First(f=>f.id==source.id).attackRevealRemaining);
        });
        Check("exposure expiration cannot conceal a unit after it leaves cover",()=>{
            var game=WorldBattle();var source=game.State.fighters[0];var enemy=game.State.fighters[1];
            source.x=0;source.y=350;WorldCall(game,"Attack",source,enemy);
            WorldTick(game,2.1f);Assert(!source.hidden);Equal(0,source.concealmentSources);
            Near(0,game.SnapshotFor("a").fighters.First(f=>f.id==source.id).attackRevealRemaining);
        });
        Check("ambush provenance does not mislabel a non-attack reveal as attack exposure",()=>{
            var game=WorldBattle();var source=game.State.fighters[0];
            source.x=0;source.y=350;
            var plan=new TacticPlanState{id="ambush",unitIds=new[]{source.unitId}};
            plan.nodes.Add(new TacticNode{x=source.x,y=source.y,action="ambush",condition="always",seconds=10,threshold=35});
            game.State.players[0].tactics.Add(plan);WorldCall(game,"PrepareBattlePlans");
            object[] arguments={source,.05f,new Point2(),false};WorldTick(game);
            WorldCall(game,"TacticDestination",arguments);Assert(source.hidden);Equal(4,source.concealmentSources);
            source.tacticWait=11;WorldCall(game,"TacticDestination",arguments);Assert(!source.hidden);
            Near(0,game.SnapshotFor("a").fighters.First(f=>f.id==source.id).attackRevealRemaining);
        });
        Check("hidden enemies do not leak through effects or tracking focus zones",()=>{
            var game=VisionFixture();var enemy=game.State.fighters[1];enemy.hidden=true;
            game.State.effects.Add(new EffectState{id="hidden",type="poison",sourceId=enemy.id,x=enemy.x,y=enemy.y});
            game.State.battlefield.zones.Add(new CommandZoneState{id="focus",kind="focus",ownerId="a",targetId=enemy.id,x=enemy.x,y=enemy.y});
            game.State.battlefield.zones.Add(new CommandZoneState{id="smoke",kind="smoke",ownerId="b",x=enemy.x,y=enemy.y});
            var snapshot=game.SnapshotFor("a");Assert(snapshot.fighters.All(f=>f.ownerId=="a"));
            Equal(0,snapshot.effects.Count);Assert(snapshot.battlefield.zones.All(z=>z.kind!="focus"));
            Assert(snapshot.battlefield.zones.Any(z=>z.kind=="smoke"));
            game.State.fighters[0].x=enemy.x-60;
            snapshot=game.SnapshotFor("a");Assert(snapshot.fighters.Any(f=>f.id==enemy.id));
            Equal(1,snapshot.effects.Count);Assert(snapshot.battlefield.zones.Any(z=>z.kind=="focus"));
        });
    }
    static ArenaGame VisionFixture()
    {
        var game=new ArenaGame("a","视野甲",38);game.AddPlayer("b","视野乙");
        game.State.phase="battle";game.State.battlefield=BattlefieldCatalog.CreateExpanded("courtyards","clear",38);
        game.State.fighters.Add(new FighterState{id="ally",ownerId="a",kind="zombie",x=-600,y=0,hp=40,maxHp=40,speed=40});
        game.State.fighters.Add(new FighterState{id="enemy",ownerId="b",kind="zombie",x=600,y=0,hp=40,maxHp=40});
        return game;
    }
}
