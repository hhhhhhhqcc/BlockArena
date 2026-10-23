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
        string bestiarySearch="", bestiaryKind, bestiaryCategory="all";
        bool bestiaryOwnedOnly;
        int bestiarySort;
        readonly Dictionary<string,int> bestiaryOwnedCounts=new Dictionary<string,int>();
        readonly Color gold=new Color(.93f,.72f,.3f);
        readonly Color copper=new Color(.5f,.34f,.18f);
        Vector2 hoverScreenPosition;
        float hoverUntil;
        int soundHoverId=-1,soundHoverFrame=-10;
        float nextHoverSound;
        bool roomVisibleLastDraw;
        internal bool RoomVisibleForVerification { get { return roomVisibleLastDraw; } }
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

        Rect HeaderRect { get { return new Rect(width/2-337,18,674,54); } }
        Rect MarketRect { get { EnsureMarketPosition(); return new Rect(marketPosition.x,marketPosition.y,476,434); } }
        Rect MarketToggleRect { get { Rect r=CommandDockRect;return new Rect(r.x+414,r.y+22,130,44); } }
        Rect BestiaryToggleRect { get { return Rect.zero; } }
        Rect PrepareBannerRect { get { return new Rect(width/2-210,ReadyRect.y-31,420,25); } }
        Rect ReadyRect { get { Rect r=CommandDockRect;return new Rect(r.x+574,r.y+19,298,50); } }
        Rect NoticeRect { get { return new Rect(width/2-265,CommandDockRect.y-61,530,44); } }

        public bool Contains(Vector2 screen)
        {
            Metrics(); UpdateMarketPointer(); UpdateRoster();
            if(ShowSettings||app.QuickStarting)return true;
            if (inspectionPointerOwned || inspectionDragging || inspectionReleasedFrame == Time.frameCount || marketPointerOwned || marketDragging || releasedPointerFrame == Time.frameCount || audioPointerOwned || audioReleasedFrame == Time.frameCount || skillPointerOwned || skillReleasedFrame == Time.frameCount) return true;
            Vector2 p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            if (CommunicationCovers(p)) return true;
            if (BattlefieldHudCovers(p)) return true;
            if (AudioCovers(p)) return true;
            if (EconomyCovers(p)) return true;
            var state = app.State; var me = app.Me;
            if (state == null || app.ShowGameMenu || app.ShowRoom || app.ShowBestiary || state.phase == "lobby" || state.phase == "finished" ||
                RecruitVisible) return true;
            if (HeaderRect.Contains(p) || (CommandDockVisible&&CommandDockRect.Contains(p)) || RosterCovers(p) || PinnedInspectionCovers(p) || (RecruitPending&&!RecruitVisible&&RecruitRestoreRect.Contains(p))) return true;
            if (app.CanPrepare && (((app.Me.ready || state.phase == "recruit") && PrepareBannerRect.Contains(p)) || ReadyRect.Contains(p) || MarketToggleRect.Contains(p))) return true;
            if(MarketVisible&&MarketRect.Contains(p))return true;
            if (!string.IsNullOrEmpty(app.Notice) && NoticeRect.Contains(p)) return true;
            return SkillUiCovers(p);
        }

        public void SetHover(string titleText, string bodyText, Vector2 screenPosition)
        {
            if(!LayerAcceptsPointer())return;
            hoveredObjective=null;
            commanderTooltip=false;
            ResetInspection();
            PrepareHover(titleText);
            inspectionDefinition=null;
            hoverTitle = titleText;
            hoverBody = ArenaDescription.Format(bodyText);
            hoverScreenPosition = screenPosition;
            hoverUntil = Time.unscaledTime + .15f;
        }

        public void ClearHover() { hoverTitle = null; hoverBody = null; hoveredObjective=null;inspectionDefinition=null;commanderTooltip=false;hoverTooltipRect=Rect.zero; ResetInspection(); }
        public void SetInspection(CreatureDefinition definition,FighterState fighter,UnitState unit,PlayerState owner,Vector2 screenPosition)
        {
            if(definition==null){ClearHover();return;}
            hoveredObjective=null;
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
            bool priorEnabled=GUI.enabled;Color priorColor=GUI.color,priorContent=GUI.contentColor,priorBackground=GUI.backgroundColor;
            try {
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
            GUI.color = GUI.contentColor = Color.white;GUI.enabled=true;
            roomVisibleLastDraw=false;
            if(resetMenuFocus){GUIUtility.keyboardControl=0;resetMenuFocus=false;}
            if(ShowSettings&&!app.QuickStarting){
                DrawAudio();
                if(app.State!=null&&app.ShowRoom){roomVisibleLastDraw=true;DrawModal(3,Room);}
                if(app.ShowBestiary)DrawModal(5,Bestiary);
            }else{
            if (app.State == null||app.QuickStarting) DrawModal(1,Menu);
            else
            {
                if(app.State.phase!="lobby"){
                DrawCommandDock();
                Header(); Health(); DrawRosterTab();
                if (app.CanPrepare) Preparation();
                if(MarketVisible) {drawingMarket=true;Market();drawingMarket=false;}
                DrawEconomyObjectives();
                if (app.State.phase == "results") Results();
                if (RecruitVisible) DrawModal(2,Recruit); else DrawRecruitRestore();
                if (app.Me != null && app.Me.eliminated && app.State.phase != "finished")
                    Text(new Rect(30,112,460,50),"你的军团已退场 · 继续观战",label);
                if (app.State.phase == "finished" && Time.unscaledTime - finishedAt >= 4.6f && (app.World == null || !app.World.RitualActive)) DrawModal(4,Finished);
                }
            }
            DrawBattlefieldHud();
            if(app.ShowGameMenu)DrawModal(7,GameMenu);
            if(!app.QuickStarting&&app.State!=null&&(app.ShowRoom||app.State.phase=="lobby")&&!(app.State.phase=="lobby"&&app.ShowGameMenu&&!app.ShowRoom)){roomVisibleLastDraw=true;DrawModal(3,Room);}
            if(app.ShowBestiary) DrawModal(5,Bestiary);
            DrawSkillPanels();
            DrawPinnedInspection();
            if (!AudioBlocksUnderlying) Hover();
            }
            DrawCommunication();
            if (!string.IsNullOrEmpty(app.Notice))
            {
                var r = NoticeRect;
                Panel(r,new Color(.03f,.08f,.07f,.94f));
                Text(r,app.Notice,centered);
            }
            } finally {
            GUI.matrix = previous;
            GUI.enabled=priorEnabled;GUI.color=priorColor;GUI.contentColor=priorContent;GUI.backgroundColor=priorBackground;
            drawingModal=drawingMarket=drawingAudio=drawingCommander=drawingSkills=drawingArmy=drawingSkillDetails=false;
            }
        }

        void Header()
        {
            Rect r=HeaderRect;Panel(r,surface);
            string phase=app.State.phase;
            string phaseName=PhaseName(phase);
            Text(new Rect(22,25,206,30),"方块斗兽场",label,gold);
            Text(new Rect(r.x+17,r.y+13,204,30),(phase=="lobby"?"":app.State.round.ToString("00")+"  ·  ")+phaseName,label,white);
            Fill(new Rect(r.x+213,r.y+15,1,24),line);
            if(!WorldSummaryVisible)Text(new Rect(r.x+235,r.y+15,322,28),phase=="lobby"?"等待指挥官入场":phase=="prep"||phase=="recruit"?"招募  ·  布阵  ·  强化":"",small,muted);
            if(phase!="lobby"&&phase!="finished")Text(new Rect(r.xMax-78,r.y+12,65,32),Mathf.CeilToInt(app.State.timeLeft)+"s",number,app.State.timeLeft<8?gold:mint);
            if(!app.Connected)Text(new Rect(width-344,25,326,25),"连接中断 · Esc 查看房间",small,new Color(1,.57f,.43f));
        }

        void Health()
        {
            Rect roster=RosterRect;
            Panel(roster,surface);
            foreach(var p in app.State.players)
            {
                PlayerVisual motion;playerVisuals.TryGetValue(p.id,out motion);
                float shownHp=motion==null?p.hp:motion.displayHp;
                bool shownEliminated=p.eliminated&&shownHp<=0;
                float damageAge=motion==null?10:Time.unscaledTime-motion.damagedAt;
                float shake=damageAge<.5f?Mathf.Sin(damageAge*76)*5*Mathf.Exp(-damageAge*7):0;
                Rect row=new Rect(roster.x+shake,roster.y+p.slot*60,roster.width,54);Color team=Team(p.slot);
                RoundFill(row,new Color(.04f,.075f,.085f,.7f),8);
                if(p.slot>0)Fill(new Rect(row.x+14,row.y-3,row.width-28,1),new Color(line.r,line.g,line.b,.42f));
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
            if(IconButton(ReadyRect,"ready",app.Me.ready?"已准备  ·  取消":"准备完毕",app.Me.ready?new Color(.18f,.36f,.32f):mint,"ready"))app.Send(new GameCommand{type="ready"});
            if(IconButton(MarketToggleRect,"bag",app.ShowShop?"商店  −":"商店  M",new Color(.11f,.20f,.22f)))app.ShowShop=!app.ShowShop;
        }

        void Market()
        {
            Rect r=MarketRect;Panel(r,new Color(.075f,.11f,.12f,.99f));
            float reveal=Mathf.Clamp(marketMotion.value,0,1.12f),bump=(1-reveal)*8;
            // The window and every button keep fixed hit rectangles; only inset ornament moves.
            Fill(new Rect(r.x+13,r.y+49,(r.width-26)*Mathf.Clamp01(reveal),2),mint);
            PanelHeading(r,"bag","永久技能商店");
            for(int i=0;i<3;i++)Fill(new Rect(r.x+319+i*7,r.y+22,3,7),new Color(.59f,.47f,.3f));
            bool active=GUI.enabled;GUI.enabled=active&&app.ShowShop&&!marketDragging;
            if(IconButton(new Rect(r.xMax-47,r.y+9,35,33),"close","",new Color(.17f,.22f,.23f)))app.ShowShop=false;
            Text(new Rect(r.x+15,r.y+60,446,25),"永久强化  ·  当前与未来生物",micro,muted);
            if(app.Me.market!=null)for(int i=0;i<Mathf.Min(4,app.Me.market.Count);i++)
            {
                ShopOffer offer=app.Me.market[i];TradeDefinition item=TradeCatalog.Get(offer.itemId);if(item.type!="skill")continue;
                Rect card=MarketSkillCardRect(i);int matches=MatchingArmyCount(item);
                bool hovered=GUI.enabled&&LayerAcceptsPointer()&&card.Contains(Mouse())&&!marketDragging;
                Panel(card,hovered?new Color(.145f,.21f,.22f,.99f):new Color(.09f,.14f,.15f,.99f));
                if(matches>0)Fill(new Rect(card.x+4,card.y+5,3,card.height-10),mint);
                marketCards[i].Step(hovered?1:0,23,.57f);
                float lift=marketCards[i].value*2.8f;
                ArenaIcons.Draw(new Rect(card.x+10,card.y+10+bump*.5f-lift,28,28),ItemIcon(item));
                Text(new Rect(card.x+43,card.y+9,162,26),item.name,micro,white);
                Text(new Rect(card.x+11,card.y+42,193,24),TargetName(item.target),micro,gold);
                ArenaIcons.Draw(new Rect(card.x+11,card.y+69,18,18),matches>0?"ready":"range");
                Text(new Rect(card.x+37,card.y+67,168,24),matches>0?matches+" 只受益":"暂无匹配",micro,matches>0?mint:muted);
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
            bestiaryOwnedCounts.Clear();if(app.Me!=null&&app.Me.units!=null)foreach(var unit in app.Me.units){int n;bestiaryOwnedCounts.TryGetValue(unit.kind,out n);bestiaryOwnedCounts[unit.kind]=n+1;}
            Panel(new Rect(x,y,1120,716),new Color(.045f,.085f,.10f,.99f));
            ArenaIcons.Draw(new Rect(x+23,y+20,38,38),"book");Text(new Rect(x+74,y+20,830,42),"万物图鉴",heading,white);
            Fill(new Rect(x+24,y+69,1072,1),line);
            Fill(new Rect(x+336,y+89,1,584),line);
            if(IconButton(new Rect(x+1056,y+20,40,36),"close","",new Color(.2f,.25f,.25f)))app.ShowBestiary=false;
            string before=bestiarySearch;
            bestiarySearch=InputBox(new Rect(x+24,y+81,296,43),"bestiary-search",bestiarySearch,60);
            if(before!=bestiarySearch)bestiaryScroll=Vector2.zero;
            if(string.IsNullOrEmpty(bestiarySearch))Text(new Rect(x+39,y+90,260,26),"名称 / 类别 / 标签",small,muted);
            string[] categories={"all","passive","neutral","hostile","boss"};
            for(int i=0;i<categories.Length;i++){
                string category=categories[i];Rect filter=new Rect(x+24+(i%3)*100,y+138+(i/3)*36,96,31);
                if(Button(filter,category=="all"?"全部":category=="passive"?"友善":category=="neutral"?"中立":category=="hostile"?"敌对":"首领",bestiaryCategory==category?new Color(.20f,.38f,.32f):new Color(.10f,.18f,.20f),bestiaryCategory==category?mint:muted)){bestiaryCategory=category;bestiaryScroll=Vector2.zero;}
            }
            if(Button(new Rect(x+224,y+174,96,31),bestiaryOwnedOnly?"已拥有 ✓":"已拥有",bestiaryOwnedOnly?new Color(.20f,.38f,.32f):new Color(.10f,.18f,.20f),bestiaryOwnedOnly?mint:muted)){bestiaryOwnedOnly=!bestiaryOwnedOnly;bestiaryScroll=Vector2.zero;}
            var matches=new List<string>();
            foreach(string kind in CreatureCatalog.Kinds){var d=CreatureCatalog.Get(kind);if((bestiaryCategory=="all"||d.category==bestiaryCategory)&&(!bestiaryOwnedOnly||BestiaryOwnedCount(kind)>0)&&MatchesSearch(d,bestiarySearch))matches.Add(kind);}
            matches.Sort((a,b)=>{var da=CreatureCatalog.Get(a);var db=CreatureCatalog.Get(b);int order=bestiarySort==0?da.price.CompareTo(db.price):bestiarySort==1?db.price.CompareTo(da.price):string.Compare(da.name,db.name,StringComparison.CurrentCulture);return order==0?string.CompareOrdinal(a,b):order;});
            if(string.IsNullOrEmpty(bestiaryKind))bestiaryKind=CreatureCatalog.Kinds[0];
            if(matches.Count>0&&!matches.Contains(bestiaryKind)){bestiaryKind=matches[0];detailScroll=Vector2.zero;}
            Text(new Rect(x+25,y+219,163,27),matches.Count+" / "+CreatureCatalog.Kinds.Length,micro,muted);
            if(Button(new Rect(x+183,y+215,137,29),bestiarySort==0?"价格 ↑":bestiarySort==1?"价格 ↓":"名称",new Color(.11f,.2f,.21f))){bestiarySort=(bestiarySort+1)%3;bestiaryScroll=Vector2.zero;}
            const float listHeight=416;
            bestiaryScroll=BeginUiScroll(new Rect(x+24,y+252,296,listHeight),bestiaryScroll,new Rect(0,0,274,Mathf.Max(listHeight,matches.Count*69)));
            if(matches.Count==0)Text(new Rect(20,36,230,40),"没有匹配的生物",centered,muted);
            for(int i=0;i<matches.Count;i++){
                if(i*69+66<bestiaryScroll.y||i*69>bestiaryScroll.y+listHeight)continue;
                var d=CreatureCatalog.Get(matches[i]);Rect row=new Rect(0,i*69,272,63);int owned=BestiaryOwnedCount(d.id);
                if(Button(row,"",bestiaryKind==d.id?new Color(.15f,.31f,.29f):new Color(.075f,.14f,.16f))){bestiaryKind=d.id;detailScroll=Vector2.zero;}
                ArenaIcons.Creature(new Rect(9,row.y+6,48,48),d.model??d.id,MobColor(d));
                Text(new Rect(68,row.y+7,154,25),d.name,label,d.category=="boss"?gold:white);
                ArenaIcons.Draw(new Rect(68,row.y+37,18,18),"coin");Text(new Rect(94,row.y+34,94,24),d.price.ToString(),micro,gold);
                if(owned>0)Text(new Rect(197,row.y+34,64,24),"×"+owned,micro,mint);
            }
            GUI.EndScrollView();
            CreatureDefinition selected=CreatureCatalog.Get(bestiaryKind);Color color=MobColor(selected);
            var abilityIds=selected.skills??new string[0];float contentHeight=479;
            foreach(string id in abilityIds)contentHeight+=77+small.CalcHeight(new GUIContent(ArenaDescription.Format(CreatureAbilities.Get(id).description)),596);
            detailScroll=BeginUiScroll(new Rect(x+350,y+84,738,588),detailScroll,new Rect(0,0,714,Mathf.Max(588,contentHeight)));
            RoundFill(new Rect(9,5,177,171),new Color(.025f,.06f,.075f),10);
            Fill(new Rect(18,160,159,2),new Color(color.r,color.g,color.b,.6f));
            float bob=Mathf.Sin(Time.unscaledTime*2.6f)*3;
            ArenaIcons.Creature(new Rect(19,15+bob,157,151),selected.model??selected.id,color);
            Text(new Rect(207,16,478,52),selected.name,title,selected.category=="boss"?gold:white);
            Text(new Rect(210,79,475,30),CategoryName(selected.category)+" · "+selected.role,small,Color.Lerp(color,white,.45f));
            ArenaIcons.Draw(new Rect(211,125,25,25),"coin");Text(new Rect(250,123,110,31),selected.price.ToString(),label,gold);
            for(int i=0;i<Mathf.Min(6,selected.tier);i++)Fill(new Rect(390+i*17,132,10,10),gold);
            Text(new Rect(19,195,678,67),ArenaDescription.Format(selected.description),label,muted);
            BestiaryAttributes(selected);
            Text(new Rect(20,396,665,45),"<color=#8DEACB>"+TagNames(selected.tags)+"</color>",small,mint);
            float yy=476;Text(new Rect(19,yy-35,665,29),"天赋技能",label,gold);
            foreach(string id in abilityIds){
                AbilityDefinition ability=CreatureAbilities.Get(id);string description=ArenaDescription.Format(ability.description);float h=small.CalcHeight(new GUIContent(description),596);
                RoundFill(new Rect(17,yy,678,63+h),new Color(.075f,.14f,.16f,.95f),8);Fill(new Rect(17,yy+10,2,43+h),mint);
                ArenaIcons.Draw(new Rect(31,yy+14,28,28),AbilityIcon(ability));Text(new Rect(77,yy+10,590,30),ability.name,label,mint);
                Text(new Rect(77,yy+46,596,h+3),description,small,white);yy+=77+h;
            }
            GUI.EndScrollView();
        }
        int BestiaryOwnedCount(string kind){int n;return bestiaryOwnedCounts.TryGetValue(kind,out n)?n:0;}

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
            if(app.State.hostId==app.PlayerId&&Button(new Rect(x+30,y+262,520,48),"返回大厅  →",mint))
                app.Send(new GameCommand{type="restart"});
            if(Button(new Rect(x+30,y+327,520,43),"返回主菜单",new Color(.15f,.24f,.22f),white))app.Leave();
        }

        void Hover()
        {
            hoverTooltipRect=Rect.zero;
            if(Time.unscaledTime>hoverUntil||app.State==null||app.ShowGameMenu||app.ShowBestiary||app.State.phase=="finished")return;
            if(RecruitVisible&&!(CommanderSelectable&&(CommanderRect.Contains(Mouse())||app.Tactics.SkillPickerOpen&&CommanderPickerRect.Contains(Mouse()))))return;
            hoverMotion.Step(1,23,.64f);
            if(inspectionDefinition!=null){Inspection();return;}
            if(hoveredObjective!=null){ObjectiveInspection();return;}
            if(string.IsNullOrEmpty(hoverTitle))return;
            float panelWidth=368,innerWidth=328;
            float bodyHeight=small.CalcHeight(new GUIContent(hoverBody??""),innerWidth);
            float titleHeight=Mathf.Max(31,label.CalcHeight(new GUIContent(hoverTitle),innerWidth));
            float panelHeight=titleHeight+bodyHeight+48;Rect r=commanderTooltip?CommanderTooltipRect(panelWidth,panelHeight):TooltipRect(panelWidth,panelHeight);
            hoverTooltipRect=r;
            Panel(r,new Color(.07f,.11f,.12f,.99f));
            Text(new Rect(r.x+20,r.y+13,innerWidth,titleHeight),hoverTitle,label,white);
            Fill(new Rect(r.x+20,r.y+titleHeight+21,innerWidth,1),line);
            Text(new Rect(r.x+20,r.y+titleHeight+31,innerWidth,bodyHeight+3),hoverBody,small,muted);
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
            switch(p){case "lobby":return "等待入场";case "recruit":case "prep":return "战前准备";case "reveal":return "战场展开";case "tactics":return "战术编排";case "battle":return "四方混战";case "results":return "回合结算";default:return "对战结束";}
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
            // A single material language: obsidian glass, a thin moss edge and warm metal accents.
            RoundedPanel(r,c);
        }
        static void RoundFill(Rect r,Color color,float radius=10)
        {
            radius=Mathf.Min(radius,Mathf.Min(r.width,r.height)*.5f);
            if(radius<=0){Fill(r,color);return;}
            // One native rounded quad avoids seams between pieces at fractional UI scales.
            bool enabled=GUI.enabled;GUI.enabled=true;Color previous=GUI.color;GUI.color=Color.white;
            GUI.DrawTexture(r,Texture2D.whiteTexture,ScaleMode.StretchToFill,true,0,color,0,radius);
            GUI.color=previous;GUI.enabled=enabled;
        }
        void RoundedPanel(Rect r,Color color)
        {
            RoundFill(new Rect(r.x+3,r.y+5,r.width,r.height),new Color(.005f,.02f,.025f,.45f),12);
            RoundFill(r,new Color(.25f,.37f,.35f,color.a),12);
            RoundFill(new Rect(r.x+1,r.y+1,r.width-2,r.height-2),color,11);
            Fill(new Rect(r.x+16,r.y+1,r.width-32,1),new Color(gold.r,gold.g,gold.b,.45f));
        }
        internal string HoverBodyText { get { return hoverBody??""; } }
        static void Fill(Rect r,Color color){Color old=GUI.color;bool enabled=GUI.enabled;GUI.enabled=true;GUI.color=color;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;GUI.enabled=enabled;}
        void Text(Rect r,string text,GUIStyle style,Color? color=null)
        {
            // GUI.color multiplies the style color; setting it twice made muted Chinese almost black.
            bool enabled=GUI.enabled;GUI.enabled=true;Color old=GUI.color,oldContent=GUI.contentColor,oldText=style.normal.textColor;
            GUI.color=Color.white;GUI.contentColor=Color.white;
            if(color.HasValue)style.normal.textColor=color.Value;
            GUI.Label(r,text??"",style);
            style.normal.textColor=oldText;GUI.color=old;GUI.contentColor=oldContent;GUI.enabled=enabled;
        }
        bool Button(Rect r,string text,Color background,Color? foreground=null,string cue="ui_click",bool rounded=true)
        {
            bool functional=GUI.enabled,interactive=functional&&LayerAcceptsPointer();
            bool hovered=interactive&&r.Contains(Event.current.mousePosition);
            int id=GUIUtility.GetControlID(FocusType.Passive);
            if(hovered&&Event.current.type==EventType.Repaint){
                if((soundHoverId!=id||Time.frameCount-soundHoverFrame>2)&&Time.unscaledTime>=nextHoverSound){
                    nextHoverSound=Time.unscaledTime+.1f;if(SoundCue!=null)SoundCue("ui_hover");
                }
                soundHoverId=id;soundHoverFrame=Time.frameCount;
            }
            Spring motion;if(!buttonMotion.TryGetValue(id,out motion)){motion=new Spring();buttonMotion.Add(id,motion);}
            motion.Step(!functional?0:hovered&&Input.GetMouseButton(0)?-1:hovered?1:0,27,.57f);
            lastButtonOffset=Mathf.Clamp(-motion.value*1.45f,-2.2f,2.2f);
            Color fill=ButtonFill(background,functional,Mathf.Clamp01(motion.value));
            GUI.enabled=true;
            float radius=r.height<33?6:9;
            RoundFill(new Rect(r.x,r.y+2,r.width,r.height),new Color(.005f,.018f,.024f,.46f),radius);
            RoundFill(r,hovered?new Color(.59f,.63f,.46f):new Color(.22f,.34f,.35f),radius);
            RoundFill(new Rect(r.x+1,r.y+1+lastButtonOffset*.25f,r.width-2,r.height-2),fill,radius-1);
            Fill(new Rect(r.x+10,r.y+2+lastButtonOffset,r.width-20,1),new Color(1,1,1,.075f));
            int oldSize=button.fontSize;button.fontSize=r.height<32?15:19;
            Color color=foreground??(background.g>.45f?new Color(.035f,.1f,.08f):white);
            GUI.enabled=interactive;RegisterPointerRect(r);bool pressed=GUI.Button(r,"",button);
            if(pressed)ClickFeedback(cue);
            GUI.enabled=true;int oldCenter=centered.fontSize;centered.fontSize=button.fontSize;
            if(!string.IsNullOrEmpty(text))Text(new Rect(r.x+5,r.y+lastButtonOffset,r.width-10,r.height),text,centered,functional?color:muted);
            centered.fontSize=oldCenter;button.fontSize=oldSize;GUI.enabled=functional;return pressed;
        }
        internal static Color ButtonFill(Color background,bool functional,float hover)
        { return functional?Color.Lerp(background,Color.white,Mathf.Clamp01(hover)*.10f):Color.Lerp(background,new Color(.11f,.14f,.14f),.7f); }
        bool IconButton(Rect r,string icon,string text,Color background,string cue="ui_click")
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
            bool previousEnabled=GUI.enabled;GUI.enabled=previousEnabled&&(LayerAcceptsPointer()||Event.current.type==EventType.Repaint||Event.current.type==EventType.Layout);
            RegisterPointerRect(r,true);
            Color old=GUI.backgroundColor;GUI.backgroundColor=new Color(.12f,.2f,.18f);
            GUI.SetNextControlName(name);string result=GUI.TextField(r,value??"",limit,field);GUI.backgroundColor=old;GUI.enabled=previousEnabled;return result;
        }
    }
}
