using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlockArena.Core;

internal static partial class Program
{
    static object WorldCall(ArenaGame game,string name,params object[] args)
    {return typeof(ArenaGame).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(game,args);}
    static ArenaGame WorldBattle(string map="bog",string weather="clear",string kindA="cow",string kindB="cow")
    {
        var game=new ArenaGame("a","甲",91);game.AddPlayer("b","乙");
        game.State.phase="battle";game.State.round=1;game.State.timeLeft=55;
        game.State.battlefield=BattlefieldCatalog.Create(map,weather,91);
        for(int i=0;i<2;i++){
            var p=game.State.players[i];p.steve=new Point2(0,0);
            var f=ArenaGame.PreviewFighter(p,new UnitState{id="world-"+p.id,kind=i==0?kindA:kindB,x=i==0?350:-350,y=350});
            f.attackCooldown=1000;game.State.fighters.Add(f);
        }
        WorldCall(game,"StartBattlefieldCombat");return game;
    }
    static void WorldTick(ArenaGame game,float dt=.05f){WorldCall(game,"TickBattlefield",dt);}
    static void WorldDamage(ArenaGame game,FighterState target,float damage,FighterState source)
    {WorldCall(game,"Damage",target,damage,source);}

    private static void WorldRulesChecks()
    {
        Check("commander selection is available every round before ready but cannot reset charges or cooldown",()=>{
            var game=Two(false);var p=game.State.players[0];game.State.round=4;
            p.commanderUses=0;p.commanderCooldown=5;
            foreach(string phase in new[]{"recruit","prep","tactics"}){
                game.State.phase=phase;p.ready=false;
                game.Apply("a",new GameCommand{type="commander",skillId="ward"});Equal("ward",p.commanderSkill);Equal(0,p.commanderUses);Near(5,p.commanderCooldown);
                game.Apply("a",new GameCommand{type="commander",skillId="cleanse"});Equal("cleanse",p.commanderSkill);Equal(0,p.commanderUses);Near(5,p.commanderCooldown);
                p.ready=true;string before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="commander",skillId="drums"}));Equal(before,Json(game.State));
                p.ready=false;before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="commander",skillId="invalid"}));Equal(before,Json(game.State));
                p.eliminated=true;before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="commander",skillId="drums"}));Equal(before,Json(game.State));p.eliminated=false;
            }
            foreach(string phase in new[]{"reveal","battle","results","finished"}){
                game.State.phase=phase;string before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="commander",skillId="drums"}));Equal(before,Json(game.State));
            }
            game.State.phase="tactics";game.State.battlefield=BattlefieldCatalog.Create("bog","clear",4);
            game.Apply("a",new GameCommand{type="commander",skillId="trap"});WorldCall(game,"BeginBattle");
            Equal("trap",p.commanderSkill);Equal(2,p.commanderUses);Near(0,p.commanderCooldown);
        });
        Check("three battlefields expose objectives, seven weather rules and eight commander skills",()=>{
            Equal(3,BattlefieldCatalog.Maps.Length);Equal(7,BattlefieldCatalog.Weathers.Length);Equal(8,BattlefieldCatalog.Commanders.Length);
            foreach(string id in BattlefieldCatalog.Maps){
                var def=BattlefieldCatalog.GetMap(id);var world=BattlefieldCatalog.Create(id,def.weathers[0],19);
                Assert(world.terrain.Count>=5&&world.objectives.Count>0);Assert(world.objectives.All(o=>o.radius>0));
            }
            foreach(var id in BattlefieldCatalog.Weathers)Assert(BattlefieldCatalog.GetWeather(id).description.Length>5);
            foreach(var id in BattlefieldCatalog.Commanders)Assert(BattlefieldCatalog.GetCommander(id).duration>0);
            Reject(()=>BattlefieldCatalog.GetMap("bad"));Reject(()=>BattlefieldCatalog.GetWeather("bad"));Reject(()=>BattlefieldCatalog.GetCommander("bad"));
        });
        Check("world selection is host-only, validates weather and allows all seven manual overrides",()=>{
            var game=new ArenaGame("a","甲");game.AddPlayer("b","乙");
            string before=Json(game.State);
            Reject(()=>game.Apply("b",new GameCommand{type="world_options",mapId="bog",weather="rain"}));
            Reject(()=>game.Apply("a",new GameCommand{type="world_options",mapId="bog",weather="bad"}));
            Equal(before,Json(game.State));
            game.Apply("a",new GameCommand{type="world_options",mapId="bog",weather="fog"});Equal("bog",game.State.preferredMap);
            game.Apply("a",new GameCommand{type="world_options",mapId="bog",weather="sandstorm"});Equal("sandstorm",game.State.preferredWeather);
            game.Apply("a",new GameCommand{type="commander",skillId="rift"});Equal("rift",game.State.players[0].commanderSkill);
            game.State.round=2;game.State.phase="prep";game.Apply("a",new GameCommand{type="commander",skillId="trap"});Equal("trap",game.State.players[0].commanderSkill);
        });
        Check("weather and terrain modifiers recompute without compounding; flight avoids wetland",()=>{
            var game=WorldBattle("bog","rain","skeleton","dolphin");var archer=game.State.fighters[0];var swimmer=game.State.fighters[1];
            float range=archer.range,speed=swimmer.speed;swimmer.x=-170;swimmer.y=25;
            WorldTick(game);Near(range*.9f,archer.range);Near(speed*1.1f*1.15f,swimmer.speed);
            for(int i=0;i<30;i++)WorldTick(game);Near(range*.9f,archer.range);Near(speed*1.1f*1.15f,swimmer.speed);
            var bat=ArenaGame.PreviewFighter(game.State.players[0],new UnitState{id="bat",kind="bat",x=-170,y=25});float flight=bat.speed;
            game.State.fighters.Add(bat);WorldTick(game);Near(flight,bat.speed);
            var ground=ArenaGame.PreviewFighter(game.State.players[0],new UnitState{id="ground",kind="cow",x=-170,y=25});float walking=ground.speed;
            game.State.fighters.Add(ground);WorldTick(game);Near(walking*.8f,ground.speed);
            game.State.battlefield.weather="heat";WorldTick(game);Near(walking,ground.speed);Near(.8f,ground.healingMultiplier);
        });
        Check("rain changes direct fire and burning exactly once; heat reduces healing and snow extends control",()=>{
            var game=WorldBattle("bog","rain","blaze");var fire=game.State.fighters[0];var target=game.State.fighters[1];
            target.maxHp=target.hp=1000;target.baseArmor=target.armor=0;target.shield=0;WorldTick(game);
            WorldDamage(game,target,100,fire);Near(930,target.hp);
            target.burnTime=.5f;target.burnDps=10;target.burnSource=fire;
            CreatureAbilities.Tick(target,game.State.fighters,.5f,(f,damage,source)=>WorldDamage(game,f,damage,source),null);Near(926.5f,target.hp);
            Assert(!CreatureAbilities.PeriodicDamage);
            game.State.battlefield.weather="heat";WorldTick(game);WorldDamage(game,target,100,fire);Near(801.5f,target.hp);
            var panda=ArenaGame.PreviewFighter(game.State.players[0],new UnitState{id="panda",kind="panda"});panda.hp=50;panda.healingMultiplier=.8f;
            float regen=CreatureAbilities.Get(panda.skills.First(id=>CreatureAbilities.Get(id).effect=="regen")).value;
            CreatureAbilities.Tick(panda,new List<FighterState>{panda},1,null,null);Near(50+regen*.8f,panda.hp);
            game.State.battlefield.weather="snow";WorldTick(game);Near(1.25f,target.statusDurationMultiplier);
        });
        Check("highland benefits grounded ranged fire; cover absorbs damage and eventually collapses",()=>{
            var game=WorldBattle("courtyards","clear","skeleton");var archer=game.State.fighters[0];var target=game.State.fighters[1];
            archer.x=archer.y=0;float range=archer.range;WorldTick(game);Near(range*1.15f,archer.range);
            target.x=target.y=350;target.hp=target.maxHp=10000;target.armor=target.shield=0;
            WorldDamage(game,target,100,archer);Near(9892,target.hp);
            var ruin=game.State.battlefield.terrain.First(t=>t.kind=="ruins");target.x=ruin.x;target.y=ruin.y;
            archer.x=ruin.x*1.3f;archer.y=ruin.y*1.3f;WorldTick(game);float before=target.hp;
            WorldDamage(game,target,100,archer);Near(before-75,target.hp);Near(125,ruin.hp);
            for(int i=0;i<5;i++)WorldDamage(game,target,100,archer);Near(0,ruin.hp);
            Assert(game.State.effects.Any(e=>e.type=="cover_break"));before=target.hp;WorldDamage(game,target,100,archer);Near(before-100,target.hp);
        });
        Check("storm warns before damage and water conduction is capped at three secondary targets",()=>{
            var game=WorldBattle("bog","storm");var world=game.State.battlefield;
            world.terrain.Clear();world.objectives.Clear();world.terrain.Add(new TerrainState{id="pool",kind="water",halfWidth=400,halfHeight=400});
            var struck=game.State.fighters[0];struck.x=struck.y=0;struck.hp=struck.maxHp=1000;struck.baseArmor=struck.armor=0;
            game.State.fighters[1].x=350;game.State.fighters[1].y=350;
            for(int i=0;i<5;i++){
                var f=ArenaGame.PreviewFighter(game.State.players[1],new UnitState{id="chain-"+i,kind="cow",x=100+i*5,y=0});f.maxHp=f.hp=1000;game.State.fighters.Add(f);
            }
            world.elapsed=9.99f;WorldTick(game);Assert(world.hazards.Count==1&&world.hazards[0].delay>1.4f);Near(1000,struck.hp);
            var hazard=world.hazards[0];hazard.x=hazard.y=0;
            for(int i=0;i<30;i++)WorldTick(game);
            Near(920,struck.hp);Equal(3,game.State.fighters.Count(f=>f.id.StartsWith("chain-")&&f.hp<1000));
            Assert(hazard.triggered);Assert(struck.stunTime>0);Assert(game.State.effects.Any(e=>e.type=="lightning"));
        });
        Check("objectives reject contests, grant bounded one-time income and deep-copy progress",()=>{
            var game=WorldBattle();var world=game.State.battlefield;var mine=world.objectives.First();
            var a=game.State.fighters[0];var b=game.State.fighters[1];a.x=b.x=mine.x;a.y=b.y=mine.y;
            for(int i=0;i<70;i++)WorldTick(game);Assert(mine.ownerId==null);Near(0,mine.capture);
            b.x=b.y=350;for(int i=0;i<61;i++)WorldTick(game);Equal("a",mine.ownerId);Equal(2,game.State.players[0].gold);
            for(int i=0;i<70;i++)WorldTick(game);Equal(2,game.State.players[0].gold);
            foreach(var objective in world.objectives.Skip(1)){
                a.x=objective.x;a.y=objective.y;for(int i=0;i<61;i++)WorldTick(game);
            }
            Equal(4,game.State.players[0].gold);Equal(4,game.State.players[0].eventIncome);
            var snapshot=game.SnapshotFor("a");snapshot.battlefield.terrain[0].freeze=.7f;snapshot.battlefield.objectives[0].capture=.9f;
            Assert(world.terrain[0].freeze!=.7f&&mine.capture!=.9f);
        });
        Check("commander skills validate first, provide two casts and reset every battle",()=>{
            foreach(string id in BattlefieldCatalog.Commanders){
                var game=WorldBattle();var p=game.State.players[0];p.commanderSkill=id;
                var a=game.State.fighters[0];var b=game.State.fighters[1];a.x=a.y=0;b.x=25;b.y=0;
                string before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="cast",x=float.NaN}));
                Reject(()=>game.Apply("a",new GameCommand{type="cast",x=401}));Equal(before,Json(game.State));
                var cast=new GameCommand{type="cast",x=0,y=0,targetId=b.id};game.Apply("a",cast);Equal(1,p.commanderUses);
                Reject(()=>game.Apply("a",cast));Near(8,p.commanderCooldown);
                p.commanderCooldown=0;game.Apply("a",cast);Equal(0,p.commanderUses);p.commanderCooldown=0;Reject(()=>game.Apply("a",cast));
                WorldCall(game,"StartBattlefieldCombat");Equal(2,p.commanderUses);Near(0,p.commanderCooldown);
            }
        });
        Check("drums, ward, cleanse, smoke and focus change combat with expiry and resistance",()=>{
            var game=WorldBattle();var p=game.State.players[0];var a=game.State.fighters[0];var b=game.State.fighters[1];
            a.x=a.y=0;b.x=25;b.y=0;float speed=a.speed,attackSpeed=a.attackSpeed;
            game.Apply("a",new GameCommand{type="cast"});WorldTick(game);Near(speed*1.1f,a.speed);Near(attackSpeed*1.2f,a.attackSpeed);
            game.State.battlefield.zones.Clear();p.commanderCooldown=0;p.commanderSkill="ward";a.poisonTime=5;a.slowTime=5;
            game.Apply("a",new GameCommand{type="cast"});Near(0,a.poisonTime);Near(0,a.slowTime);WorldTick(game);Near(.2f,a.commandGuard);
            game.State.battlefield.zones.Clear();p.commanderUses=2;p.commanderCooldown=0;p.commanderSkill="smoke";
            game.Apply("a",new GameCommand{type="cast"});WorldTick(game);Assert(a.hidden);Near(speed*1.3f,a.speed);
            a.revealUntil=game.State.battlefield.elapsed+2;WorldTick(game);Assert(!a.hidden);
            p.commanderUses=2;p.commanderCooldown=0;p.commanderSkill="focus";
            game.Apply("a",new GameCommand{type="cast",targetId=b.id});Equal(b,(FighterState)WorldCall(game,"PreferredCommanderTarget",a));
            b.maxHp=b.hp=1000;b.armor=b.shield=0;WorldDamage(game,b,100,a);Near(885,b.hp);
            var q=game.State.players[1];q.commanderSkill="cleanse";q.steve=new Point2(b.x,b.y);b.burnTime=4;b.rootTime=2;
            game.Apply("b",new GameCommand{type="cast",x=b.x,y=b.y});Near(0,b.burnTime);Near(0,b.rootTime);Near(4,b.resistanceTime);
            Assert(!game.State.battlefield.zones.Any(z=>z.kind=="focus"));
            var spider=AbilityPreview("cave_spider");spider.ownerId="a";
            CreatureAbilities.Attack(spider,b,game.State.fighters,(target,amount,source)=>WorldDamage(game,target,amount,source),null,null);Near(0,b.poisonTime);
        });
        Check("traps are private, ignore flight, root ground units; decoys expire without income",()=>{
            var game=WorldBattle();var p=game.State.players[0];p.commanderSkill="trap";
            game.Apply("a",new GameCommand{type="cast"});Equal(1,game.SnapshotFor("a").battlefield.hazards.Count);Equal(0,game.SnapshotFor("b").battlefield.hazards.Count);
            Assert(!game.SnapshotFor("b").effects.Any(e=>e.type=="command_trap"),"hidden trap placement leaked through global effects");
            var bat=ArenaGame.PreviewFighter(game.State.players[1],new UnitState{id="trap-bat",kind="bat"});game.State.fighters.Add(bat);WorldTick(game);Near(bat.maxHp,bat.hp);
            Equal(1,game.SnapshotFor("b").battlefield.hazards.Count);
            var victim=game.State.fighters[1];victim.x=victim.y=0;victim.baseArmor=victim.armor=0;float hp=victim.hp;WorldTick(game);Near(hp-12,victim.hp);Assert(victim.rootTime>1.9f);
            Equal(1,game.SnapshotFor("b").battlefield.hazards.Count);
            Assert(game.SnapshotFor("b").effects.Any(e=>e.type=="command_trap"&&e.targetId==victim.id));
            p.commanderSkill="decoy";p.commanderCooldown=0;game.Apply("a",new GameCommand{type="cast"});var decoy=game.State.fighters.First(f=>f.isDecoy);
            Equal(decoy,(FighterState)WorldCall(game,"PreferredCommanderTarget",victim));
            for(int i=0;i<121;i++)WorldTick(game);Assert(decoy.dead);Equal(0,game.State.players[1].roundKills);
        });
        Check("rift has a windup, teleports only three allies and moving Steve interrupts",()=>{
            var game=WorldBattle();var p=game.State.players[0];p.commanderSkill="rift";
            game.State.fighters[0].x=game.State.fighters[0].y=0;
            for(int i=0;i<4;i++)game.State.fighters.Add(ArenaGame.PreviewFighter(p,new UnitState{id="rift-"+i,kind="cow",x=10+i*10}));
            game.Apply("a",new GameCommand{type="cast",x=150});for(int i=0;i<10;i++)WorldTick(game);Near(0,p.steve.x);
            for(int i=0;i<7;i++)WorldTick(game);Near(150,p.steve.x);Equal(3,game.State.fighters.Count(f=>f.ownerId==p.id&&f.x>100));
            p.commanderCooldown=0;game.Apply("a",new GameCommand{type="cast",x=0});p.steve=new Point2(190,0);
            for(int i=0;i<17;i++)WorldTick(game);Near(190,p.steve.x);
        });
        Check("forest and fog hide enemy snapshots while close allies, scouts and the tower reveal them",()=>{
            var game=WorldBattle("bog","fog");var a=game.State.fighters[0];var b=game.State.fighters[1];
            a.x=b.x=-90;a.y=0;b.y=215;WorldTick(game);Assert(b.hidden);
            Assert(!game.SnapshotFor("a").fighters.Any(f=>f.id==b.id));Assert(game.SnapshotFor("b").fighters.Any(f=>f.id==b.id));
            a.y=160;Assert(game.SnapshotFor("a").fighters.Any(f=>f.id==b.id));a.y=0;
            var scout=ArenaGame.PreviewFighter(game.State.players[0],new UnitState{id="forest-scout",kind="bat",x=-90});game.State.fighters.Add(scout);
            Assert(game.SnapshotFor("a").fighters.Any(f=>f.id==b.id));game.State.fighters.Remove(scout);
            game.State.battlefield.objectives.First(o=>o.kind=="tower").ownerId="a";
            Assert(game.SnapshotFor("a").fighters.Any(f=>f.id==b.id));
            game.State.battlefield.objectives.First(o=>o.kind=="tower").ownerId=null;
            a.x=a.y=0;b.x=b.y=350;WorldTick(game);Assert(!b.hidden);Assert(game.SnapshotFor("a").fighters.Any(f=>f.id==b.id));
            Assert(!BattlefieldVision.CanSee(game.State.battlefield,a,b));
            game.State.battlefield.weather="clear";WorldTick(game);Assert(game.SnapshotFor("a").fighters.Any(f=>f.id==b.id));
        });
        Check("a defeated army immediately spectates the remaining battle instead of losing every enemy snapshot",()=>{
            var game=WorldBattle("bog","fog");var own=game.State.fighters[0];var enemy=game.State.fighters[1];
            own.x=own.y=350;enemy.x=enemy.y=-350;enemy.hidden=true;
            var third=new PlayerState{id="c",name="丙",slot=2};game.State.players.Add(third);
            var survivor=ArenaGame.PreviewFighter(third,new UnitState{id="third",kind="skeleton",x=-300,y=-300});game.State.fighters.Add(survivor);
            Assert(!game.SnapshotFor("a").fighters.Any(f=>f.id==enemy.id));
            own.dead=true;own.hp=0;Assert(!game.State.players[0].eliminated);
            var snapshot=game.SnapshotFor("a");Assert(snapshot.fighters.Any(f=>f.id==enemy.id)&&snapshot.fighters.Any(f=>f.id==survivor.id));
            // A still-living army retains concealed-enemy discovery restrictions.
            own.dead=false;own.hp=1;Assert(!game.SnapshotFor("a").fighters.Any(f=>f.id==enemy.id));
        });
        Check("solid cover has clearance, breaks open and flight crosses it without losing position",()=>{
            var world=BattlefieldCatalog.Create("bog","clear",3);var ruin=world.terrain.First(t=>t.kind=="ruins");
            Assert(!BattlefieldCatalog.IsPassable(world,ruin.x,ruin.y));Assert(BattlefieldCatalog.IsPassable(world,ruin.x,ruin.y,true));
            float x=ruin.x+ruin.halfWidth*.72f+5;
            Assert(BattlefieldCatalog.IsPassable(world,x,ruin.y));Assert(!BattlefieldCatalog.IsPassable(world,x,ruin.y,false,false,10));
            ruin.hp=0;Assert(BattlefieldCatalog.IsPassable(world,ruin.x,ruin.y,false,false,10));
            Assert(!BattlefieldCatalog.IsPassable(world,float.NaN,0));
        });
        Check("snow extends slow duration, resistance rejects reapplication and lava spares fire and flight",()=>{
            var game=WorldBattle("bog","snow","stray");var archer=game.State.fighters[0];var target=game.State.fighters[1];
            target.maxHp=target.hp=1000;target.baseArmor=target.armor=0;WorldTick(game);
            CreatureAbilities.Attack(archer,target,game.State.fighters,(f,damage,source)=>WorldDamage(game,f,damage,source),null,null);
            float duration=archer.skills.Select(CreatureAbilities.Get).First(a=>a.effect=="slow").duration;
            Near(duration*1.25f,target.slowTime);target.slowTime=0;target.resistanceTime=4;
            CreatureAbilities.Attack(archer,target,game.State.fighters,(f,damage,source)=>WorldDamage(game,f,damage,source),null,null);Near(0,target.slowTime);
            game.State.battlefield.weather="clear";game.State.battlefield.terrain.Clear();
            game.State.battlefield.terrain.Add(new TerrainState{id="lava",kind="lava",x=0,y=0,radius=60});
            target.x=target.y=0;float before=target.hp;WorldTick(game,.1f);Near(before-3.5f,target.hp);
            var fire=ArenaGame.PreviewFighter(game.State.players[0],new UnitState{id="lava-fire",kind="magma_cube"});
            var flight=ArenaGame.PreviewFighter(game.State.players[0],new UnitState{id="lava-flight",kind="bat"});
            game.State.fighters.Add(fire);game.State.fighters.Add(flight);WorldTick(game,.1f);Near(fire.maxHp,fire.hp);Near(flight.maxHp,flight.hp);
        });
        Check("courtyard ground routing crosses narrow bridges without falling or oscillating",()=>{
            var game=WorldBattle("courtyards");var f=game.State.fighters[0];f.x=270;f.y=140;
            var destination=new Point2(-260,-160);
            for(int i=0;i<1600;i++){
                var waypoint=(Point2)WorldCall(game,"BattlefieldWaypoint",f,destination);
                float dx=waypoint.x-f.x,dy=waypoint.y-f.y,d=(float)Math.Sqrt(dx*dx+dy*dy);if(d<.01f)continue;
                float step=Math.Min(3,d);var move=(Point2)WorldCall(game,"MoveOnBattlefield",f,new Point2(f.x+dx/d*step,f.y+dy/d*step));
                Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,move.x,move.y));f.x=move.x;f.y=move.y;
                if(Math.Abs(f.x-destination.x)+Math.Abs(f.y-destination.y)<5)break;
            }
            Assert(Math.Abs(f.x-destination.x)+Math.Abs(f.y-destination.y)<8,"route stalled at "+f.x+","+f.y);
            var water=WorldBattle("frost","snow");var river=water.State.battlefield.terrain.First(t=>t.kind=="water");
            Assert(!BattlefieldCatalog.IsPassable(water.State.battlefield,150,0));
            Assert(BattlefieldCatalog.IsPassable(water.State.battlefield,150,0,true));
            Assert(BattlefieldCatalog.IsPassable(water.State.battlefield,150,0,false,true));
            Assert(BattlefieldCatalog.IsPassable(water.State.battlefield,0,0));
            var walker=water.State.fighters[0];walker.x=150;walker.y=100;
            var crossing=new Point2(150,-100);var beforeIce=(Point2)WorldCall(water,"BattlefieldWaypoint",walker,crossing);
            Assert(Math.Abs(beforeIce.x-150)>50,"river route should approach the bridge or bank before freezing");
            for(int i=0;i<400;i++){
                var waypoint=(Point2)WorldCall(water,"BattlefieldWaypoint",walker,crossing);
                float dx=waypoint.x-walker.x,dy=waypoint.y-walker.y,d=(float)Math.Sqrt(dx*dx+dy*dy);if(d<.01f)continue;
                var move=(Point2)WorldCall(water,"MoveOnBattlefield",walker,new Point2(walker.x+dx/d*Math.Min(3,d),walker.y+dy/d*Math.Min(3,d)));
                Assert(BattlefieldCatalog.IsPassable(water.State.battlefield,move.x,move.y));walker.x=move.x;walker.y=move.y;
                if(Math.Abs(walker.x-crossing.x)+Math.Abs(walker.y-crossing.y)<5)break;
            }
            Assert(Math.Abs(walker.x-crossing.x)+Math.Abs(walker.y-crossing.y)<8,"unfrozen river route stalled");
            for(int i=0;i<401;i++)WorldTick(water);Near(1,river.freeze);Assert(BattlefieldCatalog.IsPassable(water.State.battlefield,150,0));
            walker.x=150;walker.y=100;var afterIce=(Point2)WorldCall(water,"BattlefieldWaypoint",walker,crossing);Near(150,afterIce.x);Near(-100,afterIce.y);
        });
        Check("position normalization repairs displaced ground units while preserving legal flight and aquatic points",()=>{
            var game=WorldBattle("courtyards");var ground=game.State.fighters[0];ground.x=180;ground.y=70;
            Assert(!BattlefieldCatalog.IsPassable(game.State.battlefield,ground.x,ground.y));
            WorldCall(game,"NormalizeBattlefieldPosition",ground);Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,ground.x,ground.y));
            var original=new Point2(ground.x,ground.y);WorldCall(game,"NormalizeBattlefieldPosition",ground);Near(original.x,ground.x);Near(original.y,ground.y);
            var flyer=AbilityPreview("bat");flyer.x=180;flyer.y=70;WorldCall(game,"NormalizeBattlefieldPosition",flyer);Near(180,flyer.x);Near(70,flyer.y);
            game.State.battlefield=BattlefieldCatalog.Create("frost","snow",12);
            var swimmer=AbilityPreview("dolphin");swimmer.x=150;swimmer.y=0;WorldCall(game,"NormalizeBattlefieldPosition",swimmer);Near(150,swimmer.x);Near(0,swimmer.y);
            ground.x=150;ground.y=0;WorldCall(game,"NormalizeBattlefieldPosition",ground);Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,ground.x,ground.y));
            flyer.x=450;flyer.y=100;WorldCall(game,"NormalizeBattlefieldPosition",flyer);Near(410,flyer.x);Near(100,flyer.y);
            game.State.preferredMap="frost";game.State.preferredWeather="snow";game.State.players[0].steve=new Point2(150,0);
            WorldCall(game,"InitializeBattlefield");var steve=game.State.players[0].steve;Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,steve.x,steve.y));
        });
        Check("unreachable tactical nodes complete at the safe edge without changing stored route or special movement",()=>{
            var game=WorldBattle("courtyards");var p=game.State.players[0];var fighter=game.State.fighters[0];
            p.units.Add(new UnitState{id=fighter.unitId,kind=fighter.kind});game.State.phase="tactics";
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{fighter.unitId},nodes=new[]{new TacticNode{action="wait",x=180,y=70,seconds=.2f}}});
            game.State.phase="battle";WorldCall(game,"PrepareBattlePlans");object[] call={fighter,.05f,null,false};
            Assert((bool)WorldCall(game,"TacticDestination",call));var projected=(Point2)call[2];
            Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,projected.x,projected.y));
            fighter.x=projected.x;fighter.y=projected.y;for(int i=0;i<5;i++)WorldCall(game,"TacticDestination",call);Equal(1,fighter.tacticIndex);
            Near(180,p.tactics[0].nodes[0].x);Near(70,p.tactics[0].nodes[0].y);
            fighter.kind="bat";fighter.tacticIndex=0;fighter.x=fighter.y=300;WorldCall(game,"TacticDestination",call);
            var flight=(Point2)call[2];Near(180,flight.x);Near(70,flight.y);
            game.State.battlefield=BattlefieldCatalog.Create("frost","snow",12);p.tactics[0].nodes[0]=new TacticNode{x=150,y=0};
            fighter.kind="dolphin";fighter.tacticIndex=0;WorldCall(game,"TacticDestination",call);var aquatic=(Point2)call[2];Near(150,aquatic.x);Near(0,aquatic.y);
            fighter.kind="cow";fighter.x=150;fighter.y=100;p.tactics[0].nodes[0]=new TacticNode{condition="after_time",seconds=20,x=150,y=0};
            WorldCall(game,"TacticDestination",call);Assert((bool)call[3]);Near(100,((Point2)call[2]).y);
            game.State.battlefield.elapsed=20;WorldTick(game);WorldCall(game,"TacticDestination",call);Assert(!(bool)call[3]);Near(150,((Point2)call[2]).x);Near(0,((Point2)call[2]).y);
            Near(150,p.tactics[0].nodes[0].x);Near(0,p.tactics[0].nodes[0].y);
        });
    }
}
