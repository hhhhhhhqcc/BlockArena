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
                case "courtyards":return new MapDefinition{id=id,name="四庭高台",description="四条古代仪式长桥汇于断崖圣坛；侧庭补给与中央高地形成两条争夺路线。雷暴迫使守军离开高台。",color="#D0AE78",weathers=new[]{"clear","storm"}};
                case "bog":return new MapDefinition{id=id,name="腐沼矿场",description="弯曲沼河贯穿废弃矿场；沿岸矿道通向两处矿脉，中央干地瞭望塔控制分岔。",color="#6B9B79",weathers=new[]{"rain","fog","heat"}};
                case "frost":return new MapDefinition{id=id,name="霜林遗迹",description="曲折冰河划开古代林地；三座石桥连接双侧山脊，雪后结冰会打开通往信标的捷径。",color="#9AC9D9",weathers=new[]{"snow","fog"}};
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
                state.terrain.Add(new TerrainState{id="moat",kind="chasm",radius=235,halfWidth=135,halfHeight=32});
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
        // Production battlefields have four times the playable area. Keep Create at the
        // original size for saved fixtures and isolated legacy rule checks.
        public static BattlefieldState CreateExpanded(string mapId,string weather,int seed)
        {
            var state=Create(mapId,weather,seed);
            state.scale=2;state.halfExtent=820;
            state.terrain.Clear();state.objectives.Clear();
            // Designed as one connected landscape. Randomness moves the four armies,
            // not the bridges, shorelines or rewards that players need to learn.
            if(mapId=="courtyards"){
                state.terrain.Add(Rect("central-highland","highland",0,0,460,460,1.65f));
                state.terrain.Add(new TerrainState{id="moat",kind="chasm",radius=510,halfWidth=300,halfHeight=76});
                for(int side=0;side<4;side++){
                    Point2 bridge=ArenaGame.LocalToWorld(side,0,380);
                    state.terrain.Add(Rect("bridge-"+side,"bridge",bridge.x,bridge.y,side%2==0?136:340,side%2==0?340:136,.7f));
                    Point2 ruin=ArenaGame.LocalToWorld(side,200,560);
                    state.terrain.Add(Rect("ruin-"+side,"ruins",ruin.x,ruin.y,side%2==0?110:70,side%2==0?70:110,.65f));
                    Point2 reward=ArenaGame.LocalToWorld(side,-120,600);
                    state.objectives.Add(Objective("courtyard-cache-"+side,"supply","古庭补给",reward.x,reward.y,1));
                }
                state.objectives.Insert(0,Objective("altar","altar","中央圣坛",0,0,2));
            }else if(mapId=="bog"){
                state.terrain.Add(new TerrainState{id="marsh-river",kind="wetland",shape="river",halfWidth=690,halfHeight=92,radius=112});
                state.terrain.Add(Shaped("north-forest","forest","ellipse",-165,320,410,250,16));
                state.terrain.Add(Shaped("south-forest","forest","ellipse",165,-320,410,250,16));
                state.terrain.Add(Shaped("west-grove","forest","ellipse",-530,-160,190,280,-18));
                state.terrain.Add(Shaped("east-grove","forest","ellipse",530,160,190,280,-18));
                state.terrain.Add(Rect("mine-rise-west","highland",-375,205,190,160,.52f));
                state.terrain.Add(Rect("mine-rise-east","highland",375,-205,190,160,.52f));
                state.terrain.Add(Rect("river-crossing","bridge",0,0,135,245,.22f));
                state.terrain.Add(Rect("west-ruins","ruins",-165,-175,104,74,.55f));
                state.terrain.Add(Rect("east-ruins","ruins",165,175,104,74,.55f));
                state.objectives.Add(Objective("ore-west","mine","沉岸矿脉",-375,205,2));
                state.objectives.Add(Objective("ore-east","mine","苔岩矿脉",375,-205,2));
                state.objectives.Add(Objective("watchtower","tower","岔口瞭望塔",0,0,1));
            }else{
                state.terrain.Add(new TerrainState{id="frozen-river",kind="water",shape="river",halfWidth=690,halfHeight=58,radius=106});
                state.terrain.Add(Rect("river-bridge","bridge",0,0,146,205,.24f));
                for(int sign=-1;sign<=1;sign+=2){
                    float x=sign*495,y=(float)Math.Sin(x/690*Math.PI)*106;
                    state.terrain.Add(Rect("side-bridge-"+sign,"bridge",x,y,100,190,.2f));
                }
                state.terrain.Add(Rect("north-rise","highland",295,310,230,190,1.15f));
                state.terrain.Add(Rect("south-rise","highland",-295,-310,230,190,1.15f));
                state.terrain.Add(Shaped("snow-forest-west","forest","ellipse",-340,255,390,285,-20));
                state.terrain.Add(Shaped("snow-forest-east","forest","ellipse",340,-255,390,285,-20));
                state.terrain.Add(Shaped("snow-grove-west","forest","ellipse",-520,-155,190,220,14));
                state.terrain.Add(Shaped("snow-grove-east","forest","ellipse",520,155,190,220,14));
                state.objectives.Add(Objective("beacon","beacon","中央信标",0,0,2));
                state.objectives.Add(Objective("supply-west","supply","林隘补给",-485,-245,1));
                state.objectives.Add(Objective("supply-east","supply","霜岸补给",485,245,1));
            }
            return state;
        }
        static TerrainState Shaped(string id,string kind,string shape,float x,float y,float width,float depth,float rotation)
        {var terrain=Rect(id,kind,x,y,width,depth);terrain.shape=shape;terrain.rotation=rotation;return terrain;}
        public static Point2 TerrainPoint(TerrainState terrain,float localX,float localY)
        {
            double a=terrain.rotation*Math.PI/180;
            return new Point2(terrain.x+localX*(float)Math.Cos(a)-localY*(float)Math.Sin(a),terrain.y+localX*(float)Math.Sin(a)+localY*(float)Math.Cos(a));
        }
        public static float RiverCentre(TerrainState terrain,float along)
        {return terrain.halfWidth>0?(float)Math.Sin(along/terrain.halfWidth*Math.PI)*terrain.radius:0;}
        public static float RiverWidth(TerrainState terrain,float along)
        {return terrain.halfHeight*(.94f+.06f*(float)Math.Cos(along/Math.Max(1,terrain.halfWidth)*Math.PI*2));}
        public static float Scale(BattlefieldState state){return state!=null&&state.scale>0?state.scale:1;}
        public static float Extent(BattlefieldState state){return state!=null&&state.halfExtent>0?state.halfExtent:410;}
        public static bool Contains(TerrainState terrain,float x,float y)
        {
            if(terrain==null)return false;
            float dx=x-terrain.x,dy=y-terrain.y;
            if(terrain.rotation!=0){double angle=-terrain.rotation*Math.PI/180;float rx=dx*(float)Math.Cos(angle)-dy*(float)Math.Sin(angle);dy=dx*(float)Math.Sin(angle)+dy*(float)Math.Cos(angle);dx=rx;}
            if(terrain.shape=="river")return Math.Abs(dx)<=terrain.halfWidth&&Math.Abs(dy-RiverCentre(terrain,dx))<=RiverWidth(terrain,dx);
            if(terrain.shape=="ellipse")return terrain.halfWidth>0&&terrain.halfHeight>0&&dx*dx/(terrain.halfWidth*terrain.halfWidth)+dy*dy/(terrain.halfHeight*terrain.halfHeight)<=1;
            if(terrain.kind=="chasm"){
                float d=(float)Math.Sqrt(dx*dx+dy*dy);
                float passage=terrain.halfHeight>0?terrain.halfHeight:32;
                return d>terrain.halfWidth&&d<terrain.radius&&Math.Abs(dx)>passage&&Math.Abs(dy)>passage;
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
            float extent=Extent(state);
            if(Math.Abs(x)>extent||Math.Abs(y)>extent)return false;
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
