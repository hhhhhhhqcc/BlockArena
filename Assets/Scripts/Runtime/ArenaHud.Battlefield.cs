using System;
using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        Vector2 tacticScroll;
        readonly HashSet<int> mixedRouteShapes=new HashSet<int>();
        string battlefieldUiPhase;
        int battlefieldUiRound=-1;
        bool battlefieldKeyboardCaptured;
        public bool KeyboardCaptured { get { return CommunicationInputActive||ShowSettings||app.QuickStarting||app.ShowGameMenu||app.ShowRoom||app.ShowBestiary||app.State==null||app.State.phase=="lobby"||battlefieldKeyboardCaptured; } }
        readonly Color routeBlue=new Color(.39f,.83f,1);
        Rect WorldSummaryRect { get { Rect r=HeaderRect;return new Rect(r.x+227,r.y+7,359,40); } }
        Rect WorldOptionsRect { get { return LobbyWorldPanelRect; } }
        Rect CommanderRect { get {
            if(RecruitVisible)return new Rect(18,84,208,44);
            if(app.State!=null&&app.State.phase=="lobby")return LobbyCommanderRect;
            Rect r=CommandDockRect;return new Rect(r.x+12,r.y+17,232,54);
        } }
        Rect CommanderPickerRect { get {
            float w=Mathf.Min(400,width-36),h=274;
            if(app.State!=null&&app.State.phase=="lobby")return new Rect(RoomPanelRect.xMax+16,Mathf.Clamp(RoomPanelRect.y+197,84,height-h-20),w,h);
            float y=RecruitVisible?CommanderRect.yMax+10:CommanderRect.y-h-10;
            float bottom=!RecruitVisible&&app.State!=null&&app.State.phase!="lobby"?CommandDockRect.y-12:height-20;
            return new Rect(Mathf.Clamp(CommanderRect.x,18,width-w-18),Mathf.Clamp(y,80,Mathf.Max(80,bottom-h)),w,h);
        } }
        internal Rect CommanderSelectionScreenRect { get { Metrics();return ToScreenRect(CommanderRect); } }
        internal Rect CommanderPickerScreenRect { get { Metrics();return ToScreenRect(CommanderPickerRect); } }
        readonly Spring commanderPickerReveal=new Spring();
        bool drawingCommander;
        internal Rect CommanderScreenRect { get { Metrics();return ToScreenRect(CommanderRect); } }
        internal bool CommanderSelectableForVerification { get { return CommanderSelectable; } }
        Rect CommanderChoiceRect(int index){Rect r=CommanderPickerRect;float cardWidth=(r.width-38)/2;return new Rect(r.x+15+(index%2)*(cardWidth+8),r.y+54+(index/2)*52,cardWidth,44);}
        internal Rect CommanderChoiceScreenRect(int index){Metrics();return ToScreenRect(CommanderChoiceRect(index));}
        bool CommanderOverlayCovers(Vector2 p){return !BattlefieldHidden&&CommanderSelectable&&app.Tactics.SkillPickerOpen&&CommanderPickerRect.Contains(p);}
        Rect TacticsPanelRect { get { return new Rect(18,88,326,Mathf.Min(610,height-212)); } }
        Rect TacticsTabRect { get { return new Rect(18,88,206,43); } }
        internal Rect EditorScreenRect { get { Metrics();Rect r=TacticsPanelRect;return new Rect(r.x*scale,Screen.height-r.yMax*scale,r.width*scale,r.height*scale); } }
        internal bool BattlefieldControlsFit { get { Metrics();Rect r=CommanderRect;return r.xMin>=0&&r.yMin>=0&&r.xMax<=width&&r.yMax<=height&&!r.Overlaps(OwnedSkillToggleRect); } }
        Rect TacticsReadyRect { get { return ReadyRect; } }
        bool CommanderSelectable { get { return app.State!=null&&app.Me!=null&&!app.Me.eliminated&&!app.Me.ready&&
            (app.State.phase=="lobby"||app.State.phase=="prep"||app.State.phase=="recruit"||app.State.phase=="tactics"); } }
        bool BattlefieldHidden { get { return ShowSettings||app.QuickStarting||app.State==null||app.Tactics==null||app.ShowGameMenu||app.ShowBestiary||app.State.phase=="finished"; } }
        bool WorldSummaryVisible { get { return app.State!=null&&app.State.battlefield!=null&&
            !(app.State.phase=="reveal"&&(app.State.revealStage=="arena"||app.State.revealStage=="map")); } }

        // These rectangles use the same logical coordinate system as the rest of the HUD.
        bool BattlefieldHudCovers(Vector2 p)
        {
            if(BattlefieldHidden)return false;
            if(app.State.phase=="lobby"&&WorldOptionsRect.Contains(p))return true;
            if(app.ShowRoom&&app.State.phase!="lobby")return false;
            if(CommanderSelectable&&(CommanderRect.Contains(p)||app.Tactics.SkillPickerOpen&&CommanderPickerRect.Contains(p)))return true;
            if(WorldSummaryVisible&&WorldSummaryRect.Contains(p))return true;
            if(app.Tactics.Active&&((app.Tactics.Collapsed?TacticsTabRect:TacticsPanelRect).Contains(p)||TacticsReadyRect.Contains(p)))return true;
            return app.State.phase=="battle"&&app.Me!=null&&!app.Me.eliminated&&CommanderRect.Contains(p);
        }

        void DrawBattlefieldHud()
        {
            // GUI focus is sampled here; Update must never invoke an IMGUI query.
            battlefieldKeyboardCaptured=GUI.GetNameOfFocusedControl().StartsWith("tactic_",StringComparison.Ordinal);
            if(BattlefieldHidden)return;
            if(app.State.phase=="lobby")return;
            if(battlefieldUiPhase!=app.State.phase||battlefieldUiRound!=app.State.round){
                if(app.State.phase=="tactics"){tacticScroll=Vector2.zero;app.Tactics.Collapsed=false;}
                battlefieldUiPhase=app.State.phase;battlefieldUiRound=app.State.round;
            }
            bool enabled=GUI.enabled;
            GUI.enabled=enabled;
            if(RecruitVisible){if(CommanderSelectable)DrawCommanderSelection();GUI.enabled=enabled;return;}
            if(app.ShowRoom&&app.State.phase!="lobby"){GUI.enabled=enabled;return;}
            if(WorldSummaryVisible)DrawWorldSummary();
            if(app.State.phase=="reveal")DrawWeatherReveal();
            if(app.Tactics.Active){DrawTacticsProjection();DrawTacticsEditor();}
            if(CommanderSelectable||(CommandDockVisible&&app.State.phase!="battle"))DrawCommanderSelection();
            else if(app.State.phase=="battle"&&app.Me!=null&&!app.Me.eliminated)DrawCommanderBattle();
            GUI.enabled=enabled;
        }

        void DrawWorldOptions()
        {
            bool host=app.State.hostId==app.PlayerId;
            if(IconButton(LobbyWorldToggleRect,"book","战场设置",new Color(.095f,.20f,.21f))){app.Tactics.OptionsOpen=!app.Tactics.OptionsOpen;if(app.Tactics.OptionsOpen)app.Tactics.SkillPickerOpen=false;}
            if(!app.Tactics.OptionsOpen)return;
            Rect r=WorldOptionsRect;Panel(r,new Color(.035f,.075f,.09f,.98f));
            PanelHeading(r,"book",host?"战场设置":"战场设置 · 房主决定");
            if(IconButton(new Rect(r.xMax-44,r.y+10,29,29),"close","",new Color(.10f,.18f,.20f)))app.Tactics.OptionsOpen=false;
            bool previous=GUI.enabled;GUI.enabled=previous&&host&&app.Connected;
            Text(new Rect(r.x+16,r.y+59,280,23),"地图",micro,muted);
            string map=app.State.preferredMap;
            if(Button(new Rect(r.x+16,r.y+84,294,37),string.IsNullOrEmpty(map)?"随机地图  ›":BattlefieldCatalog.GetMap(map).name+"  ›",new Color(.16f,.27f,.25f)))
                app.Send(new GameCommand{type="world_options",mapId=CycleRandom(BattlefieldCatalog.Maps,map),weather=app.State.preferredWeather});
            Text(new Rect(r.x+16,r.y+131,280,23),"天气",micro,muted);
            string weather=app.State.preferredWeather;
            if(Button(new Rect(r.x+16,r.y+156,294,37),string.IsNullOrEmpty(weather)?"按地图随机天气  ›":BattlefieldCatalog.GetWeather(weather).name+"  ›",new Color(.16f,.27f,.25f)))
                app.Send(new GameCommand{type="world_options",mapId=map,weather=CycleRandom(BattlefieldCatalog.Weathers,weather)});
            GUI.enabled=previous;
            DrawWeatherGlyph(new Rect(r.x+17,r.y+211,20,20),string.IsNullOrEmpty(weather)?"clear":weather,mint);
        }
        static string CycleRandom(string[] values,string current)
        {
            if(string.IsNullOrEmpty(current))return values[0];
            int next=Array.IndexOf(values,current)+1;return next>=values.Length?null:values[next];
        }

        void DrawWorldSummary()
        {
            var world=app.State.battlefield;if(world==null)return;
            var map=BattlefieldCatalog.GetMap(world.mapId);var weather=BattlefieldCatalog.GetWeather(world.weather);
            bool weatherKnown=app.State.phase!="reveal"||app.State.revealStage=="climate";
            Rect r=WorldSummaryRect;
            ArenaIcons.Draw(new Rect(r.x+2,r.y+9,21,21),"book");
            Text(new Rect(r.x+32,r.y+8,158,28),map.name,small,white);
            if(weatherKnown){DrawWeatherGlyph(new Rect(r.x+203,r.y+9,23,23),world.weather,WeatherColor(weather));Text(new Rect(r.x+235,r.y+8,112,28),weather.name,small,WeatherColor(weather));}
            else Text(new Rect(r.x+235,r.y+8,112,28),"—",small,muted);
            if(r.Contains(Mouse()))SetHover(map.name+(weatherKnown?" · "+weather.name:""),map.description+(weatherKnown?"\n"+weather.description:"")+"\n"+TerrainGuide(world.mapId),HudPointer);
        }
        static string TerrainGuide(string map)
        {
            if(map=="courtyards")return "高地：远程射程 +15%、伤害 +8%；四座窄桥限制通路。废墟正面远程减伤25%，可被摧毁。";
            if(map=="bog")return "湿地：非水生移速 -20%、攻速 -10%；水生移速 +15%。密林：远程闪避20%、停火后隐蔽；火焰可引燃。";
            return "浅水：降雪20秒后结冰，开启新路线。密林提供隐蔽和远程闪避；高地强化远程。";
        }
        static Color WeatherColor(WeatherDefinition definition)
        {Color color;return ColorUtility.TryParseHtmlString(definition.color,out color)?color:Color.white;}

        void DrawWeatherReveal()
        {
            var state=app.State;string stage=state.revealStage;float age=state.revealStageAge;
            if(stage=="arena"||stage=="terrain")return;
            bool mapDraw=stage=="map";var world=state.battlefield;
            if(!mapDraw&&world==null)return;
            float duration=mapDraw?1.5f:stage=="weather"?2.2f:1.7f;
            float enter=Mathf.SmoothStep(0,1,Mathf.Clamp01(age/.3f));
            Rect r=new Rect(width/2-206,151+(1-enter)*-18,412,101);
            Panel(r,new Color(.035f,.075f,.085f,.95f));
            Color color=mint;string name;
            if(mapDraw){
                string mapId=BattlefieldCatalog.Maps[ArenaRevealAudio.DrawIndex("map",age,BattlefieldCatalog.Maps.Length)];
                var map=BattlefieldCatalog.GetMap(mapId);name=map.name;
                ArenaIcons.Draw(new Rect(r.x+21,r.y+27,45,45),"book");
            }else{
                var map=BattlefieldCatalog.GetMap(world.mapId);string shown=stage=="weather"?map.weathers[ArenaRevealAudio.DrawIndex("weather",age,map.weathers.Length)]:world.weather;
                var weather=BattlefieldCatalog.GetWeather(shown);color=WeatherColor(weather);name=weather.name;
                DrawWeatherGlyph(new Rect(r.x+21,r.y+29,45,45),shown,color);
            }
            Text(new Rect(r.x+90,r.y+13,295,24),mapDraw?"战场":stage=="weather"?"天气":"本轮环境",micro,muted);
            Text(new Rect(r.x+90,r.y+43,295,41),name,heading,color);
            Fill(new Rect(r.x+18,r.yMax-9,(r.width-36)*Mathf.Clamp01(age/duration),2),color);
        }

        void DrawCommanderSelection()
        {
            drawingCommander=true;var definition=BattlefieldCatalog.GetCommander(app.Me.commanderSkill);Rect toggle=CommanderRect;
            bool enabled=GUI.enabled;GUI.enabled=enabled&&CommanderSelectable;
            if(Button(toggle,"",new Color(.095f,.20f,.21f),null,"ui_toggle",true)){
                app.Tactics.SkillPickerOpen=!app.Tactics.SkillPickerOpen;
                if(app.Tactics.SkillPickerOpen){ClosePinnedInspection();CloseSkillDetails();ShowOwnedSkills=false;app.Tactics.OptionsOpen=false;}
                ClearHover();
            }
            GUI.enabled=enabled;
            float glyphY=toggle.center.y-15+lastButtonOffset;
            DrawCommanderGlyph(new Rect(toggle.x+10,glyphY,30,30),definition.id,mint);
            Text(new Rect(toggle.x+50,toggle.center.y-14+lastButtonOffset,toggle.width-81,29),definition.name,small,white);
            Text(new Rect(toggle.xMax-30,toggle.center.y-13,20,27),CommanderSelectable?(app.Tactics.SkillPickerOpen?"−":"+"):"✓",small,gold);
            if(toggle.Contains(Mouse()))SetCommanderHover("指挥技 · "+definition.name,CommanderTooltipBody(definition.id,app.Keys.Label("cast")),HudPointer);
            commanderPickerReveal.Step(app.Tactics.SkillPickerOpen?1:0,23,.58f);
            if(!app.Tactics.SkillPickerOpen||!CommanderSelectable){drawingCommander=false;return;}
            Rect r=CommanderPickerRect;RoundedPanel(r,new Color(.035f,.075f,.09f,.99f));
            Text(new Rect(r.x+17,r.y+13,r.width-70,28),app.State.phase=="lobby"?"指挥技":"第 "+app.State.round.ToString("00")+" 轮 · 指挥技",label,gold);
            if(Button(new Rect(r.xMax-45,r.y+11,29,28),"×",new Color(.11f,.20f,.22f),muted,"ui_toggle",true))app.Tactics.SkillPickerOpen=false;
            for(int i=0;i<BattlefieldCatalog.Commanders.Length;i++){
                var skill=BattlefieldCatalog.GetCommander(BattlefieldCatalog.Commanders[i]);bool selected=skill.id==app.Me.commanderSkill;
                Rect card=CommanderChoiceRect(i);
                if(Button(card,"",selected?new Color(.14f,.31f,.27f):new Color(.065f,.14f,.16f),null,"skill_select",true)){
                    app.Send(new GameCommand{type="commander",skillId=skill.id});app.Tactics.SkillPickerOpen=false;ClearHover();
                }
                float bump=(1-Mathf.Clamp01(commanderPickerReveal.value))*5;
                DrawCommanderGlyph(new Rect(card.x+9,card.y+8+lastButtonOffset+bump,28,28),skill.id,selected?mint:gold);
                Text(new Rect(card.x+47,card.y+9+lastButtonOffset,card.width-64,27),skill.name,small,selected?mint:white);
                if(selected)RoundFill(new Rect(card.xMax-13,card.center.y-3,6,6),mint,3);
                if(card.Contains(Mouse()))SetCommanderHover(skill.name,CommanderTooltipBody(skill.id,app.Keys.Label("cast")),HudPointer);
            }
            drawingCommander=false;
        }
        internal static string CommanderTooltipBody(string id,string castKey="R")
        {
            // SetHover accepts plain catalog text and applies Format once. Do not put rich tags here.
            return BattlefieldCatalog.GetCommander(id).description+"\n"+castKey+" 瞄准 · 再按 "+castKey+" / 左键施放\n每场 2 次 · 冷却 8 秒";
        }
        static string CommanderBrief(string id)
        {
            switch(id){case "drums":return "8秒攻速↑20% / 移速↑10%";case "ward":return "6秒减伤20% / 净化";case "focus":return "8秒优先集火 / 易伤15%";case "cleanse":return "清除负面状态 / 4秒抗性";case "smoke":return "5秒隐蔽 / 移速↑30%";case "trap":return "隐蔽陷阱 / 定身2秒";case "decoy":return "6秒嘲讽 / 吸引火力";default:return "0.8秒前摇 / 携3友军";}
        }
        static string CommanderIcon(string id)
        {switch(id){case "drums":return "sword";case "ward":return "shield";case "focus":return "range";case "cleanse":return "heart";case "smoke":case "rift":return "speed";case "trap":return "poison";default:return "skull";}}
        void DrawCommanderBattle()
        {
            var me=app.Me;var skill=BattlefieldCatalog.GetCommander(me.commanderSkill);
            Rect r=CommanderRect;bool available=me.commanderUses>0&&me.commanderCooldown<=0;
            bool before=GUI.enabled;GUI.enabled=before&&available;
            if(Button(r,"",app.Tactics.Targeting?new Color(.21f,.38f,.36f):new Color(.045f,.10f,.12f)))app.Tactics.ArmSkill(false);
            GUI.enabled=before;
            string castKey=app.Keys.Label("cast");float keyWidth=Mathf.Clamp(castKey.Length*11,27,64),keyShift=keyWidth-27;
            RoundFill(new Rect(r.x+9,r.y+12,keyWidth,30),available?new Color(.20f,.36f,.31f):new Color(.12f,.18f,.20f),5);
            Text(new Rect(r.x+9,r.y+12,keyWidth,30),castKey,centered,available?mint:muted);
            DrawCommanderGlyph(new Rect(r.x+44+keyShift,r.y+11,32,32),skill.id,available?mint:muted);
            Text(new Rect(r.x+85+keyShift,r.y+5,141-keyShift,25),skill.name,small,white);
            string status=app.Tactics.Targeting?app.Keys.Label("cast")+" / 点击施放":me.commanderUses<=0?"0 / 2":me.commanderCooldown>0?"冷却 "+me.commanderCooldown.ToString("0.0")+"s":""+me.commanderUses+" / 2";
            Text(new Rect(r.x+85+keyShift,r.y+29,141-keyShift,22),status,micro,available?mint:muted);
            if(r.Contains(Mouse()))SetCommanderHover(skill.name,CommanderTooltipBody(skill.id,app.Keys.Label("cast")),HudPointer);
            float cooldown=Mathf.Clamp01(me.commanderCooldown/8f);
            Fill(new Rect(r.x+85,r.yMax-5,133*(1-cooldown),2),available?mint:gold);
            if(app.Tactics.Targeting)DrawCommanderTarget(skill);
        }
        void DrawCommanderTarget(CommanderDefinition skill)
        {
            Point2 point;if(app.World==null||BattlefieldHudCovers(Mouse())||!app.Tactics.PointAt(HudPointer,out point))return;
            Vector2 at;if(TacticProject(app.Tactics.WorldPoint(point.x,point.y,.15f),out at)){
                Fill(new Rect(at.x-7,at.y-1,14,2),white);Fill(new Rect(at.x-1,at.y-7,2,14),white);
                DrawCommanderGlyph(new Rect(at.x-14,at.y-41,28,28),skill.id,mint);
            }
        }

        void DrawTacticsEditor()
        {
            var tactics=app.Tactics;bool before=GUI.enabled;
            if(tactics.Collapsed){if(IconButton(TacticsTabRect,"book","展开战术编排",new Color(.12f,.24f,.25f)))tactics.Collapsed=false;}
            else{
                Rect r=TacticsPanelRect;Panel(r,new Color(.035f,.075f,.09f,.96f));
                PanelHeading(r,"book","战术编排");
                if(Button(new Rect(r.xMax-43,r.y+10,30,29),"−",new Color(.14f,.23f,.23f)))tactics.Collapsed=true;
                Text(new Rect(r.x+15,r.y+61,192,25),tactics.SelectedCount+" 只  ·  "+(tactics.RoutesMixed?"多条路线":tactics.RouteNodeCount+" 节点"),small,routeBlue);
                GUI.enabled=before&&!app.Me.ready;
                if(Button(new Rect(r.xMax-105,r.y+59,91,29),"全选",new Color(.12f,.25f,.25f)))tactics.SelectAll(false);
                ArenaIcons.Draw(new Rect(r.x+16,r.y+94,18,18),"range");
                Text(new Rect(r.x+43,r.y+90,r.width-57,24),tactics.RoutesMixed?"右键重绘整组":"Shift 多选   /   右键布点",micro,tactics.RoutesMixed?gold:muted);
                Rect viewport=new Rect(r.x+11,r.y+119,r.width-22,r.height-247);
                int count=tactics.RouteNodeCount;float listHeight=Mathf.Max(viewport.height,count*169);
                tacticScroll=BeginUiScroll(viewport,tacticScroll,new Rect(0,0,viewport.width-19,listHeight));
                if(count==0){
                    Text(new Rect(15,23,viewport.width-49,83),tactics.SelectedCount==0?"选择生物":tactics.RoutesMixed?"右键绘制新路线":"右键添加节点",centered,muted);

                }
                for(int i=0;i<count;i++){
                    if(i>=tactics.RouteNodeCount)break;
                    DrawTacticNode(i,new Rect(1,i*169,viewport.width-22,160));
                }
                GUI.EndScrollView();
                float foot=r.yMax-116;
                GUI.enabled=before&&!app.Me.ready&&tactics.SelectedCount>0&&count<ArenaTactics.MaxNodes;
                if(Button(new Rect(r.x+14,foot,184,31),"＋ 原地等待节点",new Color(.12f,.25f,.26f)))tactics.AddWait();
                GUI.enabled=before&&!app.Me.ready&&tactics.SelectedCount>0&&(count>0||tactics.RoutesMixed);
                if(Button(new Rect(r.x+207,foot,105,31),"清空路线",new Color(.26f,.17f,.15f)))tactics.ClearRoute();
                GUI.enabled=before&&!app.Me.ready&&tactics.SelectedCount>0&&!tactics.RoutesMixed;
                string save=tactics.RoutesMixed?"多条路线":tactics.Dirty?"保存路线":tactics.IsDraftSynced?"路线已同步":"同步路线";
                if(IconButton(new Rect(r.x+14,foot+41,298,35),"ready",save,tactics.Dirty?new Color(.25f,.43f,.38f):new Color(.12f,.24f,.24f)))tactics.Save();
                GUI.enabled=before;
                Text(new Rect(r.x+16,r.yMax-31,r.width-32,22),"",micro,muted);
            }
            GUI.enabled=before;
            bool ready=app.Me.ready;
            if(IconButton(TacticsReadyRect,"ready",ready?"战术就绪 · 点击取消":"战术完成 · 准备开战",ready?new Color(.19f,.36f,.31f):mint,"ready"))tactics.Finish();
        }
        void DrawTacticNode(int index,Rect r)
        {
            var tactics=app.Tactics;var node=tactics.DraftNodes[index];bool selected=tactics.SelectedNode==index;
            RoundFill(r,selected?new Color(.095f,.20f,.23f):new Color(.065f,.13f,.15f),7);
            Fill(new Rect(r.x,r.y+8,2,r.height-16),selected?routeBlue:new Color(.24f,.40f,.40f));
            Fill(new Rect(r.x+10,r.y+12,26,26),new Color(.18f,.38f,.43f));
            Text(new Rect(r.x+10,r.y+12,26,26),(index+1).ToString(),centered,white);
            if(Button(new Rect(r.x+44,r.y+10,139,31),ActionName(node.action)+"  ›",new Color(.12f,.29f,.29f))){tactics.SelectedNode=index;tactics.CycleAction(index);}
            bool enabled=GUI.enabled;GUI.enabled=enabled&&index>0;
            if(Button(new Rect(r.xMax-85,r.y+11,23,29),"↑",new Color(.13f,.22f,.23f)))tactics.MoveNode(index,-1);
            GUI.enabled=enabled&&index<tactics.RouteNodeCount-1;
            if(Button(new Rect(r.xMax-58,r.y+11,23,29),"↓",new Color(.13f,.22f,.23f)))tactics.MoveNode(index,1);
            GUI.enabled=enabled;
            if(Button(new Rect(r.xMax-31,r.y+11,23,29),"×",new Color(.30f,.16f,.15f))){tactics.RemoveNode(index);return;}
            if(Button(new Rect(r.x+11,r.y+48,r.width-22,28),"条件 · "+ConditionName(node),new Color(.10f,.19f,.23f)))tactics.CycleCondition(index);
            bool timed=node.action!="move"||node.condition=="after_time";
            if(node.condition=="health_below"||node.condition=="enemy_near"){
                string threshold=node.condition=="health_below"?"生命 ≤ "+node.threshold.ToString("0")+"%":"敌人距离 ≤ "+node.threshold.ToString("0");
                Text(new Rect(r.x+12,r.y+84,157,24),threshold,micro,routeBlue);
                if(Button(new Rect(r.x+171,r.y+83,30,26),"−",new Color(.13f,.24f,.25f)))tactics.ChangeThreshold(index,node.condition=="health_below"?-5:-25);
                if(Button(new Rect(r.x+208,r.y+83,30,26),"+",new Color(.13f,.24f,.25f)))tactics.ChangeThreshold(index,node.condition=="health_below"?5:25);
            }else if(timed){
                Text(new Rect(r.x+12,r.y+84,157,24),(node.condition=="after_time"?"开战后 / 停留 ":"最多停留 ")+node.seconds.ToString("0")+"s",micro,routeBlue);
                if(Button(new Rect(r.x+171,r.y+83,30,26),"−",new Color(.13f,.24f,.25f)))tactics.ChangeSeconds(index,-1);
                if(Button(new Rect(r.x+208,r.y+83,30,26),"+",new Color(.13f,.24f,.25f)))tactics.ChangeSeconds(index,1);
            }else Text(new Rect(r.x+12,r.y+84,r.width-24,24),"抵达后执行下一节点",micro,muted);
            if(timed&&(node.condition=="health_below"||node.condition=="enemy_near")){
                Text(new Rect(r.x+12,r.y+112,157,24),"最多停留 "+node.seconds.ToString("0")+"s",micro,routeBlue);
                if(Button(new Rect(r.x+171,r.y+111,30,26),"−",new Color(.13f,.24f,.25f)))tactics.ChangeSeconds(index,-1);
                if(Button(new Rect(r.x+208,r.y+111,30,26),"+",new Color(.13f,.24f,.25f)))tactics.ChangeSeconds(index,1);
            }else Text(new Rect(r.x+12,r.y+113,r.width-24,21),NodeExplanation(node),micro,muted);
            bool passable=BattlefieldCatalog.IsPassable(app.State.battlefield,node.x,node.y);
            Text(new Rect(r.x+12,r.y+140,r.width-24,19),passable?"落点 "+Mathf.RoundToInt(node.x)+", "+Mathf.RoundToInt(node.y):"地面暂不可通行 · 靠近可达边缘",micro,passable?new Color(.47f,.59f,.60f):gold);
            if(index<tactics.RouteNodeCount-1){Fill(new Rect(r.x+22,r.yMax,2,9),routeBlue);}
        }
        static string ActionName(string action)
        {switch(action){case "capture":return "占领目标";case "ambush":return "伏击待敌";case "wait":return "定点等待";case "attack":return "驻守攻击";default:return "移动前往";}}
        static string ConditionName(TacticNode node)
        {switch(node.condition){case "after_time":return "开战时间达到";case "health_below":return "生命低于阈值";case "enemy_near":return "落点附近出现敌人";default:return "立即执行";}}
        static string NodeExplanation(TacticNode node)
        {switch(node.action){case "capture":return "占领成功或超时后前进";case "ambush":return "遇敌或超时后解除伏击";case "wait":return "等待结束后前进";case "attack":return "驻守攻击，超时后前进";default:return "条件满足后前往指定位置";}}

        static void DrawWeatherGlyph(Rect r,string weather,Color color)
        {
            float p=r.width/12;float x=r.x,y=r.y;
            if(weather=="clear"||weather=="heat"){
                Fill(new Rect(x+4*p,y+4*p,4*p,4*p),color);
                Fill(new Rect(x+5*p,y,2*p,2*p),color);Fill(new Rect(x+5*p,y+10*p,2*p,2*p),color);
                Fill(new Rect(x,y+5*p,2*p,2*p),color);Fill(new Rect(x+10*p,y+5*p,2*p,2*p),color);
                if(weather=="heat")Fill(new Rect(x+8*p,y+9*p,p,3*p),color);
            }else if(weather=="fog"||weather=="sandstorm"){
                for(int i=0;i<4;i++)Fill(new Rect(x+(i%2)*2*p,y+(2+i*2)*p,(10-i%2*3)*p,p),color);
            }else{
                Fill(new Rect(x+2*p,y+2*p,8*p,4*p),color);Fill(new Rect(x+4*p,y,4*p,3*p),color);
                for(int i=0;i<3;i++){
                    float dx=x+(2+i*3)*p;
                    if(weather=="snow"){Fill(new Rect(dx,y+8*p,3*p,p),color);Fill(new Rect(dx+p,y+7*p,p,3*p),color);}
                    else Fill(new Rect(dx,y+7*p,p,3*p),color);
                }
                if(weather=="storm"){Fill(new Rect(x+5*p,y+5*p,3*p,3*p),goldStatic);Fill(new Rect(x+4*p,y+8*p,3*p,3*p),goldStatic);}
            }
        }
        static void DrawCommanderGlyph(Rect r,string id,Color tint)
        {
            string[] pixels;
            switch(id){
                case "drums":pixels=new[]{"  #    #  ","   #  #   ","  ######  "," ######## "," ## ## ## "," ## ## ## "," ######## ","  ######  ","  #    #  "," ##    ## "};break;
                case "ward":pixels=new[]{"    ##    ","  ######  "," ######## "," ## ## ## "," ## ## ## "," ## ## ## ","  ######  ","   ####   ","    ##    ","    ##    "};break;
                case "focus":pixels=new[]{"    ##    ","  # ## #  "," #      # ","   ####   ","## #### ##","## #### ##","   ####   "," #      # ","  # ## #  ","    ##    "};break;
                case "cleanse":pixels=new[]{"       #  ","      ### ","       #  ","  ##      "," ####     ","########  "," ####     ","  ##      ","  ##  ##  "," #######  "};break;
                case "smoke":pixels=new[]{"          ","    ###   ","  ######  "," #######  "," ######## ","##########"," ######## ","  ######  "," #  ##  # ","          "};break;
                case "trap":pixels=new[]{"          ","#        #","##      ##"," ## ## ## ","  ######  ","  ######  "," ## ## ## ","##      ##","#        #","          "};break;
                case "decoy":pixels=new[]{"   ####   ","   ####   ","    ##    "," ######## ","##########","## #### ##","   ####   ","   ####   ","   #  #   ","  ##  ##  "};break;
                default:pixels=new[]{"   ####   ","  ##  ##  "," ##    ## ","##  ##  ##","## ###  ##","##  ### ##","##  ##  ##"," ##    ## ","  ##  ##  ","   ####   "};break;
            }
            float step=r.width/12;RoundFill(r,new Color(.025f,.065f,.08f,.9f),5);
            for(int y=0;y<pixels.Length;y++)for(int x=0;x<pixels[y].Length;x++)if(pixels[y][x]=='#')Fill(new Rect(r.x+(x+1)*step,r.y+(y+1)*step,step+.15f,step+.15f),tint);
        }
        static readonly Color goldStatic=new Color(.95f,.76f,.34f);
    }
}
