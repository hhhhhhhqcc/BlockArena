using System;
using System.Linq;

namespace BlockArena.Core
{
    public static class BattlefieldCatalog
    {
        public static readonly string[] Maps={"courtyards","bog","frost"};
        public static readonly string[] Weathers={"clear","rain","storm","fog","snow","heat","sandstorm"};
        public static readonly string[] Commanders={"drums","ward","focus","cleanse","smoke","trap","decoy","rift"};
        public static MapDefinition GetMap(string id)
        {
            switch(id){
                case "courtyards":return new MapDefinition{id=id,name="四庭高台",description="四桥通向中央高台；夺取圣坛，利用废墟掩护。雷暴更偏爱高地。",color="#D0AE78",weathers=new[]{"clear","storm"}};
                case "bog":return new MapDefinition{id=id,name="腐沼矿场",description="湿地、密林与残垣交错；两处矿脉与瞭望塔迫使军团分兵。",color="#6B9B79",weathers=new[]{"rain","fog","heat"}};
                case "frost":return new MapDefinition{id=id,name="霜林遗迹",description="穿越结冰河道、争夺中央信标和补给箱；雪林适合伏击。",color="#9AC9D9",weathers=new[]{"snow","fog"}};
                default:throw new InvalidOperationException("未知地图");
            }
        }
        public static WeatherDefinition GetWeather(string id)
        {
            string name,description,color;
            switch(id){
                case "clear":name="晴朗";description="标准环境，无额外修正。";color="#FFE0A1";break;
                case "rain":name="降雨";description="火焰与灼烧 -30%；远程射程 -10%；水生移速 +10%；浇灭林火。";color="#9ACFE4";break;
                case "storm":name="雷暴";description="每10秒预警1.5秒后落雷：8%最大生命伤害、0.5秒眩晕；水域最多连锁3人，高地优先。";color="#B6AEEC";break;
                case "fog":name="浓雾";description="视野 -30%、远程射程 -20%；近战与隐蔽单位移速 +10%；密林伏击更强。";color="#C3D4CD";break;
                case "snow":name="降雪";description="移速 -15%、攻速 -10%；迟缓与眩晕持续 +25%；浅水20秒结冰。";color="#D7F2FF";break;
                case "heat":name="酷热";description="火焰与灼烧 +25%；治疗与再生 -20%；湿地暂时干涸。";color="#F3B06D";break;
                case "sandstorm":name="沙尘暴";description="远程射程 -25%、命中 -25%、视野 -20%；高速近战移速 +10%；废墟可挡风。";color="#D8B981";break;
                default:throw new InvalidOperationException("未知天气");
            }
            return new WeatherDefinition{id=id,name=name,description=description,color=color};
        }
        public static CommanderDefinition GetCommander(string id)
        {
            string name,description,icon;float duration,radius;
            switch(id){
                case "drums":name="战鼓";description="8秒区域内友军攻速 +20%、移速 +10%。";duration=8;radius=110;icon="attack";break;
                case "ward":name="守护结界";description="6秒区域内友军减伤20%；释放时净化一次。";duration=6;radius=105;icon="armor";break;
                case "focus":name="集火标记";description="标记敌人8秒，友军优先攻击它；目标受到伤害 +15%，可被净化。";duration=8;radius=0;icon="attack";break;
                case "cleanse":name="净化号角";description="清除范围内友军火、毒、迟缓、眩晕、定身和集火标记，并给予4秒负面状态抗性。";duration=4;radius=120;icon="heart";break;
                case "smoke":name="烟幕撤退";description="区域持续5秒，友军隐蔽、移速 +30%；攻击会暂时解除隐蔽。";duration=5;radius=115;icon="speed";break;
                case "trap":name="工程陷阱";description="放置隐蔽陷阱，地面敌人触发时受到12点伤害并定身2秒；侦察可发现。";duration=30;radius=36;icon="attack";break;
                case "decoy":name="诱饵傀儡";description="召唤100生命诱饵，存在6秒；吸引附近敌人；无击杀金币。";duration=6;radius=130;icon="armor";break;
                case "rift":name="裂隙传送";description="吟唱0.8秒后，Steve与附近最多3个友军传送到短距离位置。";duration=.8f;radius=85;icon="speed";break;
                default:throw new InvalidOperationException("未知指挥技");
            }
            return new CommanderDefinition{id=id,name=name,description=description,icon=icon,duration=duration,radius=radius};
        }
        static TerrainState Rect(string id,string kind,float x,float y,float width,float depth,float height=0)
        {return new TerrainState{id=id,kind=kind,x=x,y=y,halfWidth=width*.5f,halfHeight=depth*.5f,height=height,hp=kind=="ruins"?150:0,maxHp=kind=="ruins"?150:0};}
        static ObjectiveState Objective(string id,string kind,string name,float x,float y,int reward)
        {return new ObjectiveState{id=id,kind=kind,name=name,x=x,y=y,reward=reward,radius=48};}
        public static BattlefieldState Create(string mapId,string weather,int seed)
        {
            GetMap(mapId);GetWeather(weather);
            var state=new BattlefieldState{mapId=mapId,weather=weather,seed=seed};
            if(mapId=="courtyards"){
                state.terrain.Add(Rect("central-highland","highland",0,0,210,210,1.45f));
                for(int i=0;i<4;i++){
                    Point2 bridge=ArenaGame.LocalToWorld(i,0,177);
                    state.terrain.Add(Rect("bridge-"+i,"bridge",bridge.x,bridge.y,i%2==0?64:150,i%2==0?150:64,.62f));
                    Point2 ruin=ArenaGame.LocalToWorld(i,135,275);
                    state.terrain.Add(Rect("ruin-"+i,"ruins",ruin.x,ruin.y,75,48,.5f));
                }
                // The ring is a gameplay chasm; the four narrow bridges are the only ground crossings.
                state.terrain.Add(new TerrainState{id="moat",kind="chasm",radius=235,halfWidth=135});
                state.objectives.Add(Objective("altar","altar","中央圣坛",0,0,2));
            }else if(mapId=="bog"){
                state.terrain.Add(Rect("west-marsh","wetland",-170,25,220,250));
                state.terrain.Add(Rect("east-marsh","wetland",170,-25,220,250));
                state.terrain.Add(Rect("north-forest","forest",-90,215,220,120));
                state.terrain.Add(Rect("south-forest","forest",90,-215,220,120));
                state.terrain.Add(Rect("west-ruins","ruins",-205,-165,90,65,.45f));
                state.terrain.Add(Rect("east-ruins","ruins",205,165,90,65,.45f));
                state.objectives.Add(Objective("ore-west","mine","西侧矿脉",-220,0,2));
                state.objectives.Add(Objective("ore-east","mine","东侧矿脉",220,0,2));
                state.objectives.Add(Objective("watchtower","tower","瞭望塔",0,110,1));
            }else{
                state.terrain.Add(Rect("frozen-river","water",0,0,600,100));
                state.terrain.Add(Rect("river-bridge","bridge",0,0,68,120,.18f));
                state.terrain.Add(Rect("north-rise","highland",150,155,150,130,.95f));
                state.terrain.Add(Rect("south-rise","highland",-150,-155,150,130,.95f));
                state.terrain.Add(Rect("snow-forest-west","forest",-225,135,140,140));
                state.terrain.Add(Rect("snow-forest-east","forest",225,-135,140,140));
                state.objectives.Add(Objective("beacon","beacon","中央信标",0,0,2));
                state.objectives.Add(Objective("supply-west","supply","西侧补给箱",-220,-150,1));
                state.objectives.Add(Objective("supply-east","supply","东侧补给箱",220,150,1));
            }
            return state;
        }
        public static bool Contains(TerrainState terrain,float x,float y)
        {
            if(terrain==null)return false;
            float dx=x-terrain.x,dy=y-terrain.y;
            if(terrain.kind=="chasm"){
                float d=(float)Math.Sqrt(dx*dx+dy*dy);
                return d>terrain.halfWidth&&d<terrain.radius&&Math.Abs(dx)>32&&Math.Abs(dy)>32;
            }
            return terrain.radius>0?dx*dx+dy*dy<=terrain.radius*terrain.radius:
                Math.Abs(dx)<=terrain.halfWidth&&Math.Abs(dy)<=terrain.halfHeight;
        }
        public static float HeightAt(BattlefieldState state,float x,float y)
        {
            float height=0;if(state==null)return height;
            foreach(var t in state.terrain){
                if(t.kind!="highland"&&t.kind!="bridge")continue;
                if(!Contains(t,x,y))continue;
                float edge=t.radius>0?t.radius-(float)Math.Sqrt((x-t.x)*(x-t.x)+(y-t.y)*(y-t.y)):
                    Math.Min(t.halfWidth-Math.Abs(x-t.x),t.halfHeight-Math.Abs(y-t.y));
                height=Math.Max(height,t.height*Math.Min(1,Math.Max(0,edge/25)));
            }
            return height;
        }
        public static bool IsPassable(BattlefieldState state,float x,float y,bool flying=false,bool aquatic=false,float clearance=0)
        {
            if(float.IsNaN(x)||float.IsNaN(y)||float.IsInfinity(x)||float.IsInfinity(y))return false;
            if(clearance>0){
                if(!IsPassable(state,x,y,flying,aquatic))return false;
                for(int i=0;i<8;i++){
                    double angle=i*Math.PI/4;
                    if(!IsPassable(state,x+(float)Math.Cos(angle)*clearance,y+(float)Math.Sin(angle)*clearance,flying,aquatic))return false;
                }
                return true;
            }
            if(Math.Abs(x)>410||Math.Abs(y)>410)return false;
            if(state==null||flying)return true;
            bool platform=false;
            foreach(var t in state.terrain)if((t.kind=="highland"||t.kind=="bridge")&&Contains(t,x,y)){platform=true;break;}
            foreach(var t in state.terrain)if(t.kind=="chasm"&&Contains(t,x,y)&&!platform)return false;
            // Intact cover has a solid core; broken rubble no longer blocks movement.
            foreach(var t in state.terrain)if(t.kind=="ruins"&&t.hp>0&&Math.Abs(x-t.x)<t.halfWidth*.72f&&Math.Abs(y-t.y)<t.halfHeight*.72f)return false;
            if(state.mapId=="frost"&&!aquatic&&state.weather!="heat"){
                bool river=false,bridge=false;
                foreach(var t in state.terrain){
                    if(t.kind=="water"&&t.freeze<1&&Contains(t,x,y))river=true;
                    if(t.kind=="bridge"&&Contains(t,x,y))bridge=true;
                }
                if(river&&!bridge)return false;
            }
            return true;
        }
        public static bool HasTag(FighterState f,string tag)
        {return f!=null&&!string.IsNullOrEmpty(f.kind)&&CreatureCatalog.Get(f.kind).tags.Contains(tag);}
        public static bool Flying(FighterState f){return f!=null&&CreatureCatalog.Get(f.kind).flying;}
        public static bool Scout(FighterState f)
        {return f!=null&&(Flying(f)||HasTag(f,"illager")||f.kind=="wolf"||f.kind=="ocelot");}
    }
}
