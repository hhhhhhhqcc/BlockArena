using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockArena.Core
{
    public sealed partial class ArenaGame
    {
        public const float RevealArenaSeconds=1.6f, RevealMapSeconds=1.5f, RevealTerrainSeconds=4f,
            RevealWeatherSeconds=2.2f, RevealClimateSeconds=1.7f;
        public const float RevealSeconds=RevealArenaSeconds+RevealMapSeconds+RevealTerrainSeconds+RevealWeatherSeconds+RevealClimateSeconds,
            TacticsSeconds=120;
        readonly Dictionary<string,TacticPlanState> battlePlans=new Dictionary<string,TacticPlanState>();
        static readonly HashSet<string> tacticActions=new HashSet<string>{"move","capture","ambush","wait","attack"};
        static readonly HashSet<string> tacticConditions=new HashSet<string>{"always","after_time","health_below","enemy_near"};

        bool HandleTacticsCommand(PlayerState player,GameCommand command)
        {
            if(command.type!="tactics_set"&&command.type!="tactics_ready")return false;
            if(State.phase!="tactics")Fail("请在战前战术编排阶段操作");
            if(command.type=="tactics_ready"){player.ready=!player.ready;return true;}
            if(command.unitIds==null||command.unitIds.Length==0||command.unitIds.Length>256)Fail("每次请选择 1～256 只己方生物");
            if(command.nodes==null||command.nodes.Length>10)Fail("一条路线最多 10 个节点");
            var ids=new HashSet<string>(command.unitIds,StringComparer.Ordinal);
            if(ids.Count!=command.unitIds.Length||ids.Any(id=>!player.units.Any(u=>u.id==id)))Fail("路线只能分配给自己拥有的生物，且不可重复");
            // Validate the complete request first. Rejection never clears an existing route.
            var nodes=new List<TacticNode>();
            foreach(TacticNode node in command.nodes)
            {
                if(node==null||!tacticActions.Contains(node.action??"")||!tacticConditions.Contains(node.condition??""))Fail("战术节点或条件无效");
                if(!Finite(node.x)||!Finite(node.y)||Math.Abs(node.x)>BattlefieldEdge||Math.Abs(node.y)>BattlefieldEdge||
                    !Finite(node.seconds)||node.seconds<0||node.seconds>20||!Finite(node.threshold)||node.threshold<0||node.threshold>250)Fail("战术坐标、等待时间或条件参数无效");
                if(node.targetId!=null&&node.targetId.Length>80)Fail("战术目标标识过长");
                if(node.action=="capture"&&!string.IsNullOrEmpty(node.targetId)&&
                    (State.battlefield==null||!State.battlefield.objectives.Any(o=>o.id==node.targetId)))Fail("地图上不存在该占领目标");
                nodes.Add(CopyTacticNode(node));
            }
            foreach(var plan in player.tactics)plan.unitIds=plan.unitIds.Where(id=>!ids.Contains(id)).ToArray();
            player.tactics.RemoveAll(plan=>plan.unitIds.Length==0);
            if(nodes.Count>0)player.tactics.Add(new TacticPlanState{id=Id("plan"),unitIds=command.unitIds.ToArray(),nodes=nodes});
            player.ready=false;return true;
        }
        static TacticNode CopyTacticNode(TacticNode n)
        {return new TacticNode{action=n.action,condition=n.condition,targetId=n.targetId,x=n.x,y=n.y,seconds=n.seconds,threshold=n.threshold};}
        static TacticPlanState CopyTacticPlan(TacticPlanState plan)
        {return new TacticPlanState{id=plan.id,unitIds=(string[])plan.unitIds.Clone(),nodes=plan.nodes.Select(CopyTacticNode).ToList()};}
        void BeginReveal()
        {
            State.battlefield=null;State.phase="reveal";State.timeLeft=RevealSeconds;readyElapsed=0;
            State.revealStage="arena";State.revealStageAge=0;State.message="";
            foreach(var player in Living()){player.ready=false;player.moveUntil=0;player.steveNavigating=false;}
        }
        void TickReveal(float dt)
        {
            State.revealStageAge+=dt;
            float duration=State.revealStage=="arena"?RevealArenaSeconds:State.revealStage=="map"?RevealMapSeconds:
                State.revealStage=="terrain"?RevealTerrainSeconds:State.revealStage=="weather"?RevealWeatherSeconds:RevealClimateSeconds;
            if(State.revealStage=="terrain"&&State.battlefield!=null)
                State.battlefield.revealAge=Math.Min(RevealTerrainSeconds,State.revealStageAge);
            if(State.revealStageAge+.0001f<duration)return;
            State.revealStageAge=Math.Max(0,State.revealStageAge-duration);
            switch(State.revealStage){
                case "arena":InitializeBattlefieldCore(true);State.revealStage="map";break;
                case "map":State.revealStage="terrain";break;
                case "terrain":State.battlefield.revealAge=RevealTerrainSeconds;State.revealStage="weather";break;
                case "weather":SelectBattlefieldWeather();State.revealStage="climate";break;
                default:BeginTactics();break;
            }
        }
        void BeginTactics()
        {
            State.phase="tactics";State.timeLeft=TacticsSeconds;readyElapsed=0;
            State.revealStage=null;State.revealStageAge=0;State.message="";
            foreach(var player in Living()){
                player.ready=false;player.botAt=clock+.8f+Rng()*.5f;
                // Newly raised terrain can cover a previously legal flat-arena deployment.
                // Correct it before planning rather than teleporting the creature at battle start.
                foreach(var unit in player.units){
                    var creature=CreatureCatalog.Get(unit.kind);
                    Point2 safe=BattlefieldSafePoint(new Point2(unit.x,unit.y),creature.flying,creature.tags.Contains("aquatic"),creature.flying?0:10);
                    unit.x=safe.x;unit.y=safe.y;
                }
            }
        }
        void PrepareBattlePlans()
        {
            battlePlans.Clear();
            foreach(var player in Living())foreach(var plan in player.tactics)
                foreach(string id in plan.unitIds)battlePlans[id]=plan;
        }
        // A planned unit still attacks enemies in reach. Route nodes determine its movement,
        // and an explicit wait/ambush can hold position without changing server coordinates.
        bool TacticDestination(FighterState f,float dt,out Point2 point,out bool hold)
        {
            point=new Point2(f.x,f.y);hold=false;
            TacticPlanState plan;
            if(f.summoned||f.neutral||!battlePlans.TryGetValue(f.unitId,out plan)||f.tacticIndex>=plan.nodes.Count)return false;
            for(int step=0;step<10&&f.tacticIndex<plan.nodes.Count;step++)
            {
                TacticNode node=plan.nodes[f.tacticIndex];
                Point2 targetPoint=BattlefieldSafePoint(new Point2(node.x,node.y),BattlefieldCatalog.Flying(f),BattlefieldCatalog.HasTag(f,"aquatic"));point=targetPoint;
                bool enemyNear=State.fighters.Any(other=>!other.dead&&other.ownerId!=f.ownerId&&
                    Distance(other.x,other.y,targetPoint.x,targetPoint.y)<=Math.Max(35,node.threshold)&&CanBattlefieldTarget(f,other));
                bool ready=node.condition=="always"||node.condition=="after_time"&&(State.battlefield==null?BattleSeconds-State.timeLeft:State.battlefield.elapsed)>=node.seconds||
                    node.condition=="health_below"&&f.hp/Math.Max(1,f.maxHp)*100<=node.threshold||node.condition=="enemy_near"&&enemyNear;
                if(!ready){point=new Point2(f.x,f.y);hold=true;return true;}
                if(Distance(f.x,f.y,point.x,point.y)>24){f.tacticWait=0;return true;}
                f.tacticWait+=dt;
                bool advance=false;
                switch(node.action)
                {
                    case "move":advance=true;break;
                    case "wait":advance=f.tacticWait>=node.seconds;hold=!advance;break;
                    case "ambush":
                        advance=enemyNear||f.tacticWait>=Math.Max(.5f,node.seconds);
                        hold=!advance;
                        if(!advance)f.concealmentSources|=4;
                        if(!advance&&State.battlefield!=null&&f.revealUntil<=State.battlefield.elapsed)f.hidden=true;
                        if(advance){f.hidden=false;f.revealUntil=(State.battlefield==null?0:State.battlefield.elapsed)+2;}
                        break;
                    case "capture":
                        ObjectiveState objective=State.battlefield==null?null:State.battlefield.objectives.Find(o=>
                            o.id==node.targetId||string.IsNullOrEmpty(node.targetId)&&Distance(o.x,o.y,targetPoint.x,targetPoint.y)<60);
                        advance=objective==null||objective.ownerId==f.ownerId||objective.claimed||f.tacticWait>=Math.Max(3,node.seconds);
                        hold=!advance;break;
                    case "attack":
                        FighterState target=State.fighters.Where(o=>!o.dead&&o.ownerId!=f.ownerId&&CanBattlefieldTarget(f,o)&&Distance(o.x,o.y,targetPoint.x,targetPoint.y)<150)
                            .OrderBy(o=>Distance(f,o)).FirstOrDefault();
                        advance=target==null||f.tacticWait>=Math.Max(1,node.seconds);
                        if(!advance){f.targetId=target.id;point=new Point2(target.x,target.y);return true;}
                        break;
                }
                if(!advance)return true;
                f.tacticIndex++;f.tacticWait=0;hold=false;
            }
            return f.tacticIndex<plan.nodes.Count;
        }
        bool VisibleToPlayer(FighterState fighter,string viewerId)
        {
            if(fighter.ownerId==viewerId||State.phase!="battle"||State.battlefield==null)return true;
            // Display visibility is separate from AI sight/range: only concealment
            // needs discovery. Ordinary enemies and the entire map remain visible.
            if(!fighter.hidden)return true;
            PlayerState viewer=State.players.Find(p=>p.id==viewerId);
            if(viewer==null||viewer.eliminated)return true;
            // A defeated army becomes a spectator for the remainder of this round.
            // Otherwise its final death removes the only source of vision and blanks the arena.
            if(!State.fighters.Any(ally=>!ally.dead&&!ally.neutral&&ally.ownerId==viewerId))return true;
            return State.fighters.Any(ally=>!ally.dead&&ally.ownerId==viewerId&&CanBattlefieldTarget(ally,fighter));
        }
    }
}
