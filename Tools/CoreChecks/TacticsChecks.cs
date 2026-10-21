using System;
using System.Linq;
using BlockArena.Core;

internal static partial class Program
{
    static ArenaGame TacticalGame()
    {
        var game=new ArenaGame("a","甲",37);game.AddPlayer("b","乙");
        game.Apply("a",new GameCommand{type="world_options",mapId="bog",weather="rain"});
        game.Apply("a",new GameCommand{type="start",bots=false});ChooseAll(game);
        game.State.roundEvent=new ArenaEventState{kind="none"};
        foreach(var p in game.State.players)p.ready=true;
        for(int i=0;i<30&&game.State.phase=="prep";i++)game.Tick(.05f);
        Equal("reveal",game.State.phase);
        for(int i=0;i<Math.Ceiling(ArenaGame.RevealSeconds/.05f)+5&&game.State.phase=="reveal";i++)game.Tick(.05f);
        Equal("tactics",game.State.phase);return game;
    }
    static void TacticsChecks()
    {
        Check("reinforcement choice and individual deployment share one timer without waiting for other recruits",()=>{
            var game=Two(false);var a=game.State.players[0];var b=game.State.players[1];Near(45,game.State.timeLeft);
            Advance(game,7);game.Apply("a",new GameCommand{type="skip"});a.gold=100;
            var offer=a.market[0];game.Apply("a",new GameCommand{type="purchase",offerId=offer.id});Assert(a.upgrades.Contains(offer.itemId));
            game.Apply("a",new GameCommand{type="place",unitId=a.units[0].id,x=30,y=250});
            game.Apply("a",new GameCommand{type="ready"});Assert(a.ready&&b.recruitPending);Equal("recruit",game.State.phase);
            Advance(game,5);Equal("recruit",game.State.phase);Near(33,game.State.timeLeft,.01f);Near(250,a.units[0].y);
            game.Apply("b",new GameCommand{type="skip"});Equal("prep",game.State.phase);Near(33,game.State.timeLeft,.01f);Assert(a.ready);
            game.Apply("b",new GameCommand{type="ready"});Advance(game,.6f);Equal("prep",game.State.phase);
            Advance(game,.5f);Equal("reveal",game.State.phase);Assert(game.State.battlefield==null);
        });
        Check("the shared timeout settles pending recruits and reveals in the same tick without another preparation window",()=>{
            var game=Two(false);var a=game.State.players[0];var b=game.State.players[1];
            game.Apply("a",new GameCommand{type="skip"});game.Apply("a",new GameCommand{type="ready"});
            b.gold=0;game.State.timeLeft=.01f;game.Tick(.05f);
            Equal("reveal",game.State.phase);Near(ArenaGame.RevealSeconds,game.State.timeLeft);Assert(!b.recruitPending&&b.units.Count==2);
            Equal("arena",game.State.revealStage);
        });
        Check("reveal returns to the plain arena, draws map, finishes terrain, then draws weather",()=>{
            var game=new ArenaGame("a","甲",37);game.AddPlayer("b","乙");
            game.Apply("a",new GameCommand{type="world_options",mapId="frost",weather="snow"});
            game.Apply("a",new GameCommand{type="start",bots=false});ChooseAll(game);
            foreach(var p in game.State.players)p.ready=true;
            while(game.State.phase=="prep")game.Tick(.05f);
            Equal("arena",game.State.revealStage);Assert(game.State.battlefield==null);
            while(game.State.revealStage=="arena")game.Tick(.05f);
            Equal("map",game.State.revealStage);Equal("frost",game.State.battlefield.mapId);Equal("clear",game.State.battlefield.weather);Near(0,game.State.battlefield.revealAge);
            while(game.State.revealStage=="map")game.Tick(.05f);
            Equal("terrain",game.State.revealStage);Equal("clear",game.State.battlefield.weather);
            while(game.State.revealStage=="terrain")game.Tick(.05f);
            Equal("weather",game.State.revealStage);Near(ArenaGame.RevealTerrainSeconds,game.State.battlefield.revealAge);Equal("clear",game.State.battlefield.weather);
            var copy=game.SnapshotFor("a");Equal("weather",copy.revealStage);Near(game.State.revealStageAge,copy.revealStageAge);
            while(game.State.revealStage=="weather")game.Tick(.05f);
            Equal("climate",game.State.revealStage);Equal("snow",game.State.battlefield.weather);
            while(game.State.phase=="reveal")game.Tick(.05f);
            Equal("tactics",game.State.phase);Near(120,game.State.timeLeft);Assert(game.State.revealStage==null);
        });
        Check("map reveal precedes tactics; timeouts start battle without input",()=>{
            var game=TacticalGame();Assert(game.State.battlefield!=null);Equal(0,game.State.fighters.Count);
            Advance(game,ArenaGame.TacticsSeconds+.1f);Equal("battle",game.State.phase);Assert(game.State.fighters.Count>0);
        });
        Check("tactics dragging validates owner, living player, deployment bounds and terrain before acknowledging",()=>{
            var game=TacticalGame();var p=game.State.players[0];var u=p.units[0];p.ready=true;
            game.Apply("a",new GameCommand{type="place",unitId=u.id,x=0,y=300,placementId="drag-1"});
            Near(0,u.x);Near(300,u.y);Equal("drag-1",u.placementId);Assert(!p.ready);
            var bad=new[]{new GameCommand{type="place",unitId=game.State.players[1].units[0].id,x=0,y=300},
                new GameCommand{type="place",unitId=u.id,x=0,y=0},new GameCommand{type="place",unitId=u.id,x=float.NaN,y=300}};
            foreach(var command in bad){string before=Json(game.State);Reject(()=>game.Apply("a",command));Equal(before,Json(game.State));}
            game.State.battlefield=BattlefieldCatalog.Create("courtyards","clear",5);
            string previous=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="place",unitId=u.id,x=135,y=275}));Equal(previous,Json(game.State));
            p.eliminated=true;previous=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="place",unitId=u.id,x=0,y=300}));Equal(previous,Json(game.State));
            Assert(game.SnapshotFor("b").players[0].units.Count==0);
        });
        Check("tactics dismissal refunds the paid price and removes only the dismissed route membership",()=>{
            var game=TacticalGame();var p=game.State.players[0];var unit=p.units.Last();unit.paidPrice=11;
            var ids=p.units.Select(u=>u.id).ToArray();
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=ids,nodes=new[]{new TacticNode{x=0,y=300}}});
            p.ready=true;string before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="dismiss",unitId=unit.id}));Equal(before,Json(game.State));
            p.ready=false;int gold=p.gold;game.Apply("a",new GameCommand{type="dismiss",unitId=unit.id});
            Equal(gold+5,p.gold);Assert(!p.units.Contains(unit));Assert(p.tactics.All(plan=>!plan.unitIds.Contains(unit.id)));Equal(ids.Length-1,p.tactics[0].unitIds.Length);
        });
        Check("Steve click navigation crosses bridges and routes around solid ruins without clipping",()=>{
            foreach(string map in new[]{"courtyards","bog","frost"}){
                var game=TacticalGame();game.State.battlefield=BattlefieldCatalog.Create(map,"clear",5);var p=game.State.players[0];
                p.steve=map=="frost"?new Point2(150,100):new Point2(270,140);
                var goal=map=="frost"?new Point2(150,-100):new Point2(-260,-160);
                game.Apply("a",new GameCommand{type="move_to",x=goal.x,y=goal.y});Assert(p.steveNavigating);
                for(int tick=0;tick<600&&p.steveNavigating;tick++){
                    game.Tick(.05f);Assert(BattlefieldCatalog.IsPassable(game.State.battlefield,p.steve.x,p.steve.y,false,false,9));
                }
                Assert(Math.Abs(p.steve.x-goal.x)+Math.Abs(p.steve.y-goal.y)<4,"Steve navigation stalled on "+map+" at "+p.steve.x+","+p.steve.y);
                game.Apply("a",new GameCommand{type="move_to",x=0,y=300});
                game.Apply("a",new GameCommand{type="move",dx=0,dy=0});Assert(!p.steveNavigating);
                string before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="move_to",x=float.PositiveInfinity}));Equal(before,Json(game.State));
            }
        });
        Check("tactical routes are owner-only deep copies and cannot mutate accepted nodes",()=>{
            var game=TacticalGame();var p=game.State.players[0];
            var node=new TacticNode{action="wait",x=10,y=20,seconds=3};
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{p.units[0].id},nodes=new[]{node}});
            node.x=100;Near(10,p.tactics[0].nodes[0].x);
            var own=game.SnapshotFor("a");var other=game.SnapshotFor("b");Equal(0,other.players[0].tactics.Count);
            own.players[0].tactics[0].nodes[0].x=300;own.players[0].tactics[0].unitIds[0]="fake";
            Near(10,p.tactics[0].nodes[0].x);Equal(p.units[0].id,p.tactics[0].unitIds[0]);
        });
        Check("invalid route batches reject atomically: foreign, duplicate, NaN, action, objective and size",()=>{
            var game=TacticalGame();var p=game.State.players[0];var id=p.units[0].id;
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{id},nodes=new[]{new TacticNode{x=0,y=200}}});
            var bad=new[]{
                new GameCommand{type="tactics_set",unitIds=new[]{game.State.players[1].units[0].id},nodes=new TacticNode[0]},
                new GameCommand{type="tactics_set",unitIds=new[]{id,id},nodes=new TacticNode[0]},
                new GameCommand{type="tactics_set",unitIds=new[]{id},nodes=new[]{new TacticNode{x=float.NaN}}},
                new GameCommand{type="tactics_set",unitIds=new[]{id},nodes=new[]{new TacticNode{action="arbitrary"}}},
                new GameCommand{type="tactics_set",unitIds=new[]{id},nodes=new[]{new TacticNode{action="capture",targetId="missing"}}},
                new GameCommand{type="tactics_set",unitIds=new[]{id},nodes=Enumerable.Range(0,11).Select(i=>new TacticNode()).ToArray()}
            };
            foreach(var command in bad){string before=Json(game.State);Reject(()=>game.Apply("a",command));Equal(before,Json(game.State));}
        });
        Check("reassigning part of a squad preserves other units and changing nodes unsets readiness",()=>{
            var game=TacticalGame();var p=game.State.players[0];var ids=p.units.Select(u=>u.id).ToArray();
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=ids,nodes=new[]{new TacticNode{x=20}}});
            game.Apply("a",new GameCommand{type="tactics_ready"});Assert(p.ready);
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{ids[0]},nodes=new[]{new TacticNode{x=100}}});
            Assert(!p.ready);Equal(2,p.tactics.Count);Equal(ids.Length-1,p.tactics[0].unitIds.Length);
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{ids[0]},nodes=new TacticNode[0]});Equal(1,p.tactics.Count);
        });
        Check("wait and after-time nodes hold before releasing movement; routes remain locked during battle",()=>{
            var game=TacticalGame();var p=game.State.players[0];var unit=p.units[0];
            unit.x=0;unit.y=300;
            game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{unit.id},nodes=new[]{
                new TacticNode{action="wait",x=0,y=300,seconds=1},
                new TacticNode{action="move",condition="after_time",seconds=2,x=160,y=300}
            }});
            FinishDeployment(game);var f=game.State.fighters.First(v=>v.unitId==unit.id);
            foreach(var v in game.State.fighters){v.attackCooldown=1000;if(v!=f)v.speed=v.baseSpeed=0;}
            // Remove separation and objective interference, preserving the host's tactical timers.
            game.State.fighters.RemoveAll(v=>v.ownerId=="a"&&v!=f);game.State.battlefield.terrain.Clear();game.State.battlefield.objectives.Clear();
            Advance(game,.8f);Near(0,f.x);Advance(game,.7f);Near(0,f.x);Advance(game,1);Assert(f.x>10,"Timed route did not start");
            string before=Json(game.State);Reject(()=>game.Apply("a",new GameCommand{type="tactics_set",unitIds=new[]{unit.id},nodes=new TacticNode[0]}));Equal(before,Json(game.State));
        });
    }
}
