using System;
using System.Collections.Generic;
using System.Text;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Small native overlays leave the world visible; all input rectangles share the same UI scale.
    public sealed partial class ArenaHud
    {
        readonly ArenaApp app;
        GUIStyle label, title, small, heading, button, field, centered, micro, number;
        Font font;
        float scale, width, height;
        readonly Color mint = new Color(.38f, .87f, .73f);
        readonly Color white = new Color(.94f, .96f, .91f);
        readonly Color muted = new Color(.69f, .77f, .72f);
        readonly Color dark = new Color(.035f, .075f, .075f, .92f);
        float pendingUntil;
        int pendingRound = -1;
        string hoverTitle, hoverBody;
        CreatureDefinition inspectionDefinition;
        FighterState inspectionFighter;
        UnitState inspectionUnit;
        PlayerState inspectionOwner;
        Vector2 bestiaryScroll, detailScroll;
        string bestiarySearch="", bestiaryKind;
        readonly Color gold=new Color(.93f,.72f,.3f);
        readonly Color copper=new Color(.5f,.34f,.18f);
        Vector2 hoverScreenPosition;
        float hoverUntil;
        public ArenaHud(ArenaApp app) { this.app = app; }

        void Setup()
        {
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Noto Sans CJK SC", "Arial" }, 32);
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 19, richText = true, wordWrap = true, padding = new RectOffset(0,0,0,0) };
            label.normal.textColor = white;
            small = new GUIStyle(label) { fontSize = 17 }; small.normal.textColor = muted;
            micro = new GUIStyle(label) { fontSize = 14 }; micro.normal.textColor = muted;
            number = new GUIStyle(label) { fontSize = 23, fontStyle = FontStyle.Bold };
            title = new GUIStyle(label) { fontSize = 44, fontStyle = FontStyle.Bold };
            heading = new GUIStyle(label) { fontSize = 28, fontStyle = FontStyle.Bold };
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 19, alignment = TextAnchor.MiddleCenter, border = new RectOffset(0,0,0,0), padding = new RectOffset(8,8,3,3) };
            button.normal.background = button.hover.background = button.active.background = null;
            button.normal.textColor = button.hover.textColor = button.active.textColor = dark;
            field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 20, padding = new RectOffset(15,15,10,9) };
            field.normal.background = field.focused.background = Texture2D.whiteTexture;
            field.normal.textColor = field.focused.textColor = white;
            GUI.skin.settings.selectionColor = new Color(.1f,.45f,.35f);
        }

        void Metrics()
        {
            scale = Mathf.Max(.3f, Mathf.Min(Screen.width / 1440f, Screen.height / 900f));
            width = Screen.width / scale; height = Screen.height / scale;
            if (small != null) small.fontSize = Mathf.Clamp(Mathf.CeilToInt(14f / scale),17,21);
            if (micro != null) micro.fontSize = Mathf.Clamp(Mathf.CeilToInt(12f / scale),16,19);
            if (micro != null) micro.fontSize = Mathf.Max(16,small.fontSize-1);
            if (label != null) label.fontSize = Mathf.Clamp(Mathf.CeilToInt(15f / scale),19,23);
            if (centered != null) centered.fontSize = label.fontSize;
        }

        Rect HeaderRect { get { return new Rect(width/2-173,18,346,47); } }
        Rect HeaderActionsRect { get { return new Rect(width-231,18,213,43); } }
        Rect MarketRect { get { EnsureMarketPosition(); return new Rect(marketPosition.x,marketPosition.y,476,434); } }
        Rect MarketToggleRect { get { return new Rect(width/2+118,height-74,136,46); } }
        Rect BestiaryToggleRect { get { return new Rect(width/2-254,height-74,136,46); } }
        Rect PrepareBannerRect { get { return new Rect(width/2-210,ReadyRect.y-31,420,25); } }
        Rect ReadyRect { get { return new Rect(width/2-106,height-74,212,46); } }
        Rect NoticeRect { get { return new Rect(width/2-290,PrepareBannerRect.y-66,580,48); } }

        public bool Contains(Vector2 screen)
        {
            Metrics(); UpdateMarketPointer(); UpdateRoster();
            if (marketPointerOwned || marketDragging || releasedPointerFrame == Time.frameCount || audioPointerOwned || audioReleasedFrame == Time.frameCount || skillPointerOwned || skillReleasedFrame == Time.frameCount) return true;
            Vector2 p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            if (AudioCovers(p)) return true;
            if (EconomyCovers(p)) return true;
            var state = app.State; var me = app.Me;
            if (state == null || app.ShowRoom || app.ShowHelp || app.ShowBestiary || state.phase == "finished" ||
                (state.phase == "recruit" && me != null && me.recruitPending && !me.eliminated)) return true;
            if (HeaderRect.Contains(p) || HeaderActionsRect.Contains(p) || RosterCovers(p)) return true;
            if (app.CanPrepare && (((app.Me.ready || state.phase == "recruit") && PrepareBannerRect.Contains(p)) || ReadyRect.Contains(p) || MarketToggleRect.Contains(p) || BestiaryToggleRect.Contains(p))) return true;
            if(MarketVisible&&MarketRect.Contains(p))return true;
            if (!string.IsNullOrEmpty(app.Notice) && NoticeRect.Contains(p)) return true;
            return SkillUiCovers(p);
        }

        public void SetHover(string titleText, string bodyText, Vector2 screenPosition)
        {
            ResetInspection();
            PrepareHover(titleText);
            inspectionDefinition=null;
            hoverTitle = titleText;
            hoverBody = ArenaDescription.Format(bodyText);
            hoverScreenPosition = screenPosition;
            hoverUntil = Time.unscaledTime + .15f;
        }

        public void ClearHover() { hoverTitle = null; hoverBody = null; inspectionDefinition=null; ResetInspection(); }
        public void SetInspection(CreatureDefinition definition,FighterState fighter,UnitState unit,PlayerState owner,Vector2 screenPosition)
        {
            if(definition==null){ClearHover();return;}
            BeginInspection(definition,fighter,unit,owner);
            PrepareHover(fighter!=null?fighter.id:unit!=null?unit.id:definition.id);
            inspectionDefinition=definition;inspectionUnit=unit;inspectionOwner=owner;
            inspectionFighter=fighter??(owner!=null&&unit!=null?ArenaGame.PreviewFighter(owner,unit):null);
            CacheInspectionSkills();
            hoverScreenPosition=screenPosition;hoverUntil=Time.unscaledTime+.15f;hoverTitle=null;
        }

        public void Draw()
        {
            Setup(); Metrics(); AdvanceMotion(); UpdateMarketPointer(); UpdateRoster(); handRects.Clear(); textRects.Clear();
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
            GUI.color = Color.white;
            if (app.State == null) Menu();
            else
            {
                bool blocked = ModalOpen || AudioBlocksUnderlying || SkillBlocksUnderlying || EconomyBlocksUnderlying || (MarketVisible && (MarketRect.Contains(Mouse()) || marketPointerOwned));
                GUI.enabled = !blocked;
                Header(); Health(); DrawRosterTab();
                if (app.CanPrepare) Preparation();
                GUI.enabled = !ModalOpen && !AudioBlocksUnderlying && !SkillBlocksUnderlying && !EconomyBlocksUnderlying;
                if(MarketVisible) Market();
                GUI.enabled = true;
                DrawEconomyObjectives();
                if (app.State.phase == "results") Results();
                if (app.State.phase == "recruit" && app.Me != null && app.Me.recruitPending && !app.Me.eliminated) Recruit();
                if (app.Me != null && app.Me.eliminated && app.State.phase != "finished")
                    Text(new Rect(30,112,460,50),"你的军团已退场 · 继续观战",label);
                if (app.State.phase == "finished" && Time.unscaledTime - finishedAt >= 4.6f && (app.World == null || !app.World.RitualActive)) Finished();
                if (app.ShowRoom) Room();
            }
            if(app.ShowBestiary) Bestiary();
            if (app.ShowHelp) Help();
            DrawSkillPanels();
            DrawArmyManagement();
            if (!string.IsNullOrEmpty(app.Notice))
            {
                var r = NoticeRect;
                Panel(r,new Color(.03f,.08f,.07f,.94f));
                Text(r,app.Notice,centered);
            }
            if (!AudioBlocksUnderlying) Hover();
            DrawAudio();
            GUI.matrix = previous;
        }

        void Menu()
        {
            Fill(new Rect(0,0,width,height),new Color(.015f,.035f,.04f,.3f));
            float x = Mathf.Max(50,(width-1260)/2);
            float y = Mathf.Max(45,(height-704)/2);
            Panel(new Rect(x,y,470,704),dark);
            Text(new Rect(x+32,y+28,400,28),"BLOCK ARENA  /  UNITY EDITION",small,mint);
            Text(new Rect(x+32,y+72,410,76),"方块斗兽场",title);
            Text(new Rect(x+34,y+154,400,56),"召集生物，自由布阵，四方交锋。\n用你的阵形，决定战斗的走向。",label,muted);
            Text(new Rect(x+34,y+232,400,25),"指挥官昵称",small);
            app.PlayerName = InputBox(new Rect(x+34,y+262,402,48),"name",app.PlayerName,16);
            Text(new Rect(x+34,y+325,230,25),"房主 IP / 穿透域名",small);
            Text(new Rect(x+324,y+325,112,25),"TCP 端口",small);
            app.Address = InputBox(new Rect(x+34,y+355,278,48),"address",app.Address,160);
            app.PortText = InputBox(new Rect(x+324,y+355,112,48),"port",app.PortText,5);
            if (Button(new Rect(x+34,y+426,196,54),"本机开服",mint)) app.Host();
            if (Button(new Rect(x+240,y+426,196,54),"加入服务器",new Color(.7f,.78f,.73f))) app.Join();
            if (Button(new Rect(x+34,y+496,402,45),"单人试玩 · 与三位电脑交锋",new Color(.16f,.26f,.23f),white)) app.Host(true);
            Text(new Rect(x+34,y+558,402,56),string.IsNullOrEmpty(app.Status) ? "局域网：填写房主 IPv4\n内网穿透：填写映射后的 TCP 域名与端口" : app.Status,small);
            if (app.HasSession && Button(new Rect(x+34,y+625,190,35),"取消连接",new Color(.16f,.23f,.22f),white)) app.Leave();
            if (Button(new Rect(x+296,y+625,140,35),"玩法帮助",new Color(.16f,.23f,.22f),white)) app.ShowHelp = true;
            if (Button(new Rect(x+296,y+670,140,25),"退出游戏",new Color(.1f,.15f,.14f),muted)) Application.Quit();
            if (width > 1150)
            {
                Text(new Rect(x+550,y+75,630,60),"你的军团",title,white);
                Text(new Rect(x+550,y+133,630,60),"正在等待号令。",title,mint);
                Text(new Rect(x+555,y+222,570,60),"20 点生命  ·  四方自动混战\n赚取金币，招募与强化。没有格子和数量上限。",label,white);
                if(IconButton(new Rect(x+555,y+325,244,49),"book","探索万物图鉴",new Color(.2f,.31f,.28f)))app.ShowBestiary=true;
                Text(new Rect(x+555,y+601,650,45),"本机开服  /  TCP 7777  /  无需中心服务器",small);
            }
        }

        void Header()
        {
            Rect r=HeaderRect;Panel(r,new Color(.06f,.085f,.095f,.94f));
            string phase=app.State.phase;
            string phaseName=phase=="recruit"&&app.Me!=null&&!app.Me.recruitPending?"等待援军":PhaseName(phase);
            Text(new Rect(r.x+14,r.y+9,240,29),(phase=="lobby"?"":app.State.round.ToString("00")+"  ·  ")+phaseName,label,white);
            if(phase!="lobby"&&phase!="finished")Text(new Rect(r.xMax-80,r.y+7,65,32),Mathf.CeilToInt(app.State.timeLeft)+"s",number,app.State.timeLeft<8?gold:mint);
            Rect actions=HeaderActionsRect;
            Rect book=new Rect(actions.x,actions.y,44,42),help=new Rect(actions.x+53,actions.y,44,42),room=new Rect(actions.x+106,actions.y,54,42);
            if(IconButton(book,"book","",new Color(.13f,.2f,.22f)))app.ShowBestiary=!app.ShowBestiary;
            if(Button(help,"?",new Color(.13f,.2f,.22f),gold))app.ShowHelp=true;
            if(Button(room,"",new Color(.13f,.2f,.22f)))app.ShowRoom=true;
            float ry=room.y+lastButtonOffset;
            Fill(new Rect(room.x+17,ry+9,7,7),gold);Fill(new Rect(room.x+30,ry+12,6,6),mint);
            Fill(new Rect(room.x+13,ry+19,15,13),gold);Fill(new Rect(room.x+30,ry+21,11,11),mint);
            if(GUI.enabled&&book.Contains(Mouse()))SetHover("万物图鉴","查看生物、属性与技能。",Input.mousePosition);
            if(GUI.enabled&&help.Contains(Mouse()))SetHover("玩法帮助","移动、布阵、永久技能与联机说明。",HudPointer);
            if(GUI.enabled&&room.Contains(Mouse()))SetHover("房间",app.Connected?"查看玩家或离开房间。":"连接已中断，打开房间重新连接。",Input.mousePosition);
            if(!app.Connected)Text(new Rect(width-344,58,326,25),"连接中断 · 打开房间重连",small,new Color(1,.57f,.43f));
        }

        void Health()
        {
            Rect roster=RosterRect;
            foreach(var p in app.State.players)
            {
                PlayerVisual motion;playerVisuals.TryGetValue(p.id,out motion);
                float shownHp=motion==null?p.hp:motion.displayHp;
                bool shownEliminated=p.eliminated&&shownHp<=0;
                float damageAge=motion==null?10:Time.unscaledTime-motion.damagedAt;
                float shake=damageAge<.5f?Mathf.Sin(damageAge*76)*5*Mathf.Exp(-damageAge*7):0;
                Rect row=new Rect(roster.x+shake,roster.y+p.slot*60,roster.width,54);Color team=Team(p.slot);
                Panel(row,new Color(.04f,.075f,.085f,.91f));
                Fill(new Rect(row.x+4,row.y+7,3,row.height-14),team);
                if(damageAge<.4f)Fill(new Rect(row.x+4,row.y+4,row.width-8,row.height-8),new Color(.9f,.16f,.12f,(.4f-damageAge)*.45f));
                ArenaIcons.Draw(new Rect(row.x+13,row.y+16,25,25),"coin");
                Text(new Rect(row.x+42,row.y+15,59,29),p.gold.ToString(),label,shownEliminated?muted:gold);
                bool leader=app.State.phase=="battle"&&p.id==app.State.leaderId;
                if(leader) { float pulse=.5f+.5f*Mathf.Sin(Time.unscaledTime*4);Fill(new Rect(row.x+4,row.y+3,row.width-8,2),new Color(gold.r,gold.g,gold.b,.6f+.4f*pulse)); }
                Text(new Rect(row.x+105,row.y+5,158,25),(leader?"★ ":"")+Trim(p.name,leader?5:7)+(p.id==app.PlayerId?" ·你":""),micro,shownEliminated?muted:leader?gold:white);
                Rect bar=new Rect(row.x+106,row.y+32,196,12);
                Fill(bar,new Color(.015f,.025f,.03f));
                float trail=motion==null?p.hp:motion.trailHp;
                Fill(new Rect(bar.x+1,bar.y+1,(bar.width-2)*Mathf.Clamp01(trail/20f),bar.height-2),new Color(.81f,.2f,.16f));
                Fill(new Rect(bar.x+1,bar.y+1,(bar.width-2)*Mathf.Clamp01(shownHp/20f),bar.height-2),shownEliminated?muted:team);
                for(int mark=1;mark<4;mark++)Fill(new Rect(bar.x+bar.width*mark/4,bar.y+1,1,bar.height-2),new Color(.01f,.025f,.03f,.45f));
                Text(new Rect(row.x+258,row.y+4,55,26),Mathf.CeilToInt(shownHp)+"/20",micro,shownHp<=5?new Color(1,.54f,.43f):white);
                if(motion!=null)CoinFeedback(motion,row);
            }
        }

        void Preparation()
        {
            if(app.Me.ready||app.State.phase=="recruit")Text(PrepareBannerRect,app.Me.ready?"等待其他指挥官":"等待援军 · 可自由布阵",centered,muted);
            if(IconButton(ReadyRect,"ready",app.Me.ready?"已准备 · 取消":"准备完毕",app.Me.ready?new Color(.18f,.36f,.32f):mint,"ready"))app.Send(new GameCommand{type="ready"});
            if(IconButton(MarketToggleRect,"bag",app.ShowShop?"收起商店":"商店 [M]",new Color(.19f,.24f,.24f)))app.ShowShop=!app.ShowShop;
            if(IconButton(BestiaryToggleRect,"book","生物图鉴",new Color(.19f,.24f,.24f)))app.ShowBestiary=true;
        }

        void Market()
        {
            Rect r=MarketRect;Panel(r,new Color(.075f,.11f,.12f,.99f));
            float reveal=Mathf.Clamp(marketMotion.value,0,1.12f),bump=(1-reveal)*8;
            // The window and every button keep fixed hit rectangles; only inset ornament moves.
            Fill(new Rect(r.x+13,r.y+49,(r.width-26)*Mathf.Clamp01(reveal),2),mint);
            ArenaIcons.Draw(new Rect(r.x+15,r.y+11+bump,28,28),"bag");
            Text(new Rect(r.x+52,r.y+10+bump,320,31),"永久技能商店",label,gold);
            for(int i=0;i<3;i++)Fill(new Rect(r.x+319+i*7,r.y+22,3,7),new Color(.59f,.47f,.3f));
            bool active=GUI.enabled;GUI.enabled=active&&app.ShowShop&&!marketDragging;
            if(IconButton(new Rect(r.xMax-47,r.y+9,35,33),"close","",new Color(.17f,.22f,.23f)))app.ShowShop=false;
            Text(new Rect(r.x+15,r.y+58,446,25),"永久强化现有与未来的适用生物",micro,muted);
            if(app.Me.market!=null)for(int i=0;i<Mathf.Min(4,app.Me.market.Count);i++)
            {
                ShopOffer offer=app.Me.market[i];TradeDefinition item=TradeCatalog.Get(offer.itemId);if(item.type!="skill")continue;
                Rect card=MarketSkillCardRect(i);int matches=MatchingArmyCount(item);
                bool hovered=GUI.enabled&&card.Contains(Mouse())&&!marketDragging;
                Panel(card,hovered?new Color(.145f,.21f,.22f,.99f):new Color(.09f,.14f,.15f,.99f));
                if(matches>0)Fill(new Rect(card.x+4,card.y+5,3,card.height-10),mint);
                marketCards[i].Step(hovered?1:0,23,.57f);
                float lift=marketCards[i].value*2.8f;
                ArenaIcons.Draw(new Rect(card.x+10,card.y+10+bump*.5f-lift,28,28),ItemIcon(item));
                Text(new Rect(card.x+43,card.y+9,162,26),item.name,micro,white);
                Text(new Rect(card.x+11,card.y+42,193,24),TargetName(item.target)+" · 详情 ›",micro,gold);
                Text(new Rect(card.x+11,card.y+68,191,24),matches>0?"当前 "+matches+" 只可强化":"当前无匹配 · 可预购",micro,matches>0?mint:muted);
                SkillDetailButton(new Rect(card.x+7,card.y+6,card.width-14,85),item);
                bool owned=app.Me.upgrades!=null&&app.Me.upgrades.Contains(item.id);
                string why=offer.sold?"已售出":owned?"已掌握":!app.Connected?"未连接":app.Me.gold<offer.price?"金币不足":"学习";
                GUI.enabled=active&&app.ShowShop&&!marketDragging&&app.Connected&&why=="学习";
                if(IconButton(new Rect(card.x+10,card.y+97,195,30),"coin",offer.price+" · "+why,new Color(.22f,.4f,.35f),"purchase"))
                    app.Send(new GameCommand{type="purchase",offerId=offer.id});
                GUI.enabled=active&&app.ShowShop&&!marketDragging;
                if(GUI.enabled&&card.Contains(Mouse()))SkillHover(item);
            }
            int available=AvailableSkillCount();
            if(app.Me.market==null||app.Me.market.Count==0)
                Text(new Rect(r.x+24,r.y+174,r.width-48,115),available==0?"所有永久技能已掌握\n可在「已购技能」查看效果与适用生物":"本批技能已售罄\n刷新商店查看其他永久技能",centered,available==0?mint:muted);
            GUI.enabled=active&&app.ShowShop&&!marketDragging&&app.Me.gold>=TradeCatalog.RefreshPrice&&app.Connected&&available>0;
            if(IconButton(new Rect(r.x+14,r.y+388,221,32),"refresh","刷新 · 2 金币",new Color(.3f,.26f,.17f),"refresh"))app.Send(new GameCommand{type="refresh"});
            GUI.enabled=active;
            ArenaIcons.Draw(new Rect(r.x+352,r.y+390,25,25),"coin");Text(new Rect(r.x+386,r.y+389,69,28),app.Me.gold.ToString(),label,gold);
        }

        void Bestiary()
        {
            Shade();float x=(width-1120)/2,y=(height-716)/2;
            Panel(new Rect(x,y,1120,716),new Color(.075f,.11f,.12f,.99f));
            ArenaIcons.Draw(new Rect(x+23,y+20,38,38),"book");Text(new Rect(x+74,y+20,830,42),"万物图鉴 · 方块世界的生灵",heading,gold);
            if(IconButton(new Rect(x+1056,y+20,40,36),"close","",new Color(.2f,.25f,.25f)))app.ShowBestiary=false;
            bestiarySearch=InputBox(new Rect(x+24,y+81,296,43),"bestiary-search",bestiarySearch,60);
            if(string.IsNullOrEmpty(bestiarySearch))Text(new Rect(x+39,y+90,260,26),"搜索名称 / 类别 / 标签",small,muted);
            var matches=new List<string>();
            foreach(string kind in CreatureCatalog.Kinds)if(MatchesSearch(CreatureCatalog.Get(kind),bestiarySearch))matches.Add(kind);
            if(string.IsNullOrEmpty(bestiaryKind))bestiaryKind=CreatureCatalog.Kinds[0];
            bestiaryScroll=GUI.BeginScrollView(new Rect(x+24,y+139,296,532),bestiaryScroll,new Rect(0,0,274,Mathf.Max(532,matches.Count*69)));
            for(int i=0;i<matches.Count;i++)
            {
                if(i*69+66<bestiaryScroll.y||i*69>bestiaryScroll.y+532)continue;
                var d=CreatureCatalog.Get(matches[i]);Rect row=new Rect(0,i*69,272,63);
                if(Button(row,"",bestiaryKind==d.id?new Color(.18f,.33f,.29f):new Color(.12f,.16f,.17f)))bestiaryKind=d.id;
                ArenaIcons.Creature(new Rect(9,row.y+6,48,48),d.model??d.id,MobColor(d));
                Text(new Rect(68,row.y+7,146,25),d.name,label,white);Text(new Rect(68,row.y+36,168,22),CategoryName(d.category)+" · "+d.price+" 金币",micro,muted);
            }
            GUI.EndScrollView();
            CreatureDefinition selected=CreatureCatalog.Get(bestiaryKind);Color color=MobColor(selected);
            var abilityIds=selected.skills??new string[0];float contentHeight=412;
            foreach(string id in abilityIds)contentHeight+=81+small.CalcHeight(new GUIContent(ArenaDescription.Format(CreatureAbilities.Get(id).description)),618);
            detailScroll=GUI.BeginScrollView(new Rect(x+350,y+84,738,588),detailScroll,new Rect(0,0,714,Mathf.Max(588,contentHeight)));
            ArenaIcons.Creature(new Rect(17,7,157,151),selected.model??selected.id,color);
            Text(new Rect(202,20,478,52),selected.name,title,white);Text(new Rect(205,86,480,30),CategoryName(selected.category)+"  /  "+selected.role,small,Color.Lerp(color,white,.45f));
            ArenaIcons.Draw(new Rect(203,127,28,28),"coin");Text(new Rect(243,127,350,31),selected.price+" 金币  ·  阶位 "+selected.tier,label,gold);
            Text(new Rect(17,185,678,75),ArenaDescription.Format(selected.description),label,muted);
            Stat(new Rect(17,269,142,32),"heart",selected.hp.ToString("0.#"));Stat(new Rect(185,269,142,32),"sword",selected.attack.ToString("0.#"));Stat(new Rect(352,269,142,32),"speed",selected.attackSpeed.ToString("0.##")+"/秒");Stat(new Rect(520,269,158,32),"range",selected.range.ToString("0.#"));
            Text(new Rect(19,321,665,48),(selected.flying?"飞行生物   ·   ":"")+"移速 "+selected.speed.ToString("0.#")+"   ·   "+TagNames(selected.tags),small,mint);
            float yy=384;Text(new Rect(17,yy-25,660,29),"天赋与技能",label,gold);
            foreach(string id in abilityIds)
            {
                AbilityDefinition ability=CreatureAbilities.Get(id);string description=ArenaDescription.Format(ability.description);float h=small.CalcHeight(new GUIContent(description),618);
                Panel(new Rect(17,yy,678,66+h),new Color(.11f,.16f,.17f,.95f));ArenaIcons.Draw(new Rect(30,yy+13,31,31),ability.icon??"crystal");Text(new Rect(77,yy+11,590,30),ability.name,label,mint);
                Text(new Rect(77,yy+49,618,h+3),description,small,white);yy+=81+h;
            }
            GUI.EndScrollView();
            Text(new Rect(x+25,y+682,1054,26),CreatureCatalog.Kinds.Length+" 种生物  ·  所有价格与战斗属性由房主服务器确认  ·  鼠标滚轮浏览",micro,muted);
        }

        void Room()
        {
            Shade();float x=width/2-300,y=height/2-280;
            Panel(new Rect(x,y,600,560),dark);
            Text(new Rect(x+30,y+25,500,42),"集结你的对手",heading,mint);
            if(Button(new Rect(x+522,y+24,45,35),"×",new Color(.15f,.24f,.22f),white))app.ShowRoom=false;
            Text(new Rect(x+30,y+85,540,50),app.Hosting?"服务器正在本机运行 · TCP "+app.PortText:"连接至 "+app.Address+":"+app.PortText,label);
            Text(new Rect(x+30,y+139,540,48),app.Hosting?"局域网地址："+app.LocalAddresses:"所有玩家连接同一位房主即可开始",small);
            float yy=y+204;
            for(int slot=0;slot<4;slot++)
            {
                var p=app.State.players.Find(a=>a.slot==slot);
                Fill(new Rect(x+30,yy,540,42),new Color(.15f,.23f,.2f,.55f));
                Fill(new Rect(x+30,yy,4,42),Team(slot));
                Text(new Rect(x+47,yy+8,360,28),p==null?"等待加入…":p.name,label,p==null?muted:white);
                Text(new Rect(x+430,yy+9,115,25),p==null?"空位":p.bot?"电脑":p.id==app.State.hostId?"房主":"玩家",small);
                yy+=49;
            }
            if(app.State.phase=="lobby")
            {
                bool host=app.State.hostId==app.PlayerId;GUI.enabled=host&&app.Connected;
                if(Button(new Rect(x+30,y+414,540,49),host?"开始对战 · 空位由电脑加入":"等待房主开始",mint))
                    app.Send(new GameCommand{type="start",bots=true});
                GUI.enabled=true;
            }
            else Text(new Rect(x+30,y+417,540,40),"穿透时映射 TCP "+app.PortText+" 到本机同一端口，朋友使用映射地址加入。",small);
            if(!app.Connected&&Button(new Rect(x+30,y+483,260,43),"重新连接原队伍",mint))app.Reconnect();
            if(Button(new Rect(x+310,y+483,260,43),app.Hosting?"关闭服务器并离开":"离开服务器",new Color(.25f,.16f,.16f),white))app.Leave();
        }

        void Results()
        {
            Text(new Rect(width/2-180,height-64,360,30),"战斗结束 · 援军即将抵达",centered,muted);
        }

        void Finished()
        {
            Shade();float x=width/2-290,y=height/2-210;
            Panel(new Rect(x,y,580,420),dark);
            var p=app.State.players.Find(a=>a.id==app.State.winnerId);
            Text(new Rect(x+30,y+30,520,36),"MATCH COMPLETE",small,mint);
            Text(new Rect(x+30,y+84,520,90),p==null?"本局平局":p.id==app.PlayerId?"你的军团，最后屹立。":"斗兽场决出了冠军",heading);
            Text(new Rect(x+30,y+182,520,55),p==null?"所有军团同归于尽。":p.name+" 赢得了这场 "+app.State.round+" 回合的对战。",label,muted);
            if(app.State.hostId==app.PlayerId&&Button(new Rect(x+30,y+262,520,48),"再来一局  →",mint))
                app.Send(new GameCommand{type="restart"});
            if(Button(new Rect(x+30,y+327,520,43),"返回主菜单",new Color(.15f,.24f,.22f),white))app.Leave();
        }

        void Help()
        {
            Shade();float x=width/2-356,y=height/2-332;
            Panel(new Rect(x,y,712,664),dark);
            Text(new Rect(x+30,y+27,610,44),"你的生物，你的阵形。",heading,mint);
            if(Button(new Rect(x+637,y+25,43,35),"×",new Color(.15f,.24f,.22f),white))app.ShowHelp=false;
            string[] names={"01  招募与金币","02  永久技能","03  自由布阵与交锋","04  中央事件与联机"};
            string[] bodies={
                "开局 12 金币。每轮收入 6 + min(5, 击杀数) + 名次奖励 4/3/2/2。悬赏另计：每击杀领先者 3 只原始生物得 1 金币，每人每轮最多 3。",
                "招募页可花 2 金币刷新；军团中可遣散生物，返还实付价格 50%（向下取整，保留至少一只）。免费初始生物不返金币。永久技能商店按 M 打开，技能适用现有及未来生物。",
                "WASD 或点击地面移动 Steve，拖动生物自由摆位，点击准备开战。悬停即看属性，停留 1 秒展开技能。滚轮缩放；战斗时右侧显示指挥官血条，其余阶段移至右边缘展开。",
                "每轮可能有中央首领或金币资源，招募后提示本轮事件；首领优先攻击总生命最高军团，击杀方获得额外金币。房主开服后，朋友填写局域网 IP 或 TCP 穿透地址加入。"};
            float yy=y+92;
            for(int i=0;i<names.Length;i++)
            {
                Text(new Rect(x+32,yy,645,26),names[i],label,mint);
                string body=ArenaDescription.Format(bodies[i]);float bodyHeight=small.CalcHeight(new GUIContent(body),645);
                Text(new Rect(x+32,yy+32,645,bodyHeight+4),body,small,white);
                yy+=bodyHeight+58;
            }
            Text(new Rect(x+32,y+618,645,28),"无需登录或中心服务器 · 军团数量不限 · 技能永久保留",small,muted);
        }

        void Hover()
        {
            if(Time.unscaledTime>hoverUntil||app.State==null||app.ShowRoom||app.ShowHelp||app.ShowBestiary||app.State.phase=="finished")return;
            if(app.State.phase=="recruit"&&app.Me!=null&&app.Me.recruitPending)return;
            hoverMotion.Step(1,23,.64f);
            if(inspectionDefinition!=null){Inspection();return;}
            if(string.IsNullOrEmpty(hoverTitle))return;
            float panelWidth=384,innerWidth=344;
            float bodyHeight=label.CalcHeight(new GUIContent(hoverBody??""),innerWidth);
            float titleHeight=Mathf.Max(36,heading.CalcHeight(new GUIContent(hoverTitle),innerWidth));
            float panelHeight=titleHeight+bodyHeight+48;Rect r=TooltipRect(panelWidth,panelHeight);
            Panel(r,new Color(.07f,.11f,.12f,.99f));
            Text(new Rect(r.x+20,r.y+13,innerWidth,titleHeight),hoverTitle,heading,white);
            Fill(new Rect(r.x+20,r.y+titleHeight+21,innerWidth,1),copper);
            Text(new Rect(r.x+20,r.y+titleHeight+31,innerWidth,bodyHeight+3),hoverBody,label,muted);
        }
        Rect TooltipRect(float w,float h)
        {
            Vector2 p=new Vector2(hoverScreenPosition.x/scale,(Screen.height-hoverScreenPosition.y)/scale);float x=p.x+22,y=p.y+20;
            if(x+w>width-18)x=p.x-w-18;if(y+h>height-55)y=p.y-h-12;
            y+=(1-Mathf.Clamp(hoverMotion.value,0,1.12f))*8;
            return new Rect(Mathf.Clamp(x,18,Mathf.Max(18,width-w-18)),Mathf.Clamp(y,18,Mathf.Max(18,height-h-18)),w,h);
        }

        void CreatureIcon(Rect r,string kind,Color c)
        {
            float cx=r.center.x,cy=r.center.y;
            Fill(new Rect(cx-54,cy+45,108,6),new Color(.01f,.03f,.03f,.8f));
            if(kind=="slime")
            {
                Fill(new Rect(cx-42,cy-34,84,78),new Color(c.r,c.g,c.b,.9f));
                Fill(new Rect(cx-22,cy-9,13,13),dark);Fill(new Rect(cx+12,cy-9,13,13),dark);Fill(new Rect(cx-5,cy+20,17,7),dark);return;
            }
            if(kind=="spider")
            {
                for(int i=0;i<4;i++){Fill(new Rect(cx-64+i*7,cy-9+i*12,31,6),c);Fill(new Rect(cx+32,cy-9+i*12,31-i*7,6),c);}
                Fill(new Rect(cx-31,cy-14,62,45),c);Fill(new Rect(cx-30,cy+4,60,32),c*.8f);
                Fill(new Rect(cx-17,cy+13,10,7),Color.red);Fill(new Rect(cx+9,cy+13,10,7),Color.red);return;
            }
            Color skin=kind=="zombie"?new Color(.43f,.65f,.3f):c;
            Fill(new Rect(cx-28,cy-52,56,48),skin);
            Fill(new Rect(cx-19,cy-34,10,8),dark);Fill(new Rect(cx+9,cy-34,10,8),dark);
            if(kind=="creeper"){Fill(new Rect(cx-6,cy-22,15,18),dark);Fill(new Rect(cx-18,cy-8,12,12),dark);Fill(new Rect(cx+8,cy-8,12,12),dark);}
            Fill(new Rect(cx-22,cy-1,44,39),kind=="zombie"?new Color(.18f,.56f,.55f):c*.9f);
            Fill(new Rect(cx-39,cy+1,13,40),skin);Fill(new Rect(cx+27,cy+1,13,40),skin);
            Fill(new Rect(cx-21,cy+39,16,17),kind=="zombie"?new Color(.3f,.33f,.58f):c*.75f);
            Fill(new Rect(cx+5,cy+39,16,17),kind=="zombie"?new Color(.3f,.33f,.58f):c*.75f);
            if(kind=="golem")Fill(new Rect(cx-12,cy+4,7,30),new Color(.28f,.48f,.24f));
        }

        static string CategoryName(string category)
        {
            switch(category){case "passive":return "友善生物";case "neutral":return "中立生物";case "hostile":return "敌对生物";case "boss":return "首领生物";default:return category??"";}
        }
        static string TagName(string tag)
        {
            switch(tag)
            {
                case "aquatic":return "水生";case "arthropod":return "节肢";case "beast":case "animal":return "动物";
                case "boss":return "首领";case "construct":return "构装";case "end":return "末地";case "flying":return "飞行";
                case "golem":return "傀儡";case "illager":return "灾厄";case "monster":return "怪物";case "mount":return "坐骑";
                case "nether":return "下界";case "skeleton":return "骷髅";case "support":return "支援";case "undead":return "亡灵";
                case "villager":return "村民";case "zombie":return "僵尸";default:return tag??"";
            }
        }
        static string TagNames(string[] tags)
        {
            if(tags==null||tags.Length==0)return "";
            var names=new string[tags.Length];for(int i=0;i<tags.Length;i++)names[i]=TagName(tags[i]);
            return string.Join(" · ",names);
        }
        static bool SearchContains(string text,string query){return !string.IsNullOrEmpty(text)&&text.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;}
        static bool MatchesSearch(CreatureDefinition definition,string search)
        {
            string query=(search??"").Trim();if(query.Length==0)return true;
            if(SearchContains(definition.name,query)||SearchContains(definition.id,query)||SearchContains(definition.role,query)||SearchContains(definition.category,query)||SearchContains(CategoryName(definition.category),query))return true;
            foreach(string tag in definition.tags??new string[0])if(SearchContains(tag,query)||SearchContains(TagName(tag),query))return true;
            return false;
        }

        static string PhaseName(string p)
        {
            switch(p){case "lobby":return "等待入场";case "recruit":return "选择援军";case "prep":return "自由布阵";case "battle":return "四方混战";case "results":return "回合结算";default:return "对战结束";}
        }
        Color Team(int slot){Color c;return ColorUtility.TryParseHtmlString(CreatureCatalog.TeamColors[Mathf.Clamp(slot,0,3)],out c)?c:mint;}
        static string Trim(string value,int length)
        {
            if(string.IsNullOrEmpty(value))return "冒险家";
            int index=0,count=0;
            while(index<value.Length&&count<length)
            {
                index+=char.IsHighSurrogate(value[index])&&index+1<value.Length&&char.IsLowSurrogate(value[index+1])?2:1;
                count++;
            }
            return index<value.Length?value.Substring(0,index)+"…":value;
        }
        void Shade(){Fill(new Rect(0,0,width,height),new Color(.005f,.015f,.02f,.66f));}
        void Panel(Rect r,Color c)
        {
            Fill(new Rect(r.x+4,r.y+5,r.width,r.height),new Color(.01f,.015f,.015f,.4f));Fill(r,new Color(.025f,.03f,.035f,c.a));
            Rect inner=new Rect(r.x+3,r.y+3,r.width-6,r.height-6);Fill(inner,c);
            Color previous=GUI.color;GUI.color=new Color(1,1,1,.4f*c.a);GUI.DrawTexture(inner,ArenaIcons.Stone,ScaleMode.StretchToFill,true);GUI.color=previous;
            Fill(new Rect(r.x+1,r.y+1,r.width-2,2),new Color(.54f,.4f,.23f,c.a));
            Fill(new Rect(r.x+1,r.y+3,2,r.height-5),new Color(.4f,.29f,.17f,c.a));
            Fill(new Rect(r.xMax-3,r.y+3,2,r.height-5),new Color(.19f,.15f,.1f,c.a));
            Fill(new Rect(r.x+3,r.yMax-3,r.width-6,2),new Color(.13f,.13f,.11f,c.a));
            Fill(new Rect(r.x+7,r.y+6,12,2),gold);Fill(new Rect(r.xMax-19,r.y+6,12,2),gold);
            Fill(new Rect(r.x+6,r.y+7,2,10),copper);Fill(new Rect(r.xMax-8,r.y+7,2,10),copper);
            if(r.height>60){Fill(new Rect(r.x+5,r.yMax-13,3,6),new Color(mint.r,mint.g,mint.b,.7f));Fill(new Rect(r.xMax-8,r.yMax-13,3,6),new Color(mint.r,mint.g,mint.b,.7f));}
        }
        static void Fill(Rect r,Color color){Color old=GUI.color;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        void Text(Rect r,string text,GUIStyle style,Color? color=null)
        {
            // GUI.color multiplies the style color; setting it twice made muted Chinese almost black.
            Color old=GUI.color,oldContent=GUI.contentColor,oldText=style.normal.textColor;
            GUI.color=Color.white;GUI.contentColor=Color.white;
            if(color.HasValue)style.normal.textColor=color.Value;
            GUI.Label(r,text??"",style);
            style.normal.textColor=oldText;GUI.color=old;GUI.contentColor=oldContent;
        }
        bool Button(Rect r,string text,Color background,Color? foreground=null,string cue="click")
        {
            bool previousEnabled=GUI.enabled;
            if(!drawingAudio&&AudioBlocksUnderlying)GUI.enabled=false;
            if(!drawingAudio&&!drawingSkills&&SkillBlocksUnderlying)GUI.enabled=false;
            if(!drawingAudio&&!drawingArmy&&EconomyBlocksUnderlying)GUI.enabled=false;
            bool hovered=GUI.enabled&&r.Contains(Event.current.mousePosition);
            int id=GUIUtility.GetControlID(FocusType.Passive);
            Spring motion;if(!buttonMotion.TryGetValue(id,out motion)){motion=new Spring();buttonMotion.Add(id,motion);}
            motion.Step(!GUI.enabled?0:hovered&&Input.GetMouseButton(0)?-1:hovered?1:0,27,.57f);
            lastButtonOffset=Mathf.Clamp(-motion.value*1.45f,-2.2f,2.2f);
            Color fill=GUI.enabled?Color.Lerp(background,Color.white,Mathf.Clamp01(motion.value)*.13f):Color.Lerp(background,new Color(.11f,.14f,.14f),.7f);
            Fill(r,new Color(.025f,.03f,.03f));Fill(new Rect(r.x+1,r.y+1,r.width-2,r.height-2),hovered?gold:copper);
            Fill(new Rect(r.x+3,r.y+3+lastButtonOffset*.4f,r.width-6,r.height-6),fill);
            Fill(new Rect(r.x+4,r.y+4+lastButtonOffset,r.width-8,2),Color.Lerp(fill,Color.white,.25f));Fill(new Rect(r.x+3,r.yMax-5,r.width-6,2),Color.Lerp(fill,Color.black,.3f));
            int oldSize=button.fontSize;button.fontSize=r.height<32?15:19;
            Color color=foreground??(background.g>.45f?new Color(.035f,.1f,.08f):white);
            button.normal.textColor=button.hover.textColor=button.active.textColor=color;
            RegisterPointerRect(r);
            bool pressed=GUI.Button(r,"",button);
            if(pressed&&cue!=null&&SoundCue!=null)SoundCue(cue);
            int oldCenter=centered.fontSize;centered.fontSize=button.fontSize;
            if(!string.IsNullOrEmpty(text))Text(new Rect(r.x+5,r.y+lastButtonOffset,r.width-10,r.height),text,centered,GUI.enabled?color:muted);
            centered.fontSize=oldCenter;button.fontSize=oldSize;GUI.enabled=previousEnabled;return pressed;
        }
        bool IconButton(Rect r,string icon,string text,Color background,string cue="click")
        {
            bool pressed=Button(r,"",background,null,cue);float size=Mathf.Min(28,r.height-12);
            ArenaIcons.Draw(new Rect(string.IsNullOrEmpty(text)?r.center.x-size/2:r.x+11,r.y+(r.height-size)/2+lastButtonOffset,size,size),icon);
            if(!string.IsNullOrEmpty(text))
            {
                int old=centered.fontSize;centered.fontSize=r.width<225?17:19;
                Text(new Rect(r.x+47,r.y+2+lastButtonOffset,r.width-56,r.height-4),text,centered,GUI.enabled?(background.g>.45f?new Color(.035f,.1f,.08f):white):muted);centered.fontSize=old;
            }
            return pressed;
        }
        void Stat(Rect r,string icon,string text)
        {
            ArenaIcons.Draw(new Rect(r.x,r.y,25,25),icon);Text(new Rect(r.x+33,r.y-1,Mathf.Max(30,r.width-31),r.height),text,label,white);
        }
        Vector2 Mouse(){Vector2 p=HudPointer;return new Vector2(p.x/scale,(Screen.height-p.y)/scale);}
        Color MobColor(CreatureDefinition definition){Color c;return ColorUtility.TryParseHtmlString(definition.color,out c)?c:mint;}
        static string Compact(string value,int count)
        {
            if(string.IsNullOrEmpty(value))return "";int i=0,n=0;while(i<value.Length&&n<count){i+=char.IsHighSurrogate(value[i])&&i+1<value.Length&&char.IsLowSurrogate(value[i+1])?2:1;n++;}return i<value.Length?value.Substring(0,i)+"…":value;
        }
        static string ItemIcon(TradeDefinition item)
        {
            string icon=(item.icon??"").ToLowerInvariant();if(icon.Contains("shield")||item.armor>0)return "shield";if(icon.Contains("heart")||item.hp>0)return "heart";if(icon.Contains("speed")||item.attackSpeed>0)return "speed";if(item.type=="skill")return "book";return "sword";
        }
        static string TargetName(string target)
        {
            if(target=="all")return "全体军团";
            if(target=="skeleton")return "骷髅系生物";
            if(target=="zombie")return "僵尸系生物";
            if(target=="beast")return "动物生物";
            if(target=="nether")return "下界生物";
            if(target=="golem")return "傀儡生物";
            foreach(string kind in CreatureCatalog.Kinds)if(kind==target)return CreatureCatalog.Get(kind).name;
            switch(target){case "undead":return "亡灵生物";case "animal":case "passive":return "动物伙伴";case "arthropod":return "节肢生物";case "illager":return "灾厄生物";case "flying":return "飞行生物";case "hostile":return "敌对生物";case "aquatic":return "水生生物";case "boss":return "首领生物";default:return "对应类别";}
        }
        string InputBox(Rect r,string name,string value,int limit)
        {
            bool previousEnabled=GUI.enabled;if(AudioBlocksUnderlying)GUI.enabled=false;
            RegisterPointerRect(r,true);
            Color old=GUI.backgroundColor;GUI.backgroundColor=new Color(.12f,.2f,.18f);
            GUI.SetNextControlName(name);string result=GUI.TextField(r,value??"",limit,field);GUI.backgroundColor=old;GUI.enabled=previousEnabled;return result;
        }
    }
}
