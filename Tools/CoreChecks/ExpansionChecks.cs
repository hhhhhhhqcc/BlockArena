using System;
using System.Collections.Generic;
using System.Linq;
using BlockArena.Core;

internal static partial class Program
{
    static ArenaGame ExpansionGame(int seed,string map="bog",int count=4)
    {
        var game=new ArenaGame("a","甲",seed);
        for(int i=1;i<count;i++)game.AddPlayer(((char)('a'+i)).ToString(),"玩家"+i);
        game.Apply("a",new GameCommand{type="world_options",mapId=map,weather="clear"});
        game.Apply("a",new GameCommand{type="start",bots=false});ChooseAll(game);return game;
    }
    static string DeploymentSignature(ArenaGame game)
    {return string.Join("|",game.State.players.Select(p=>p.id+":"+p.deploymentSide+":"+p.deploymentOffset.x.ToString("R")+":"+p.deploymentOffset.y.ToString("R")));}
    static void ExpansionChecks()
    {
        Check("expanded maps quadruple the playable area with authored connected terrain and unchanged combat reach",()=>{
            foreach(string map in BattlefieldCatalog.Maps){
                var original=BattlefieldCatalog.Create(map,"clear",15);var expanded=BattlefieldCatalog.CreateExpanded(map,"clear",15);
                Near(2,expanded.scale);Near(820,expanded.halfExtent);Near(4,(expanded.halfExtent*expanded.halfExtent)/(original.halfExtent*original.halfExtent));
                Assert(expanded.terrain.Count>=original.terrain.Count&&expanded.objectives.Count>=original.objectives.Count);
                var repeated=BattlefieldCatalog.CreateExpanded(map,"clear",791);
                Equal(System.Text.Json.JsonSerializer.Serialize(expanded.terrain,JsonOptions),System.Text.Json.JsonSerializer.Serialize(repeated.terrain,JsonOptions));
                Equal(System.Text.Json.JsonSerializer.Serialize(expanded.objectives,JsonOptions),System.Text.Json.JsonSerializer.Serialize(repeated.objectives,JsonOptions));
                foreach(var objective in expanded.objectives){
                    Assert(BattlefieldCatalog.IsPassable(expanded,objective.x,objective.y,false,false,10),"unreachable objective centre "+map+"/"+objective.id);
                    Near(48,objective.radius);
                }
                if(map=="courtyards")Assert(expanded.terrain.Any(t=>t.kind=="chasm")&&expanded.terrain.Count(t=>t.kind=="bridge")==4);
                else Assert(expanded.terrain.Any(t=>t.shape=="river")&&expanded.terrain.Count(t=>t.shape=="ellipse")>=2);
                Assert(BattlefieldCatalog.IsPassable(expanded,790,790));Assert(!BattlefieldCatalog.IsPassable(original,790,790));
                Assert(!BattlefieldCatalog.IsPassable(expanded,821,0,true));Assert(!BattlefieldCatalog.IsPassable(expanded,float.PositiveInfinity,0,true));
            }
            Near(110,BattlefieldCatalog.GetCommander("drums").radius);
        });
        Check("curved river and rotated woodland share exact gameplay contours and preserve their shape in snapshots",()=>{
            var game=ExpansionGame(95,"frost");WorldCall(game,"InitializeBattlefieldCore",true);
            var river=game.State.battlefield.terrain.First(t=>t.shape=="river");
            foreach(float x in new[]{-590f,-315f,0,315f,590f}){
                float center=BattlefieldCatalog.RiverCentre(river,x),width=BattlefieldCatalog.RiverWidth(river,x);
                Assert(BattlefieldCatalog.Contains(river,x,center+width-.1f));Assert(!BattlefieldCatalog.Contains(river,x,center+width+.1f));
            }
            var grove=game.State.battlefield.terrain.First(t=>t.shape=="ellipse");
            var inside=BattlefieldCatalog.TerrainPoint(grove,grove.halfWidth*.99f,0);
            var outside=BattlefieldCatalog.TerrainPoint(grove,grove.halfWidth*1.01f,0);
            Assert(BattlefieldCatalog.Contains(grove,inside.x,inside.y));Assert(!BattlefieldCatalog.Contains(grove,outside.x,outside.y));
            var copy=game.SnapshotFor("a");
            foreach(var terrain in game.State.battlefield.terrain){var saved=copy.battlefield.terrain.First(t=>t.id==terrain.id);Equal(terrain.shape,saved.shape);Near(terrain.rotation,saved.rotation);}
        });
        Check("every designed reward remains reachable on foot from all four random deployment directions",()=>{
            foreach(string map in BattlefieldCatalog.Maps){
                var game=ExpansionGame(274,map);WorldCall(game,"InitializeBattlefieldCore",true);
                foreach(var player in game.State.players)foreach(var objective in game.State.battlefield.objectives){
                    Point2 start=ArenaGame.FormationToWorld(player,0,265);
                    var fighter=ArenaGame.PreviewFighter(player,new UnitState{id="route-"+player.id+objective.id,kind="cow",x=start.x,y=start.y});
                    Point2 goal=new Point2(objective.x,objective.y);
                    for(int step=0;step<4200;step++){
                        var waypoint=(Point2)WorldCall(game,"BattlefieldWaypoint",fighter,goal);
                        float dx=waypoint.x-fighter.x,dy=waypoint.y-fighter.y,d=(float)Math.Sqrt(dx*dx+dy*dy);if(d<.001f)continue;
                        float stride=Math.Min(d,3);
                        var moved=(Point2)WorldCall(game,"MoveOnBattlefield",fighter,new Point2(fighter.x+dx/d*stride,fighter.y+dy/d*stride));
                        Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,moved.x,moved.y,false,false,10),"reward route stepped into terrain "+map+"/"+objective.id);
                        fighter.x=moved.x;fighter.y=moved.y;
                        if(Math.Abs(fighter.x-goal.x)+Math.Abs(fighter.y-goal.y)<7)break;
                    }
                    Assert(Math.Abs(fighter.x-goal.x)+Math.Abs(fighter.y-goal.y)<10,"reward route stalled "+map+"/"+player.id+"/"+objective.id+" at "+fighter.x+","+fighter.y);
                }
            }
        });
        Check("map draw relocates complete living formations once and leaves the initial preparation arena unchanged",()=>{
            var game=ExpansionGame(724);var before=new Dictionary<string,Point2>();
            foreach(var player in game.State.players)foreach(var unit in player.units)before[unit.id]=ArenaGame.WorldToLocal(player.slot,unit.x,unit.y);
            foreach(var player in game.State.players)player.ready=true;
            while(game.State.phase=="prep")game.Tick(.05f);
            Equal("arena",game.State.revealStage);Assert(game.State.battlefield==null&&game.State.players.All(p=>!p.deployed));
            while(game.State.revealStage=="arena")game.Tick(.05f);
            Equal("map",game.State.revealStage);Near(2,game.State.battlefield.scale);Equal(4,game.State.players.Select(p=>p.deploymentSide).Distinct().Count());
            foreach(var player in game.State.players){
                Assert(player.deployed);
                foreach(var unit in player.units){var local=ArenaGame.WorldToFormation(player,unit.x,unit.y);Near(before[unit.id].x,local.x);Near(before[unit.id].y,local.y);}
                Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,player.steve.x,player.steve.y,false,false,9));
            }
            string signature=DeploymentSignature(game);
            while(game.State.phase=="reveal")game.Tick(.05f);
            Equal(signature,DeploymentSignature(game));Equal("tactics",game.State.phase);
            var copy=game.SnapshotFor("b");Near(820,copy.battlefield.halfExtent);
            for(int i=0;i<copy.players.Count;i++){
                Equal(game.State.players[i].deployed,copy.players[i].deployed);Equal(game.State.players[i].deploymentSide,copy.players[i].deploymentSide);
                Near(game.State.players[i].deploymentOffset.x,copy.players[i].deploymentOffset.x);
            }
        });
        Check("spawn regions are separated, reproducible and clear for all three terrains including formation corners",()=>{
            var signatures=new HashSet<string>();
            foreach(string map in BattlefieldCatalog.Maps)for(int seed=0;seed<8;seed++){
                var game=ExpansionGame(seed,map);var repeat=ExpansionGame(seed,map);
                WorldCall(game,"InitializeBattlefieldCore",true);WorldCall(repeat,"InitializeBattlefieldCore",true);
                Equal(DeploymentSignature(game),DeploymentSignature(repeat));signatures.Add(DeploymentSignature(game));
                var centres=new List<Point2>();
                foreach(var player in game.State.players){
                    var centre=ArenaGame.FormationToWorld(player,0,265);
                    foreach(var existing in centres){float dx=centre.x-existing.x,dy=centre.y-existing.y;Assert(dx*dx+dy*dy>800*800);}
                    centres.Add(centre);
                    foreach(float x in new[]{-185f,0,185f})foreach(float y in new[]{175f,265f,355f}){
                        var point=ArenaGame.FormationToWorld(player,x,y);
                        Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,point.x,point.y,false,false,10),"spawn overlaps terrain: "+map+"/"+seed);
                        var inverse=ArenaGame.WorldToFormation(player,point.x,point.y);Near(x,inverse.x);Near(y,inverse.y);
                    }
                }
            }
            Equal(8,signatures.Count);
        });
        Check("eliminated commanders consume no new spawn and deployment metadata cannot leak eliminated rosters",()=>{
            var game=ExpansionGame(20);var dead=game.State.players[1];dead.hp=0;dead.eliminated=true;
            var saved=new Point2(dead.units[0].x,dead.units[0].y);
            WorldCall(game,"InitializeBattlefieldCore",true);
            Assert(!dead.deployed);Near(saved.x,dead.units[0].x);Near(saved.y,dead.units[0].y);
            Equal(3,game.State.players.Where(p=>p.deployed).Select(p=>p.deploymentSide).Distinct().Count());
            Assert(game.SnapshotFor("a").players.First(p=>p.id==dead.id).units.Count==0);
        });
        Check("returning to preparation inverts random deployment without cumulative formation drift",()=>{
            var game=ExpansionGame(325);var expected=new Dictionary<string,Point2>();
            foreach(var player in game.State.players)foreach(var unit in player.units)expected[unit.id]=new Point2(unit.x,unit.y);
            string previous=null;bool changed=false;
            for(int round=0;round<6;round++){
                WorldCall(game,"InitializeBattlefieldCore",true);string signature=DeploymentSignature(game);if(previous!=null&&previous!=signature)changed=true;previous=signature;
                WorldCall(game,"BeginRecruit",false);
                foreach(var player in game.State.players){
                    Assert(!player.deployed);foreach(var unit in player.units){Near(expected[unit.id].x,unit.x,.004f);Near(expected[unit.id].y,unit.y,.004f);}
                    var steve=ArenaGame.LocalToWorld(player.slot,0,390);Near(steve.x,player.steve.x);Near(steve.y,player.steve.y);
                }
            }
            Assert(changed,"all rounds reused the same deployment");
        });
        Check("expanded commands accept distant legal positions and preserve deployment ownership and actual skill reach",()=>{
            var game=ExpansionGame(531);WorldCall(game,"InitializeBattlefieldCore",true);game.State.phase="tactics";game.State.timeLeft=120;
            var player=game.State.players[0];var unit=player.units[0];var at=ArenaGame.FormationToWorld(player,50,300);
            game.Apply("a",new GameCommand{type="place",unitId=unit.id,x=at.x,y=at.y,placementId="expanded-place"});Near(at.x,unit.x);Near(at.y,unit.y);
            string before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="place",unitId=unit.id,x=0,y=300}));Equal(before,Json(game.State));
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{unit.id},nodes=new[]{new TacticNode{x=720,y=650}}});
            before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{unit.id},nodes=new[]{new TacticNode{x=821,y=650}}}));Equal(before,Json(game.State));
            player.steve=new Point2(650,650);game.Apply("a",new GameCommand{type="move_to",x=730,y=650});
            for(int i=0;i<100&&player.steveNavigating;i++)game.Tick(.05f);Near(730,player.steve.x,2.1f);Assert(!player.steveNavigating);
            game.State.phase="battle";player.commanderSkill="ward";player.commanderUses=2;player.commanderCooldown=0;
            game.Apply("a",new GameCommand{type="cast",x=750,y=650});Equal(1,player.commanderUses);
            player.commanderCooldown=0;before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="cast",x=400,y=650}));Equal(before,Json(game.State));
        });
        Check("blink and knockback preserve distant expanded coordinates instead of snapping to the old border",()=>{
            var blink=AbilityBattle("enderman","cow");blink.State.battlefield=BattlefieldCatalog.CreateExpanded("bog","clear",3);
            var jumper=blink.State.fighters[0];var enemy=blink.State.fighters[1];
            jumper.x=600;jumper.y=650;enemy.x=710;enemy.y=650;
            blink.Tick(.05f);Assert(jumper.x>650&&jumper.x<710);Near(650,jumper.y);
            var push=AbilityBattle("goat","cow");push.State.battlefield=BattlefieldCatalog.CreateExpanded("bog","clear",3);
            var goat=push.State.fighters[0];var cow=push.State.fighters[1];
            goat.x=700;goat.y=650;cow.x=720;cow.y=650;
            WorldCall(push,"Attack",goat,cow);Near(742,cow.x);Near(650,cow.y);
        });
        Check("expanded courtyard and frozen-river routes use the enlarged bridges and outer banks",()=>{
            foreach(string map in new[]{"courtyards","frost"}){
                var game=WorldBattle(map);game.State.battlefield=BattlefieldCatalog.CreateExpanded(map,"clear",47);
                var fighter=game.State.fighters[0];fighter.x=map=="frost"?300:540;fighter.y=map=="frost"?200:280;
                var goal=map=="frost"?new Point2(300,-200):new Point2(-520,-320);
                for(int step=0;step<2600;step++){
                    var waypoint=(Point2)WorldCall(game,"BattlefieldWaypoint",fighter,goal);
                    float dx=waypoint.x-fighter.x,dy=waypoint.y-fighter.y,d=(float)Math.Sqrt(dx*dx+dy*dy);if(d<.001f)continue;
                    var moved=(Point2)WorldCall(game,"MoveOnBattlefield",fighter,new Point2(fighter.x+dx/d*Math.Min(d,3),fighter.y+dy/d*Math.Min(d,3)));
                    Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,moved.x,moved.y));fighter.x=moved.x;fighter.y=moved.y;
                    if(Math.Abs(fighter.x-goal.x)+Math.Abs(fighter.y-goal.y)<5)break;
                }
                Assert(Math.Abs(fighter.x-goal.x)+Math.Abs(fighter.y-goal.y)<8,"expanded route stalled on "+map+" at "+fighter.x+","+fighter.y);
            }
        });
        Check("a large fog battlefield advances armies toward objectives even before any enemy becomes visible",()=>{
            var game=ExpansionGame(77,"bog");WorldCall(game,"InitializeBattlefieldCore",true);game.State.battlefield.weather="fog";
            WorldCall(game,"BeginBattle");var fighter=game.State.fighters[0];float before=Math.Abs(fighter.x)+Math.Abs(fighter.y);
            Advance(game,4);Assert(Math.Abs(fighter.x)+Math.Abs(fighter.y)<before-70,"army waited at its far-away spawn");
        });
    }
}
