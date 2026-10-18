using System;
using System.Collections.Generic;
using System.Text;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Small native overlays leave the world visible; all input rectangles share the same UI scale.
    public sealed class ArenaHud
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

        Rect HeaderRect { get { return new Rect(22,22,width-44,67); } }
        Rect FooterRect { get { return new Rect(22,height-44,width-44,34); } }
        Rect MarketRect { get { return new Rect(width-924,height-530,896,366); } }
        Rect MarketToggleRect { get { return new Rect(width/2+147,height-101,142,47); } }
        Rect BestiaryToggleRect { get { return new Rect(width/2-289,height-101,142,47); } }
        Rect RosterRect { get { return new Rect(width-254,107,224,242); } }
        Rect PrepareBannerRect { get { return new Rect(width/2-370,ReadyRect.y-44,740,34); } }
        Rect ReadyRect { get { return new Rect(width/2-126,FooterRect.y-57,252,47); } }
        Rect SelectionRect { get { return new Rect(30,height-372,314,302); } }
        Rect NoticeRect { get { return new Rect(width/2-290,PrepareBannerRect.y-66,580,48); } }
        Rect ResultsRect { get { return new Rect(width/2-380,height/2-183,760,366); } }

        public bool Contains(Vector2 screen)
        {
            Metrics();
            Vector2 p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            var state = app.State; var me = app.Me;
            if (state == null || app.ShowRoom || app.ShowHelp || app.ShowBestiary || state.phase == "finished" ||
                (state.phase == "recruit" && me != null && me.recruitPending && !me.eliminated)) return true;
            if (HeaderRect.Contains(p) || FooterRect.Contains(p) || RosterRect.Contains(p)) return true;
            if (app.CanPrepare && (PrepareBannerRect.Contains(p) || ReadyRect.Contains(p) || MarketToggleRect.Contains(p) || BestiaryToggleRect.Contains(p))) return true;
            if(app.CanPrepare&&app.ShowShop&&MarketRect.Contains(p))return true;
            if (state.phase == "results" && ResultsRect.Contains(p)) return true;
            if (!string.IsNullOrEmpty(app.Notice) && NoticeRect.Contains(p)) return true;
            return app.CanPrepare && app.SelectedId != null && SelectionRect.Contains(p);
        }

        public void SetHover(string titleText, string bodyText, Vector2 screenPosition)
        {
            inspectionDefinition=null;
            hoverTitle = titleText;
            hoverBody = bodyText;
            hoverScreenPosition = screenPosition;
            hoverUntil = Time.unscaledTime + .15f;
        }

        public void ClearHover() { hoverTitle = null; hoverBody = null; inspectionDefinition=null; }
        public void SetInspection(CreatureDefinition definition,FighterState fighter,UnitState unit,PlayerState owner,Vector2 screenPosition)
        {
            inspectionDefinition=definition;inspectionUnit=unit;inspectionOwner=owner;
            inspectionFighter=fighter??(owner!=null&&unit!=null?ArenaGame.PreviewFighter(owner,unit):null);
            hoverScreenPosition=screenPosition;hoverUntil=Time.unscaledTime+.15f;hoverTitle=null;
        }

        public void Draw()
        {
            Setup(); Metrics();
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale,scale,1));
            GUI.color = Color.white;
            if (app.State == null) Menu();
            else
            {
                Header(); Health();
                if (app.CanPrepare) Preparation();
                if(app.CanPrepare&&app.ShowShop) Market();
                Selection();
                if (app.State.phase == "results") Results();
                if (app.State.phase == "recruit" && app.Me != null && app.Me.recruitPending && !app.Me.eliminated) Recruit();
                if (app.Me != null && app.Me.eliminated && app.State.phase != "finished")
                    Text(new Rect(30,112,460,50),"你的军团已退场 · 继续观战",label);
                if (app.State.phase == "finished") Finished();
                if (app.ShowRoom) Room();
            }
            if(app.ShowBestiary) Bestiary();
            if (app.ShowHelp) Help();
            if (!string.IsNullOrEmpty(app.Notice))
            {
                var r = NoticeRect;
                Panel(r,new Color(.03f,.08f,.07f,.94f));
                Text(r,app.Notice,centered);
            }
            Hover();
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
            Panel(HeaderRect,new Color(.065f,.09f,.1f,.95f));
            ArenaIcons.Draw(new Rect(39,39,29,29),"crystal");
            Text(new Rect(78,33,265,37),"方块 · 斗兽场",heading,mint);
            Text(new Rect(79,62,250,26),"BLOCK  ARENA",micro,gold);
            string phase=app.State.phase;
            string phaseName=phase=="recruit"&&app.Me!=null&&!app.Me.recruitPending?"等待招募 · 自由布阵":PhaseName(phase);
            Text(new Rect(width/2-230,34,460,35),"第 "+app.State.round.ToString("00")+" 轮    "+phaseName,centered,white);
            if(phase!="lobby")Text(new Rect(width/2-100,67,200,20),Mathf.CeilToInt(app.State.timeLeft)+" 秒",centered,app.State.timeLeft<8?gold:muted);
            if(app.Me!=null)
            {
                ArenaIcons.Draw(new Rect(width-410,38,31,31),"coin");
                Text(new Rect(width-372,36,95,35),app.Me.gold.ToString(),number,gold);
            }
            if(IconButton(new Rect(width-258,38,58,35),"book","",new Color(.16f,.23f,.24f)))app.ShowBestiary=!app.ShowBestiary;
            if(Button(new Rect(width-189,38,68,35),"帮助",new Color(.15f,.24f,.22f),white))app.ShowHelp=true;
            if(Button(new Rect(width-110,38,68,35),"房间",mint))app.ShowRoom=true;
            Panel(FooterRect,new Color(.055f,.08f,.09f,.9f));
            Text(new Rect(34,height-37,width-300,25),app.CanPrepare?"WASD 移动  ·  拖动生物布阵  ·  M 打开商店  ·  滚轮缩放竞技场":"军团自动作战  ·  击杀与排名赚取金币  ·  滚轮缩放竞技场",small);
            Text(new Rect(width-216,height-37,180,25),app.Connected?"● 服务器已连接":"● 连接已中断",small,app.Connected?mint:new Color(1,.56f,.47f));
        }

        void Health()
        {
            float x=width-254,y=107;
            Panel(RosterRect,new Color(.025f,.065f,.06f,.8f));
            Text(new Rect(x+17,y+13,120,25),"指挥官生命",small);
            Text(new Rect(x+163,y+13,50,25),"/ 20",small);
            foreach(var p in app.State.players)
            {
                float yy=y+45+p.slot*41;Color c=Team(p.slot);
                Fill(new Rect(x+17,yy+7,5,23),c);
                Text(new Rect(x+32,yy,140,25),Trim(p.name,p.id==app.PlayerId?5:7)+(p.id==app.PlayerId?" ·你":""),small,p.eliminated?muted:white);
                Text(new Rect(x+175,yy-1,45,30),p.hp.ToString(),number,c);
                Fill(new Rect(x+33,yy+27,127,3),new Color(1,1,1,.09f));
                Fill(new Rect(x+33,yy+27,127*Mathf.Clamp01(p.hp/20f),3),c);
            }
            Text(new Rect(x+17,y+214,190,23),"军团 "+(app.Me==null?0:app.Me.units.Count)+"  ·  本轮击杀 "+(app.Me==null?0:app.Me.roundKills),small);
        }

        void Preparation()
        {
            string message=app.State.phase=="recruit"?"援军已就位，可以先布阵 · 等待其他玩家选择":
                app.Me.ready?"已准备，等待其他指挥官 · 调整阵形会取消准备":"自由布阵 · 装备强化单只生物，技能强化整类军团";
            Panel(PrepareBannerRect,new Color(.055f,.09f,.1f,.88f));
            Rect banner=PrepareBannerRect;Text(new Rect(banner.x+4,banner.y+2,banner.width-8,banner.height-4),message,centered,white);
            if(IconButton(ReadyRect,"ready",app.Me.ready?"已准备 · 取消":"准备完毕",app.Me.ready?new Color(.18f,.36f,.32f):mint))app.Send(new GameCommand{type="ready"});
            if(IconButton(MarketToggleRect,"bag",app.ShowShop?"收起商店":"商店 [M]",new Color(.19f,.24f,.24f)))app.ShowShop=!app.ShowShop;
            if(IconButton(BestiaryToggleRect,"book","生物图鉴",new Color(.19f,.24f,.24f)))app.ShowBestiary=true;
        }

        void Selection()
        {
            UnitState unit=SelectedUnit();if(!app.CanPrepare||unit==null)return;
            var item=CreatureCatalog.Get(unit.kind);Rect r=SelectionRect;float x=r.x,y=r.y;
            Panel(r,new Color(.08f,.12f,.13f,.98f));ArenaIcons.Creature(new Rect(x+16,y+13,59,59),item.model??item.id,MobColor(item));
            Text(new Rect(x+87,y+13,207,25),item.role,small,mint);Text(new Rect(x+87,y+42,207,36),item.name,heading);
            FighterState preview=ArenaGame.PreviewFighter(app.Me,unit);
            Stat(new Rect(x+19,y+97,132,30),"heart",preview.maxHp.ToString("0.#"));Stat(new Rect(x+162,y+97,133,30),"sword",preview.attack.ToString("0.#"));
            Text(new Rect(x+20,y+139,274,46),"攻速 "+preview.attackSpeed.ToString("0.##")+" / 秒   射程 "+preview.range.ToString("0.#"),small,muted);
            Text(new Rect(x+20,y+189,272,25),"装备栏  "+(unit.equipment==null?0:unit.equipment.Count)+" / 3",small,gold);
            for(int i=0;i<3;i++)
            {
                Rect slot=new Rect(x+20+i*92,y+224,84,59);Panel(slot,new Color(.1f,.15f,.16f,.95f));
                if(unit.equipment!=null&&i<unit.equipment.Count)
                {
                    var gear=TradeCatalog.Get(unit.equipment[i]);ArenaIcons.Draw(new Rect(slot.x+27,slot.y+5,30,30),ItemIcon(gear));Text(new Rect(slot.x+3,slot.y+38,78,20),Compact(gear.name,5),micro,gold);
                    if(slot.Contains(Mouse()))SetHover(gear.name,gear.description,Input.mousePosition);
                }
                else Text(new Rect(slot.x,slot.y+14,84,32),"＋",centered,muted);
            }
        }

        void Recruit()
        {
            Shade();float cardW=308,gap=22,total=cardW*3+gap*2,x=(width-total)/2,y=(height-674)/2;
            Text(new Rect(x,y-28,total,26),"灵魂契约  /  第 "+app.State.round.ToString("00")+" 轮",small,gold);
            Text(new Rect(x,y+7,total-250,54),app.State.round==1?"你的军团，由此启程。":"为下一场战斗，招募援军。",title,white);
            ArenaIcons.Draw(new Rect(x+total-205,y+14,36,36),"coin");Text(new Rect(x+total-160,y+12,160,40),app.Me.gold+" 金币",heading,gold);
            Text(new Rect(x,y+69,total,29),"花费金币选择 1 只加入永久军团，或保留金币直接进入准备。",label,muted);
            string[] choices=app.Me.shop;if(choices==null)return;
            bool pending=pendingRound==app.State.round&&Time.unscaledTime<pendingUntil;
            for(int i=0;i<Mathf.Min(3,choices.Length);i++)
            {
                CreatureDefinition item=CreatureCatalog.Get(choices[i]);float xx=x+i*(cardW+gap);Color c=MobColor(item);
                Rect card=new Rect(xx,y+116,cardW,438);Panel(card,new Color(.09f,.13f,.14f,.99f));
                Fill(new Rect(xx+8,y+124,cardW-16,3),c);ArenaIcons.Creature(new Rect(xx+73,y+143,162,137),item.model??item.id,c);
                Text(new Rect(xx+22,y+285,264,23),CategoryName(item.category)+"  ·  "+item.role,small,Color.Lerp(c,white,.45f));
                Text(new Rect(xx+22,y+317,264,39),item.name,heading,white);
                Text(new Rect(xx+22,y+364,264,57),Compact(item.description,28),small,muted);
                Stat(new Rect(xx+22,y+427,82,28),"heart",item.hp.ToString("0.#"));
                Stat(new Rect(xx+112,y+427,82,28),"sword",item.attack.ToString("0.#"));
                Stat(new Rect(xx+202,y+427,88,28),"speed",item.attackSpeed.ToString("0.##"));
                bool affordable=app.Me.gold>=item.price;GUI.enabled=!pending&&affordable&&app.Connected;
                if(IconButton(new Rect(xx+20,y+484,cardW-40,49),"coin",pending?"契约确认中…":affordable?"招募  ·  "+item.price+" 金币":"金币不足  ·  需要 "+item.price,new Color(.18f,.61f,.55f)))
                {pendingRound=app.State.round;pendingUntil=Time.unscaledTime+3;app.Send(new GameCommand{type="choose",index=i});}
                GUI.enabled=true;
            }
            GUI.enabled=!pending&&app.Connected;
            if(IconButton(new Rect(x+total-268,y+579,268,45),"skip","保留金币 · 跳过招募",new Color(.28f,.24f,.17f)))app.Send(new GameCommand{type="skip"});
            GUI.enabled=true;
            Text(new Rect(x,y+580,total-295,50),"剩余 "+Mathf.CeilToInt(app.State.timeLeft)+" 秒  ·  超时由服务器结算选择\n每轮基础收入 + 击杀奖励 + 排名奖励",small,muted);
        }

        void Market()
        {
            Rect r=MarketRect;Panel(r,new Color(.075f,.11f,.12f,.99f));
            ArenaIcons.Draw(new Rect(r.x+19,r.y+16,28,28),"bag");
            Text(new Rect(r.x+57,r.y+13,330,34),"流浪商人的补给",heading,gold);
            Text(new Rect(r.x+405,r.y+17,348,27),"下次换货 "+Mathf.CeilToInt(app.Me.shopRefreshLeft)+" 秒",small,muted);
            if(IconButton(new Rect(r.xMax-57,r.y+13,37,35),"close","",new Color(.2f,.23f,.23f)))app.ShowShop=false;
            UnitState target=SelectedUnit();
            Text(new Rect(r.x+21,r.y+53,800,27),target==null?"装备：先在场上选中一只生物  /  技能：影响对应的全体现在及未来生物":"装备目标："+CreatureCatalog.Get(target.kind).name+"  ·  "+(target.equipment==null?0:target.equipment.Count)+" / 3 件装备",small,target==null?muted:mint);
            if(app.Me.market!=null)for(int i=0;i<Mathf.Min(4,app.Me.market.Count);i++)
            {
                ShopOffer offer=app.Me.market[i];TradeDefinition item=TradeCatalog.Get(offer.itemId);
                Rect card=new Rect(r.x+21+i*216,r.y+91,204,214);Panel(card,new Color(.12f,.16f,.17f,.98f));
                ArenaIcons.Draw(new Rect(card.x+13,card.y+13,34,34),ItemIcon(item));
                Text(new Rect(card.x+55,card.y+11,136,30),item.name,label,white);
                Text(new Rect(card.x+14,card.y+51,176,22),item.type=="equipment"?"单只装备":"永久技能 · "+TargetName(item.target),micro,item.type=="equipment"?gold:mint);
                Text(new Rect(card.x+14,card.y+79,176,63),Compact(item.description,24),small,muted);
                bool owned=item.type=="skill"&&app.Me.upgrades!=null&&app.Me.upgrades.Contains(item.id);
                bool duplicate=target!=null&&target.equipment!=null&&target.equipment.Contains(item.id);
                bool full=target!=null&&target.equipment!=null&&target.equipment.Count>=3;
                bool matches=target!=null&&TradeCatalog.Applies(item,CreatureCatalog.Get(target.kind));
                string why=offer.sold?"本次已售出":owned?"已掌握":item.type=="equipment"&&target==null?"先选择一只生物":item.type=="equipment"&&!matches?"目标类型不适用":item.type=="equipment"&&duplicate?"该生物已装备":item.type=="equipment"&&full?"装备栏已满":app.Me.gold<offer.price?"金币不足":"";
                GUI.enabled=app.Connected&&string.IsNullOrEmpty(why);
                if(IconButton(new Rect(card.x+12,card.y+157,180,43),"coin",string.IsNullOrEmpty(why)?"购买 · "+offer.price:why,new Color(.24f,.41f,.36f)))
                    app.Send(new GameCommand{type="purchase",offerId=offer.id,unitId=item.type=="equipment"?app.SelectedId:null});
                GUI.enabled=true;
                if(card.Contains(Mouse()))SetHover(item.name,item.description+"\n"+(item.type=="equipment"?"应用到选中生物，最多 3 件。":"永久作用于"+TargetName(item.target)+"，包括未来招募。")+"\n价格 "+offer.price+" 金币",Input.mousePosition);
            }
            GUI.enabled=app.Me.gold>=2&&app.Connected;
            if(IconButton(new Rect(r.x+21,r.y+316,221,35),"refresh","更换货物 · 2 金币",new Color(.3f,.26f,.17f)))app.Send(new GameCommand{type="refresh"});
            GUI.enabled=true;
            Text(new Rect(r.x+261,r.y+320,600,27),"每轮及每 12 秒自动换货 · 技能永久保留 · 关闭商店可专心布阵",micro,muted);
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
            foreach(string id in abilityIds)contentHeight+=81+small.CalcHeight(new GUIContent(CreatureAbilities.Get(id).description),618);
            detailScroll=GUI.BeginScrollView(new Rect(x+350,y+84,738,588),detailScroll,new Rect(0,0,714,Mathf.Max(588,contentHeight)));
            ArenaIcons.Creature(new Rect(17,7,157,151),selected.model??selected.id,color);
            Text(new Rect(202,20,478,52),selected.name,title,white);Text(new Rect(205,86,480,30),CategoryName(selected.category)+"  /  "+selected.role,small,Color.Lerp(color,white,.45f));
            ArenaIcons.Draw(new Rect(203,127,28,28),"coin");Text(new Rect(243,127,350,31),selected.price+" 金币  ·  阶位 "+selected.tier,label,gold);
            Text(new Rect(17,185,678,75),selected.description,label,muted);
            Stat(new Rect(17,269,142,32),"heart",selected.hp.ToString("0.#"));Stat(new Rect(185,269,142,32),"sword",selected.attack.ToString("0.#"));Stat(new Rect(352,269,142,32),"speed",selected.attackSpeed.ToString("0.##")+"/秒");Stat(new Rect(520,269,158,32),"range",selected.range.ToString("0.#"));
            Text(new Rect(19,321,665,48),(selected.flying?"飞行生物   ·   ":"")+"移速 "+selected.speed.ToString("0.#")+"   ·   "+TagNames(selected.tags),small,mint);
            float yy=384;Text(new Rect(17,yy-25,660,29),"天赋与技能",label,gold);
            foreach(string id in abilityIds)
            {
                AbilityDefinition ability=CreatureAbilities.Get(id);float h=small.CalcHeight(new GUIContent(ability.description),618);
                Panel(new Rect(17,yy,678,66+h),new Color(.11f,.16f,.17f,.95f));ArenaIcons.Draw(new Rect(30,yy+13,31,31),ability.icon??"crystal");Text(new Rect(77,yy+11,590,30),ability.name,label,mint);
                Text(new Rect(77,yy+49,618,h+3),ability.description,small,white);yy+=81+h;
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
            Rect r=ResultsRect;Panel(r,new Color(.075f,.11f,.12f,.99f));
            ArenaIcons.Draw(new Rect(r.x+23,r.y+20,34,34),"sword");Text(new Rect(r.x+73,r.y+17,650,42),"第 "+app.State.round+" 轮 · 战利品结算",heading,gold);
            Text(new Rect(r.x+28,r.y+76,280,27),"指挥官",small,muted);Text(new Rect(r.x+337,r.y+76,95,27),"击杀",small,muted);Text(new Rect(r.x+458,r.y+76,100,27),"生命",small,muted);Text(new Rect(r.x+587,r.y+76,145,27),"金币收入",small,gold);
            int i=0;
            foreach(var result in app.State.placements)
            {
                var p=app.State.players.Find(a=>a.id==result.playerId);float yy=r.y+116+i*46;
                Fill(new Rect(r.x+24,yy-4,r.width-48,41),new Color(1,1,1,i%2==0?.04f:.015f));
                Text(new Rect(r.x+29,yy,290,32),result.rank+".  "+Trim(p==null?"军团":p.name,9),label,p==null?white:Team(p.slot));
                Text(new Rect(r.x+341,yy,92,32),result.kills.ToString(),label,white);
                Text(new Rect(r.x+460,yy,103,32),result.damage==0?"守住生命":"−"+result.damage,label,result.damage==0?mint:new Color(.96f,.52f,.38f));
                ArenaIcons.Draw(new Rect(r.x+587,yy,27,27),"coin");Text(new Rect(r.x+627,yy,100,32),"+"+result.income,label,gold);i++;
            }
            if(app.Me!=null)Text(new Rect(r.x+29,r.y+316,703,32),"你的收入：基础 "+app.Me.baseIncome+"  +  击杀 "+app.Me.killIncome+"  +  排名 "+app.Me.rankIncome+"  =  "+app.Me.lastIncome+" 金币",small,gold);
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
            string[] names={"01  招募与金币","02  装备与永久技能","03  自由布阵与交锋","04  局域网与内网穿透"};
            string[] bodies={
                "开局获得 12 金币。每轮从三只生物中付费招募一只，或跳过保留金币。每轮收入为基础 6 金币，加每次击杀 1 金币及排名奖励。",
                "按 M 打开或收起商店。选中生物后购买装备，每只最多 3 件；技能永久强化对应类别的现有与未来生物。每 12 秒自动换货，手动刷新 2 金币。",
                "WASD 或点击地面移动 Steve，拖动生物自由摆位，点击准备开战。战斗自动进行，悬停查看血条、技能与状态；滚轮缩放，战后生物恢复。",
                "房主本机开服，朋友填写房主的局域网 IPv4 和 TCP 端口。穿透时映射 TCP 7777，朋友填写映射后的域名和外网端口。房主退出即关服。"};
            float yy=y+92;
            for(int i=0;i<names.Length;i++)
            {
                Text(new Rect(x+32,yy,645,26),names[i],label,mint);
                float bodyHeight=small.CalcHeight(new GUIContent(bodies[i]),645);
                Text(new Rect(x+32,yy+32,645,bodyHeight+4),bodies[i],small,white);
                yy+=bodyHeight+58;
            }
            Text(new Rect(x+32,y+618,645,28),"无需登录或中心服务器 · 军团数量不限 · 装备与技能永久保留",small,muted);
        }

        void Hover()
        {
            if(Time.unscaledTime>hoverUntil||app.State==null||app.ShowRoom||app.ShowHelp||app.ShowBestiary||app.State.phase=="finished")return;
            if(app.State.phase=="recruit"&&app.Me!=null&&app.Me.recruitPending)return;
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
        void Inspection()
        {
            var d=inspectionDefinition;var f=inspectionFighter;
            float hp=f==null?d.hp:f.hp,maxHp=f==null?d.hp:f.maxHp;
            var skills=new List<string>();foreach(string id in (f==null?d.skills:f.skills)??new string[0]){string name=CreatureAbilities.Get(id).name;if(!skills.Contains(name))skills.Add(name);}
            string skillText=skills.Count==0?"暂无额外技能":string.Join(" · ",skills.ToArray());
            string equipped="";IEnumerable<string> gear=f!=null?(IEnumerable<string>)f.equipment:inspectionUnit==null?null:inspectionUnit.equipment;
            if(gear!=null)foreach(string id in gear){if(equipped.Length>0)equipped+=" · ";equipped+=TradeCatalog.Get(id).name;}
            string details=d.description+"\n\n技能："+skillText+(string.IsNullOrEmpty(equipped)?"":"\n装备："+equipped);
            var states=new List<KeyValuePair<string,string>>();
            if(f!=null){if(f.shield>0)states.Add(new KeyValuePair<string,string>("shield","护盾 "+Mathf.CeilToInt(f.shield)));if(f.poisonTime>0)states.Add(new KeyValuePair<string,string>("poison","中毒"));if(f.burnTime>0)states.Add(new KeyValuePair<string,string>("fire","燃烧"));if(f.slowTime>0)states.Add(new KeyValuePair<string,string>("frost","减速"));if(f.stunTime>0)states.Add(new KeyValuePair<string,string>("stun","眩晕"));}
            float stateWidth=0,stateHeight=28;
            foreach(var state in states){float w=small.CalcSize(new GUIContent(state.Value)).x+34;if(stateWidth+w>358){stateHeight+=30;stateWidth=0;}stateWidth+=w+6;}
            float detailHeight=small.CalcHeight(new GUIContent(details),354),detailsY=205+stateHeight+10;Rect r=TooltipRect(398,detailsY+detailHeight+18);
            Panel(r,new Color(.07f,.11f,.12f,.99f));
            ArenaIcons.Creature(new Rect(r.x+17,r.y+14,58,58),d.model??d.id,MobColor(d));
            Text(new Rect(r.x+89,r.y+13,291,36),(f!=null&&f.scale<.9f?"小":"")+d.name,heading,white);
            Text(new Rect(r.x+89,r.y+52,291,26),(inspectionOwner==null?"":Trim(inspectionOwner.name,8)+" · ")+d.role,small,inspectionOwner==null?muted:Team(inspectionOwner.slot));
            Fill(new Rect(r.x+20,r.y+92,358,18),new Color(.055f,.07f,.07f));
            Fill(new Rect(r.x+22,r.y+94,354*Mathf.Clamp01(hp/Mathf.Max(1,maxHp)),14),new Color(.29f,.75f,.45f));
            Text(new Rect(r.x+20,r.y+88,358,25),Mathf.CeilToInt(hp)+" / "+Mathf.CeilToInt(maxHp),centered,white);
            Stat(new Rect(r.x+20,r.y+127,172,29),"sword",(f==null?d.attack:f.attack).ToString("0.#"));
            Stat(new Rect(r.x+208,r.y+127,172,29),"speed",(f==null?d.attackSpeed:f.attackSpeed).ToString("0.##")+" 次/秒");
            Stat(new Rect(r.x+20,r.y+164,172,29),"range",(f==null?d.range:f.range).ToString("0.#"));
            Stat(new Rect(r.x+208,r.y+164,172,29),"shield",((f==null?0:f.armor)*100).ToString("0.#")+"% 护甲");
            float sx=0,sy=0;
            foreach(var state in states){float w=small.CalcSize(new GUIContent(state.Value)).x+34;if(sx+w>358){sy+=30;sx=0;}ArenaIcons.Draw(new Rect(r.x+20+sx,r.y+205+sy,22,22),state.Key);Text(new Rect(r.x+47+sx,r.y+204+sy,w-26,27),state.Value,small,gold);sx+=w+6;}
            if(states.Count==0){ArenaIcons.Draw(new Rect(r.x+20,r.y+204,24,24),"ready");Text(new Rect(r.x+52,r.y+204,319,27),"状态良好",small,mint);}
            Text(new Rect(r.x+20,r.y+detailsY,354,detailHeight+3),details,small,muted);
        }
        Rect TooltipRect(float w,float h)
        {
            Vector2 p=new Vector2(hoverScreenPosition.x/scale,(Screen.height-hoverScreenPosition.y)/scale);float x=p.x+22,y=p.y+20;
            if(x+w>width-18)x=p.x-w-18;if(y+h>height-55)y=p.y-h-12;
            return new Rect(Mathf.Clamp(x,18,Mathf.Max(18,width-w-18)),Mathf.Clamp(y,98,Mathf.Max(98,height-h-52)),w,h);
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
        bool Button(Rect r,string text,Color background,Color? foreground=null)
        {
            bool hovered=r.Contains(Mouse());Color fill=GUI.enabled?(hovered?Color.Lerp(background,Color.white,.12f):background):Color.Lerp(background,new Color(.11f,.14f,.14f),.7f);
            Fill(r,new Color(.025f,.03f,.03f));Fill(new Rect(r.x+1,r.y+1,r.width-2,r.height-2),copper);Fill(new Rect(r.x+3,r.y+3,r.width-6,r.height-6),fill);
            Fill(new Rect(r.x+4,r.y+4,r.width-8,2),Color.Lerp(fill,Color.white,.25f));Fill(new Rect(r.x+3,r.yMax-5,r.width-6,2),Color.Lerp(fill,Color.black,.3f));
            int oldSize=button.fontSize;button.fontSize=r.height<32?15:19;
            Color color=foreground??(background.g>.45f?new Color(.035f,.1f,.08f):white);
            button.normal.textColor=button.hover.textColor=button.active.textColor=color;
            bool pressed=GUI.Button(r,text,button);button.fontSize=oldSize;return pressed;
        }
        bool IconButton(Rect r,string icon,string text,Color background)
        {
            bool pressed=Button(r,"",background);float size=Mathf.Min(28,r.height-12);
            ArenaIcons.Draw(new Rect(string.IsNullOrEmpty(text)?r.center.x-size/2:r.x+11,r.y+(r.height-size)/2,size,size),icon);
            if(!string.IsNullOrEmpty(text))
            {
                int old=centered.fontSize;centered.fontSize=r.width<225?17:19;
                Text(new Rect(r.x+47,r.y+2,r.width-56,r.height-4),text,centered,background.g>.45f?new Color(.035f,.1f,.08f):white);centered.fontSize=old;
            }
            return pressed;
        }
        void Stat(Rect r,string icon,string text)
        {
            ArenaIcons.Draw(new Rect(r.x,r.y,25,25),icon);Text(new Rect(r.x+33,r.y-1,Mathf.Max(30,r.width-31),r.height),text,label,white);
        }
        Vector2 Mouse(){return new Vector2(Input.mousePosition.x/scale,(Screen.height-Input.mousePosition.y)/scale);}
        UnitState SelectedUnit(){return app.Me==null||string.IsNullOrEmpty(app.SelectedId)?null:app.Me.units.Find(u=>u.id==app.SelectedId);}
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
            Color old=GUI.backgroundColor;GUI.backgroundColor=new Color(.12f,.2f,.18f);
            GUI.SetNextControlName(name);string result=GUI.TextField(r,value??"",limit,field);GUI.backgroundColor=old;return result;
        }
    }
}
