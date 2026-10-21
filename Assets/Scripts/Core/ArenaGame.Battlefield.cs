using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockArena.Core
{
    public sealed partial class ArenaGame
    {
        readonly Dictionary<string,int> battlefieldGold=new Dictionary<string,int>();
        readonly HashSet<string> objectiveRewards=new HashSet<string>();
        readonly Dictionary<string,int> battlefieldRangedHits=new Dictionary<string,int>();
        readonly List<Point2> routeNodes=new List<Point2>();
        float[,] routeDistances;
        string routeMap;
        float nextStormAt=10,nextLavaAt=8;

        void InitializeBattlefield(){InitializeBattlefieldCore(false);}
        void InitializeBattlefieldCore(bool deferWeather)
        {
            string map=string.IsNullOrEmpty(State.preferredMap)?BattlefieldCatalog.Maps[random.Next(BattlefieldCatalog.Maps.Length)]:State.preferredMap;
            State.battlefield=BattlefieldCatalog.Create(map,"clear",random.Next());
            if(!deferWeather)SelectBattlefieldWeather();
            foreach(var p in State.players)p.steve=BattlefieldSafePoint(p.steve,false,false,9);
            battlefieldGold.Clear();objectiveRewards.Clear();battlefieldRangedHits.Clear();pendingRifts.Clear();
            nextStormAt=10;nextLavaAt=8;
        }
        void SelectBattlefieldWeather()
        {
            if(State.battlefield==null)return;
            MapDefinition definition=BattlefieldCatalog.GetMap(State.battlefield.mapId);
            State.battlefield.weather=string.IsNullOrEmpty(State.preferredWeather)?definition.weathers[random.Next(definition.weathers.Length)]:State.preferredWeather;
        }
        void StartBattlefieldCombat()
        {
            pendingRifts.Clear();routeMap=null;routeDistances=null;
            if(State.battlefield!=null){State.battlefield.elapsed=0;State.battlefield.hazards.Clear();State.battlefield.zones.Clear();}
            foreach(var p in State.players){p.commanderUses=2;p.commanderCooldown=0;}
            foreach(var f in State.fighters){
                CacheBattlefieldStats(f);
                NormalizeBattlefieldPosition(f);
            }
        }
        static void CacheBattlefieldStats(FighterState f)
        {
            if(f.baseAttackSpeed>0)return;
            f.baseSpeed=f.speed;f.baseRange=f.range;f.baseAttack=f.attack;
            f.baseAttackSpeed=Math.Max(.01f,f.attackSpeed);f.baseArmor=f.armor;
        }
        TerrainState TerrainAt(string kind,float x,float y)
        {return State.battlefield==null?null:State.battlefield.terrain.Find(t=>t.kind==kind&&BattlefieldCatalog.Contains(t,x,y));}
        static bool FireUnit(FighterState f)
        {return f!=null&&(f.kind=="blaze"||f.kind=="ghast"||f.kind=="magma_cube"||f.kind=="strider"||f.kind=="ender_dragon"||CreatureAbilities.Has(f,"burn"));}
        static bool Aquatic(FighterState f){return BattlefieldCatalog.HasTag(f,"aquatic");}
        bool TowerOwned(string owner)
        {return State.battlefield!=null&&State.battlefield.objectives.Any(o=>o.kind=="tower"&&o.ownerId==owner);}
        bool InWater(FighterState f)
        {
            var world=State.battlefield;if(world==null||world.weather=="heat"||BattlefieldCatalog.Flying(f)||TerrainAt("bridge",f.x,f.y)!=null)return false;
            return world.terrain.Any(t=>(t.kind=="wetland"||t.kind=="water")&&t.freeze<1&&BattlefieldCatalog.Contains(t,f.x,f.y));
        }
        void TickBattlefield(float dt)
        {
            BattlefieldState world=State.battlefield;if(world==null)return;
            world.elapsed+=dt;
            foreach(var p in State.players)p.commanderCooldown=Math.Max(0,p.commanderCooldown-dt);
            foreach(var t in world.terrain){
                if(t.kind=="water")t.freeze=world.weather=="snow"?Math.Min(1,world.elapsed/20):0;
                if(world.weather=="rain")t.burnTime=0;
                else t.burnTime=Math.Max(0,t.burnTime-dt);
            }
            TickCommanders(dt);
            foreach(var f in State.fighters.ToArray()){
                if(f.dead)continue;
                CacheBattlefieldStats(f);
                f.speed=f.baseSpeed;f.attackSpeed=f.baseAttackSpeed;f.range=f.baseRange;
                f.attack=f.baseAttack;f.armor=f.baseArmor;f.commandGuard=0;f.commandHaste=0;
                f.environmentDamage=world.weather=="rain"?.7f:world.weather=="heat"?1.25f:1;
                f.healingMultiplier=world.weather=="heat"?.8f:1;
                f.statusDurationMultiplier=world.weather=="snow"?1.25f:1;
                f.resistanceTime=Math.Max(0,f.resistanceTime-dt);f.rootTime=Math.Max(0,f.rootTime-dt);
                bool flying=BattlefieldCatalog.Flying(f),aquatic=Aquatic(f),ranged=f.baseRange>70;
                switch(world.weather){
                    case "rain":if(ranged)f.range*=.9f;if(aquatic)f.speed*=1.1f;break;
                    case "fog":if(ranged)f.range*=.8f;if(!ranged||f.hidden)f.speed*=1.1f;break;
                    case "snow":f.speed*=.85f;f.attackSpeed*=.9f;break;
                    case "sandstorm":if(ranged)f.range*=.75f;if(!ranged&&f.baseSpeed>=60)f.speed*=1.1f;break;
                }
                if(!flying&&InWater(f)){
                    if(aquatic)f.speed*=1.15f;else{f.speed*=.8f;f.attackSpeed*=.9f;}
                }
                if(!flying&&BattlefieldCatalog.HeightAt(world,f.x,f.y)>.35f&&ranged)f.range*=1.15f;
                f.hidden=!f.neutral&&world.elapsed>=f.revealUntil&&
                    TerrainAt("forest",f.x,f.y)!=null;
                foreach(var zone in world.zones){
                    if(zone.ownerId!=f.ownerId||Distance(f.x,f.y,zone.x,zone.y)>zone.radius)continue;
                    if(zone.kind=="drums"){f.attackSpeed*=1.2f;f.speed*=1.1f;f.commandHaste=.2f;}
                    else if(zone.kind=="ward")f.commandGuard=Math.Max(f.commandGuard,.2f);
                    else if(zone.kind=="smoke"){
                        f.speed*=1.3f;
                        if(world.elapsed>=f.revealUntil)f.hidden=true;
                    }
                }
                if(world.objectives.Any(o=>o.kind=="altar"&&o.ownerId==f.ownerId))f.attackSpeed*=1.05f;
                if(world.objectives.Any(o=>o.kind=="beacon"&&o.ownerId==f.ownerId))f.hp=Math.Min(f.maxHp,f.hp+f.maxHp*.01f*dt*f.healingMultiplier);
                if(f.isDecoy)continue;
                foreach(var t in world.terrain){
                    if(!BattlefieldCatalog.Contains(t,f.x,f.y)||flying)continue;
                    if(t.kind=="forest"&&t.burnTime>0)Damage(f,f.maxHp*.02f*dt*f.environmentDamage,null);
                    if(t.kind=="lava"&&!FireUnit(f))Damage(f,f.maxHp*.035f*dt,null);
                }
            }
            TickObjectives(dt);
            if(world.weather=="storm"&&world.elapsed>=nextStormAt){
                nextStormAt+=10;ScheduleLightning();
            }
            if(world.elapsed>=nextLavaAt){
                nextLavaAt+=8;
                foreach(var t in world.terrain)if(t.kind=="lava")world.hazards.Add(new HazardState{id=Id("eruption"),kind="eruption",x=t.x,y=t.y,radius=Math.Max(45,t.radius),delay=1.5f,remaining=2.2f});
            }
            TickWeatherHazards(dt);
        }
        void TickObjectives(float dt)
        {
            foreach(var o in State.battlefield.objectives){
                if(o.kind=="supply"&&o.claimed)continue;
                string owner=null;bool contested=false;
                foreach(var f in State.fighters){
                    if(f.dead||f.neutral||f.isDecoy||Distance(f.x,f.y,o.x,o.y)>o.radius)continue;
                    if(owner==null)owner=f.ownerId;else if(owner!=f.ownerId){contested=true;break;}
                }
                if(contested)continue;
                if(owner==null){o.capture=Math.Max(0,o.capture-dt/6);if(o.capture<=0)o.contenderId=null;continue;}
                if(owner==o.ownerId){o.capture=0;o.contenderId=null;continue;}
                if(o.contenderId!=owner){o.contenderId=owner;o.capture=0;}
                o.capture=Math.Min(1,o.capture+dt/3);
                if(o.capture<1)continue;
                o.ownerId=owner;o.contenderId=null;o.capture=0;
                PlayerState p=State.players.Find(player=>player.id==owner);if(p==null)continue;
                o.slot=p.slot;
                string rewardKey=o.id+"/"+owner;
                if(objectiveRewards.Add(rewardKey)){
                    int paid; battlefieldGold.TryGetValue(owner,out paid);
                    int gold=Math.Min(Math.Min(2,o.reward),Math.Max(0,4-paid));
                    p.gold+=gold;p.eventIncome+=gold;battlefieldGold[owner]=paid+gold;
                    Effect("objective_capture",o.x,o.y,p.slot,gold);
                }
                if(o.kind=="supply"){
                    o.claimed=true;
                    foreach(var ally in State.fighters)if(!ally.dead&&ally.ownerId==owner&&Distance(ally.x,ally.y,o.x,o.y)<100){
                        float healed=Math.Min(ally.maxHp-ally.hp,ally.maxHp*.2f*ally.healingMultiplier);
                        ally.hp+=healed;Effect("heal",ally.x,ally.y,ally.slot,healed,ally.x,ally.y,ally,ally);
                    }
                }
            }
        }
        void ScheduleLightning()
        {
            FighterState target=null;float score=float.MinValue;
            foreach(var f in State.fighters){
                if(f.dead||f.neutral||f.isDecoy)continue;
                float candidate=BattlefieldCatalog.HeightAt(State.battlefield,f.x,f.y)*100+(float)random.NextDouble()*80;
                if(candidate>score){score=candidate;target=f;}
            }
            if(target==null)return;
            State.battlefield.hazards.Add(new HazardState{id=Id("lightning"),kind="lightning",x=target.x,y=target.y,radius=58,delay=1.5f,remaining=2.25f});
        }
        void TickWeatherHazards(float dt)
        {
            var world=State.battlefield;
            foreach(var h in world.hazards.ToArray()){
                if(h.kind=="trap"||h.kind=="rift")continue;
                h.delay=Math.Max(0,h.delay-dt);h.remaining-=dt;
                if(!h.triggered&&h.delay<=0){
                    h.triggered=true;
                    Effect(h.kind=="lightning"?"lightning":"eruption",h.x,h.y,-1,h.radius);
                    var victims=State.fighters.Where(f=>!f.dead&&!f.isDecoy&&Distance(f.x,f.y,h.x,h.y)<=h.radius).ToList();
                    foreach(var f in victims){
                        if(h.kind=="eruption"&&(BattlefieldCatalog.Flying(f)||FireUnit(f)))continue;
                        Damage(f,f.maxHp*(h.kind=="lightning"?.08f:.1f),null);
                        if(h.kind=="lightning"&&f.resistanceTime<=0)f.stunTime=Math.Max(f.stunTime,.5f*f.statusDurationMultiplier);
                    }
                    if(h.kind=="lightning"&&victims.Any(InWater)){
                        int jumps=0;
                        foreach(var f in State.fighters.ToArray()){
                            if(jumps>=3)break;
                            if(f.dead||victims.Contains(f)||!InWater(f)||!victims.Any(v=>Distance(v,f)<=150))continue;
                            Damage(f,f.maxHp*.04f,null);
                            if(f.resistanceTime<=0)f.stunTime=Math.Max(f.stunTime,.25f);
                            Effect("lightning",f.x,f.y,-1,25);jumps++;
                        }
                    }
                }
                if(h.remaining<=0)world.hazards.Remove(h);
            }
        }
        float ModifyBattlefieldDamage(FighterState target,float amount,FighterState attacker)
        {
            var world=State.battlefield;if(world==null||ReferenceEquals(target,attacker))return amount;
            amount*=1-target.commandGuard;
            if(world.zones.Any(z=>z.kind=="focus"&&z.targetId==target.id&&z.ownerId!=target.ownerId))amount*=1.15f;
            if(attacker==null||CreatureAbilities.PeriodicDamage)return amount;
            if(FireUnit(attacker)){
                amount*=world.weather=="rain"?.7f:world.weather=="heat"?1.25f:1;
                var forest=TerrainAt("forest",target.x,target.y);
                if(forest!=null&&world.weather!="rain")forest.burnTime=Math.Max(forest.burnTime,6);
            }
            if(attacker.baseRange>70||attacker.range>70){
                bool forest=TerrainAt("forest",target.x,target.y)!=null;
                TerrainState ruin=TerrainAt("ruins",target.x,target.y);
                int count;battlefieldRangedHits.TryGetValue(target.id,out count);count++;battlefieldRangedHits[target.id]=count;
                float miss=(forest?.2f:0)+(world.weather=="sandstorm"&&TerrainAt("ruins",attacker.x,attacker.y)==null?.25f:0);
                if(Math.Floor(count*(double)miss)>Math.Floor((count-1)*(double)miss))return 0;
                if(!BattlefieldCatalog.Flying(attacker)&&BattlefieldCatalog.HeightAt(world,attacker.x,attacker.y)>.35f)amount*=1.08f;
                if(ruin!=null&&ruin.hp>0){
                    // Ruins cover incoming fire from the outside edge; flanking from the centre bypasses cover.
                    float outer=(attacker.x-ruin.x)*ruin.x+(attacker.y-ruin.y)*ruin.y;
                    if(outer>=0){
                        float absorbed=amount*.25f;ruin.hp=Math.Max(0,ruin.hp-absorbed);amount-=absorbed;
                        if(ruin.hp<=0)Effect("cover_break",ruin.x,ruin.y,-1,0);
                    }
                }
            }
            return amount;
        }
        bool CanBattlefieldTarget(FighterState source,FighterState target)
        {
            var world=State.battlefield;if(world==null)return true;
            float distance=Distance(source,target);
            bool scout=BattlefieldCatalog.Scout(source)||TowerOwned(source.ownerId);
            float vision=world.weather=="fog"?385:world.weather=="sandstorm"?440:float.PositiveInfinity;
            if(scout)vision*=1.3f;
            if(distance>vision)return false;
            if(target.hidden&&!scout&&distance>(world.weather=="fog"?65:100))return false;
            return true;
        }
        FighterState PreferredCommanderTarget(FighterState source)
        {
            if(State.battlefield==null)return null;
            FighterState decoy=null;float nearest=130;
            foreach(var f in State.fighters){
                if(f.dead||!f.isDecoy||f.ownerId==source.ownerId)continue;
                float distance=Distance(source,f);if(distance<nearest){nearest=distance;decoy=f;}
            }
            if(decoy!=null)return decoy;
            foreach(var zone in State.battlefield.zones){
                if(zone.kind!="focus"||zone.ownerId!=source.ownerId)continue;
                var target=State.fighters.Find(f=>f.id==zone.targetId&&!f.dead&&f.ownerId!=source.ownerId);
                if(target!=null&&CanBattlefieldTarget(source,target))return target;
            }
            return null;
        }
        Point2 BattlefieldWaypoint(FighterState f,Point2 destination)
        {
            var world=State.battlefield;
            if(world==null||BattlefieldCatalog.Flying(f))return destination;
            if(world.mapId=="frost"&&Aquatic(f))return destination;
            if(f.navigationUntil>clock&&Distance(destination.x,destination.y,f.navigationGoal.x,f.navigationGoal.y)<24&&
                Distance(f.x,f.y,f.navigationWaypoint.x,f.navigationWaypoint.y)>8)return f.navigationWaypoint;
            f.navigationGoal=destination;f.navigationUntil=clock+.45f;
            f.navigationWaypoint=NavigationWaypoint(new Point2(f.x,f.y),destination);return f.navigationWaypoint;
        }
        Point2 NavigationWaypoint(Point2 start,Point2 destination)
        {
            if(State.battlefield==null)return destination;
            destination=BattlefieldSafePoint(destination,false,false,10);
            if(BattlefieldLineClear(start,destination))return destination;
            EnsureBattlefieldRoutes();
            int count=routeNodes.Count;float best=float.MaxValue;Point2 next=start;
            var from=new float[count];var to=new float[count];
            for(int i=0;i<count;i++){
                float distance=Distance(start.x,start.y,routeNodes[i].x,routeNodes[i].y);
                from[i]=distance>1&&BattlefieldLineClear(start,routeNodes[i])?distance:float.PositiveInfinity;
                to[i]=BattlefieldLineClear(routeNodes[i],destination)?Distance(destination.x,destination.y,routeNodes[i].x,routeNodes[i].y):float.PositiveInfinity;
            }
            for(int i=0;i<count;i++)for(int j=0;j<count;j++){
                float cost=from[i]+routeDistances[i,j]+to[j];
                if(cost<best){best=cost;next=routeNodes[i];}
            }
            return next;
        }
        void EnsureBattlefieldRoutes()
        {
            string map=State.battlefield.mapId;
            string topology=map+":"+string.Join(",",State.battlefield.terrain.Where(t=>t.kind=="ruins"&&t.hp>0||t.kind=="water"&&t.freeze>=1).Select(t=>t.id));
            if(routeMap==topology&&routeDistances!=null)return;
            routeMap=topology;routeNodes.Clear();
            if(map=="courtyards"){
                routeNodes.Add(new Point2(0,0));
                for(int i=0;i<4;i++){routeNodes.Add(LocalToWorld(i,0,265));routeNodes.Add(LocalToWorld(i,0,110));}
                routeNodes.Add(new Point2(270,270));routeNodes.Add(new Point2(-270,270));
                routeNodes.Add(new Point2(-270,-270));routeNodes.Add(new Point2(270,-270));
            }else{
                foreach(float x in new[]{-325f,0,325f}){routeNodes.Add(new Point2(x,85));routeNodes.Add(new Point2(x,-85));}
                routeNodes.Add(new Point2(-325,0));routeNodes.Add(new Point2(325,0));
            }
            foreach(var ruin in State.battlefield.terrain.Where(t=>t.kind=="ruins"&&t.hp>0)){
                float width=ruin.halfWidth*.72f+16,depth=ruin.halfHeight*.72f+16;
                foreach(float dx in new[]{-width,width})foreach(float dy in new[]{-depth,depth}){
                    var corner=new Point2(ruin.x+dx,ruin.y+dy);
                    if(BattlefieldCatalog.IsPassable(State.battlefield,corner.x,corner.y,false,false,10))routeNodes.Add(corner);
                }
            }
            int n=routeNodes.Count;routeDistances=new float[n,n];
            for(int i=0;i<n;i++)for(int j=0;j<n;j++)routeDistances[i,j]=BattlefieldLineClear(routeNodes[i],routeNodes[j])?
                Distance(routeNodes[i].x,routeNodes[i].y,routeNodes[j].x,routeNodes[j].y):float.PositiveInfinity;
            for(int k=0;k<n;k++)for(int i=0;i<n;i++)for(int j=0;j<n;j++)routeDistances[i,j]=Math.Min(routeDistances[i,j],routeDistances[i,k]+routeDistances[k,j]);
        }
        bool BattlefieldLineClear(Point2 from,Point2 to)
        {
            int samples=Math.Max(1,(int)Math.Ceiling(Distance(from.x,from.y,to.x,to.y)/10));
            for(int i=1;i<=samples;i++)if(!BattlefieldCatalog.IsPassable(State.battlefield,from.x+(to.x-from.x)*i/samples,from.y+(to.y-from.y)*i/samples,false,false,10))return false;
            return true;
        }
        void NormalizeBattlefieldPosition(FighterState f)
        {
            if(f==null||State.battlefield==null||BattlefieldCatalog.IsPassable(State.battlefield,f.x,f.y,BattlefieldCatalog.Flying(f),Aquatic(f),BattlefieldCatalog.Flying(f)?0:10))return;
            Point2 safe=BattlefieldSafePoint(new Point2(f.x,f.y),BattlefieldCatalog.Flying(f),Aquatic(f),BattlefieldCatalog.Flying(f)?0:10);
            f.x=safe.x;f.y=safe.y;
        }
        Point2 BattlefieldSafePoint(Point2 point,bool flying=false,bool aquatic=false,float clearance=0)
        {
            float edge=410-clearance;
            point.x=Finite(point.x)?Clamp(point.x,-edge,edge):0;point.y=Finite(point.y)?Clamp(point.y,-edge,edge):0;
            if(BattlefieldCatalog.IsPassable(State.battlefield,point.x,point.y,flying,aquatic,clearance))return point;
            for(float radius=5;radius<=800;radius+=5)for(int i=0;i<32;i++){
                double angle=i*Math.PI/16;
                var candidate=new Point2(point.x+(float)Math.Cos(angle)*radius,point.y+(float)Math.Sin(angle)*radius);
                if(BattlefieldCatalog.IsPassable(State.battlefield,candidate.x,candidate.y,flying,aquatic,clearance))return candidate;
            }
            return new Point2(0,0);
        }
        Point2 MoveOnBattlefield(FighterState f,Point2 desired)
        {
            var world=State.battlefield;bool flying=BattlefieldCatalog.Flying(f);
            desired.x=Clamp(desired.x,-410,410);desired.y=Clamp(desired.y,-410,410);
            if(world==null||flying)return desired;
            float oldHeight=BattlefieldCatalog.HeightAt(world,f.x,f.y),newHeight=BattlefieldCatalog.HeightAt(world,desired.x,desired.y);
            if(f.baseRange<=70&&newHeight>oldHeight+.001f){desired.x=f.x+(desired.x-f.x)*.9f;desired.y=f.y+(desired.y-f.y)*.9f;}
            bool aquatic=Aquatic(f);
            if(BattlefieldCatalog.IsPassable(world,desired.x,desired.y,false,aquatic,10))return desired;
            if(BattlefieldCatalog.IsPassable(world,desired.x,f.y,false,aquatic,10))return new Point2(desired.x,f.y);
            if(BattlefieldCatalog.IsPassable(world,f.x,desired.y,false,aquatic,10))return new Point2(f.x,desired.y);
            return new Point2(f.x,f.y);
        }
        void TickSteveNavigation(PlayerState player,float dt)
        {
            if(State.phase!="recruit"&&State.phase!="prep"&&State.phase!="tactics"&&State.phase!="battle"){
                player.steveNavigating=false;return;
            }
            var destination=player.steveDestination;
            Point2 waypoint=(State.phase=="battle"||State.phase=="tactics")?NavigationWaypoint(player.steve,destination):destination;
            float distance=Distance(player.steve.x,player.steve.y,waypoint.x,waypoint.y);
            if(Distance(player.steve.x,player.steve.y,destination.x,destination.y)<=2){player.steveNavigating=false;return;}
            if(distance<.001f){player.steveNavigating=false;return;}
            float step=Math.Min(distance,130*dt);
            Point2 next=new Point2(player.steve.x+(waypoint.x-player.steve.x)/distance*step,player.steve.y+(waypoint.y-player.steve.y)/distance*step);
            if(BattlefieldCatalog.IsPassable(State.battlefield,next.x,next.y,false,false,9))player.steve=next;
            else player.steveNavigating=false;
        }
        BattlefieldState CopyBattlefield(BattlefieldState source,string viewerId)
        {
            if(source==null)return null;
            var copy=new BattlefieldState{mapId=source.mapId,weather=source.weather,seed=source.seed,elapsed=source.elapsed,revealAge=source.revealAge};
            foreach(var t in source.terrain)copy.terrain.Add(new TerrainState{id=t.id,kind=t.kind,x=t.x,y=t.y,radius=t.radius,halfWidth=t.halfWidth,halfHeight=t.halfHeight,height=t.height,hp=t.hp,maxHp=t.maxHp,burnTime=t.burnTime,freeze=t.freeze});
            foreach(var o in source.objectives)copy.objectives.Add(new ObjectiveState{id=o.id,kind=o.kind,name=o.name,ownerId=o.ownerId,contenderId=o.contenderId,x=o.x,y=o.y,radius=o.radius,capture=o.capture,reward=o.reward,slot=o.slot,claimed=o.claimed});
            foreach(var h in source.hazards){
                if(h.kind=="trap"&&!h.triggered&&h.ownerId!=viewerId){
                    bool scout=TowerOwned(viewerId)||State.fighters.Any(f=>!f.dead&&f.ownerId==viewerId&&BattlefieldCatalog.Scout(f)&&Distance(f.x,f.y,h.x,h.y)<=160);
                    if(!scout)continue;
                }
                copy.hazards.Add(new HazardState{id=h.id,kind=h.kind,ownerId=h.ownerId,x=h.x,y=h.y,radius=h.radius,delay=h.delay,remaining=h.remaining,triggered=h.triggered});
            }
            foreach(var z in source.zones)copy.zones.Add(new CommandZoneState{id=z.id,kind=z.kind,ownerId=z.ownerId,targetId=z.targetId,slot=z.slot,x=z.x,y=z.y,radius=z.radius,remaining=z.remaining});
            return copy;
        }
    }
}
