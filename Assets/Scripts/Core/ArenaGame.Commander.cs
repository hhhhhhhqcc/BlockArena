using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockArena.Core
{
    public sealed partial class ArenaGame
    {
        sealed class PendingRift
        {
            public string ownerId,hazardId;
            public Point2 source,destination;
            public float remaining=.8f;
        }
        readonly List<PendingRift> pendingRifts=new List<PendingRift>();

        bool HandleBattlefieldCommand(PlayerState p,GameCommand c)
        {
            if(c.type=="world_options"){
                if(p.id!=State.hostId||State.phase!="lobby")Fail("只有房主可在大厅选择地图和天气");
                string map=string.IsNullOrEmpty(c.mapId)?null:c.mapId;
                string weather=string.IsNullOrEmpty(c.weather)?null:c.weather;
                if(map!=null)BattlefieldCatalog.GetMap(map);
                if(weather!=null)BattlefieldCatalog.GetWeather(weather);
                State.preferredMap=map;State.preferredWeather=weather;return true;
            }
            if(c.type=="commander"){
                bool preparing=State.phase=="prep"||State.phase=="recruit"||State.phase=="tactics";
                if(p.eliminated||!(State.phase=="lobby"||preparing)||preparing&&p.ready)Fail("请在未准备的战前阶段选择指挥技");
                BattlefieldCatalog.GetCommander(c.skillId);p.commanderSkill=c.skillId;return true;
            }
            if(c.type!="cast")return false;
            if(p.eliminated||State.phase!="battle"||State.battlefield==null)Fail("仅能在战斗中释放指挥技");
            if(p.commanderUses<=0)Fail("本场指挥技次数已用完");
            if(p.commanderCooldown>0)Fail("指挥技冷却中");
            if(!Finite(c.x)||!Finite(c.y)||Math.Abs(c.x)>BattlefieldEdge||Math.Abs(c.y)>BattlefieldEdge)Fail("指挥坐标无效");
            CommanderDefinition skill=BattlefieldCatalog.GetCommander(p.commanderSkill);
            FighterState target=null;
            float x=c.x,y=c.y;
            if(skill.id=="focus"){
                target=State.fighters.Find(f=>f.id==c.targetId&&!f.dead&&f.ownerId!=p.id);
                if(target==null)Fail("请标记一个存活敌人");
                x=target.x;y=target.y;
                bool visible=State.fighters.Any(f=>!f.dead&&f.ownerId==p.id&&CanBattlefieldTarget(f,target));
                if(!visible||target.resistanceTime>0)Fail("目标不可见或处于净化抗性中");
            }
            if(Distance(p.steve.x,p.steve.y,x,y)>200)Fail("超出Steve的指挥距离");
            if((skill.id=="trap"||skill.id=="decoy"||skill.id=="rift")&&!BattlefieldCatalog.IsPassable(State.battlefield,x,y))Fail("该处无法放置或传送");
            p.commanderUses--;p.commanderCooldown=8;
            if(skill.id=="drums"||skill.id=="ward"||skill.id=="smoke"||skill.id=="focus"){
                State.battlefield.zones.Add(new CommandZoneState{id=Id("command"),kind=skill.id,ownerId=p.id,slot=p.slot,
                    targetId=target==null?null:target.id,x=x,y=y,radius=skill.radius,remaining=skill.duration});
                if(skill.id=="ward")CleanseArea(p.id,x,y,skill.radius,0);
            }else if(skill.id=="cleanse")CleanseArea(p.id,x,y,skill.radius,4);
            else if(skill.id=="trap")State.battlefield.hazards.Add(new HazardState{id=Id("trap"),kind="trap",ownerId=p.id,x=x,y=y,radius=36,remaining=30});
            else if(skill.id=="decoy"){
                var f=NewFighter(p,new UnitState{id=Id("decoy"),kind="iron_golem",x=x,y=y});
                f.skills=new string[0];f.summoned=true;f.isDecoy=true;f.shield=f.armor=0;
                f.hp=f.maxHp=100;f.attack=0;f.speed=0;f.trapUntil=State.battlefield.elapsed+6;
                State.fighters.Add(f);CacheBattlefieldStats(f);Effect("spawn",x,y,p.slot,source:f,target:f);
            }else if(skill.id=="rift"){
                string id=Id("rift");
                pendingRifts.Add(new PendingRift{ownerId=p.id,hazardId=id,source=p.steve,destination=new Point2(x,y)});
                State.battlefield.hazards.Add(new HazardState{id=id,kind="rift",ownerId=p.id,x=x,y=y,radius=85,delay=.8f,remaining=1.4f});
            }
            // Untriggered traps are presented through their owner-filtered hazard, never a global cast event.
            if(skill.id!="trap")Effect("command_"+skill.id,x,y,p.slot,skill.radius,p.steve.x,p.steve.y);
            return true;
        }
        void CleanseArea(string owner,float x,float y,float radius,float resistance)
        {
            foreach(var f in State.fighters){
                if(f.dead||f.ownerId!=owner||Distance(f.x,f.y,x,y)>radius)continue;
                f.poisonTime=f.poisonTick=f.poisonDps=f.burnTime=f.burnTick=f.burnDps=0;
                f.poisonSource=f.burnSource=null;f.slowTime=f.stunTime=f.rootTime=0;f.slowMultiplier=1;
                f.resistanceTime=Math.Max(f.resistanceTime,resistance);
                State.battlefield.zones.RemoveAll(z=>z.kind=="focus"&&z.targetId==f.id);
                Effect("cleanse",f.x,f.y,f.slot,source:f,target:f);
            }
        }
        void TickCommanders(float dt)
        {
            var world=State.battlefield;
            foreach(var z in world.zones.ToArray()){
                z.remaining-=dt;
                if(z.kind=="focus"){
                    var target=State.fighters.Find(f=>f.id==z.targetId&&!f.dead);
                    if(target==null)z.remaining=0;else{z.x=target.x;z.y=target.y;}
                }
                if(z.remaining<=0)world.zones.Remove(z);
            }
            foreach(var h in world.hazards.ToArray()){
                if(h.kind!="trap"&&h.kind!="rift")continue;
                h.remaining-=dt;h.delay=Math.Max(0,h.delay-dt);
                if(h.kind=="trap"&&!h.triggered){
                    var victim=State.fighters.Find(f=>!f.dead&&f.ownerId!=h.ownerId&&!BattlefieldCatalog.Flying(f)&&Distance(f.x,f.y,h.x,h.y)<=h.radius);
                    if(victim!=null){
                        h.triggered=true;h.remaining=.65f;
                        // A commander trap has an owner, but is not a creature attack and cannot reflect.
                        var source=new FighterState{ownerId=h.ownerId,slot=Player(h.ownerId).slot,kind="iron_golem",skills=new string[0],summoned=true,dead=true};
                        Damage(victim,12,source);
                        if(victim.resistanceTime<=0)victim.rootTime=Math.Max(victim.rootTime,2*victim.statusDurationMultiplier);
                        Effect("command_trap",h.x,h.y,source.slot,h.radius,target:victim);
                    }
                }
                if(h.remaining<=0)world.hazards.Remove(h);
            }
            foreach(var pending in pendingRifts.ToArray()){
                pending.remaining-=dt;if(pending.remaining>0)continue;
                pendingRifts.Remove(pending);
                PlayerState p=State.players.Find(player=>player.id==pending.ownerId);
                var h=world.hazards.Find(hazard=>hazard.id==pending.hazardId);if(h!=null)h.triggered=true;
                if(p==null||p.eliminated||Distance(p.steve.x,p.steve.y,pending.source.x,pending.source.y)>25)continue;
                var allies=State.fighters.Where(f=>!f.dead&&!f.isDecoy&&f.ownerId==p.id&&Distance(f.x,f.y,pending.source.x,pending.source.y)<=85)
                    .OrderBy(f=>Distance(f.x,f.y,pending.source.x,pending.source.y)).Take(3).ToArray();
                p.steve=pending.destination;
                for(int i=0;i<allies.Length;i++){
                    var f=allies[i];float a=i*2.094395f;
                    var at=new Point2(Clamp(pending.destination.x+(float)Math.Cos(a)*24,-BattlefieldEdge,BattlefieldEdge),Clamp(pending.destination.y+(float)Math.Sin(a)*24,-BattlefieldEdge,BattlefieldEdge));
                    if(!BattlefieldCatalog.IsPassable(world,at.x,at.y,BattlefieldCatalog.Flying(f),Aquatic(f)))at=pending.destination;
                    Effect("teleport",f.x,f.y,f.slot,0,at.x,at.y,f,f);f.x=at.x;f.y=at.y;f.targetId=null;f.think=0;
                }
                Effect("command_rift",p.steve.x,p.steve.y,p.slot,85,pending.source.x,pending.source.y);
            }
            foreach(var f in State.fighters)if(!f.dead&&f.isDecoy&&world.elapsed>=f.trapUntil)Kill(f);
            foreach(var p in State.players){
                if(!Automated(p)||p.eliminated||p.commanderUses<=0||p.commanderCooldown>0||world.elapsed<3)continue;
                var allies=State.fighters.Where(f=>!f.dead&&f.ownerId==p.id&&!f.isDecoy).ToArray();
                if(allies.Length==0)continue;
                var centre=allies.OrderBy(f=>Distance(f.x,f.y,p.steve.x,p.steve.y)).First();
                if(Distance(centre.x,centre.y,p.steve.x,p.steve.y)>190)continue;
                var command=new GameCommand{type="cast",x=centre.x,y=centre.y};
                if(p.commanderSkill=="focus"){
                    var enemy=State.fighters.Find(f=>!f.dead&&f.ownerId!=p.id&&Distance(f.x,f.y,p.steve.x,p.steve.y)<190);
                    if(enemy==null)continue;command.targetId=enemy.id;
                }
                try{HandleBattlefieldCommand(p,command);}catch(InvalidOperationException){/* A moving target may leave cast range. */}
            }
        }
    }
}
