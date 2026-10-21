using System;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        string frontPage="main";
        int helpTab;
        float copiedUntil;
        bool resetMenuFocus;
        GUIStyle menuLogo;
        public string FrontPage { get { return frontPage; } set { SetFrontPage(value); } }
        public string MenuBackgroundMode { get {
            if(app.QuickStarting)return "main";
            if(ShowSettings)return "settings";
            if(app.State!=null)return app.State.phase=="lobby"?(app.Hosting?"lobby_host":"lobby_guest"):null;
            return frontPage;
        } }
        public void SetFrontPage(string page)
        { frontPage=page=="host"||page=="join"?page:"main";ClearHover();resetMenuFocus=true; }
        public bool BackFrontPage()
        { if(frontPage=="main")return false;if(app.HasSession)app.Leave();SetFrontPage("main");return true; }
        public void SetHelpTab(int tab){helpTab=Mathf.Clamp(tab,0,3);ClearHover();}
        float FrontLeft { get { return Mathf.Max(64,width*.055f); } }
        Rect MainMenuRect { get { return new Rect(FrontLeft,Mathf.Max(100,height*.19f),410,566); } }
        Rect FrontConfigRect { get { return new Rect(FrontLeft,Mathf.Max(88,(height-592)/2),470,592); } }
        Rect FrontBackRect { get { Rect r=FrontConfigRect;return new Rect(r.x+22,r.y+21,38,38); } }
        Rect FrontNameRect { get { Rect r=FrontConfigRect;return new Rect(r.x+28,r.y+132,r.width-56,52); } }
        Rect FrontAddressRect { get { Rect r=FrontConfigRect;return new Rect(r.x+28,r.y+249,278,52); } }
        Rect FrontPortRect { get { Rect r=FrontConfigRect;return new Rect(r.x+(frontPage=="join"?320:28),r.y+249,frontPage=="join"?122:r.width-56,52); } }
        Rect FrontPrimaryRect { get { Rect r=FrontConfigRect;return new Rect(r.x+28,r.yMax-106,r.width-56,54); } }
        Rect FrontCancelRect { get { Rect r=FrontConfigRect;return new Rect(r.x+28,r.yMax-44,r.width-56,31); } }
        Rect RoomPanelRect { get { return new Rect(app.State!=null&&app.State.phase=="lobby"?FrontLeft:(width-640)/2,Mathf.Max(88,(height-660)/2),640,660); } }
        Rect RoomStartRect { get { Rect r=RoomPanelRect;return new Rect(r.x+26,r.y+538,r.width-52,52); } }
        Rect RoomLeaveRect { get { Rect r=RoomPanelRect;return new Rect(r.x+26,r.y+607,214,30); } }
        Rect RoomCopyRect { get { Rect r=RoomPanelRect;return new Rect(r.xMax-95,r.y+104,67,34); } }
        Rect LobbyCommanderRect { get { Rect r=RoomPanelRect;return new Rect(r.x+26,r.y+458,282,44); } }
        Rect LobbyWorldToggleRect { get { Rect r=RoomPanelRect;return new Rect(r.x+326,r.y+458,288,44); } }
        Rect LobbyWorldPanelRect { get { Rect r=RoomPanelRect;return new Rect(r.xMax+16,Mathf.Clamp(r.y+197,84,height-350),326,247); } }
        Rect HelpPanelRect { get { return new Rect((width-1040)/2,(height-666)/2,1040,666); } }
        Rect GameMenuPanelRect { get { return new Rect((width-416)/2,(height-586)/2,416,586); } }
        Rect MainMenuButtonRect(int index){Rect r=MainMenuRect;if(index<3)return new Rect(r.x,r.y+143+index*66,378,index==0?56:52);if(index<6)return new Rect(r.x+(index-3)*130,r.y+357,118,46);return new Rect(r.x,r.y+432,378,40);}
        Rect GameMenuActionRect(int index){Rect r=GameMenuPanelRect;return new Rect(r.x+25,r.y+94+index*58+(index>4?17:0),r.width-50,49);}
        Rect HelpTabRect(int index){Rect r=HelpPanelRect;return new Rect(r.x+24,r.y+101+index*71,196,57);}
        internal Rect MainMenuScreenRect { get { Metrics();return ToScreenRect(MainMenuRect); } }
        internal Rect FrontPanelScreenRect { get { Metrics();return ToScreenRect(frontPage=="main"?MainMenuRect:FrontConfigRect); } }
        internal Rect FrontConfigScreenRect { get { Metrics();return ToScreenRect(FrontConfigRect); } }
        internal Rect FrontBackScreenRect { get { Metrics();return ToScreenRect(FrontBackRect); } }
        internal Rect FrontPrimaryScreenRect { get { Metrics();return ToScreenRect(FrontPrimaryRect); } }
        internal Rect FrontCancelScreenRect { get { Metrics();return ToScreenRect(FrontCancelRect); } }
        internal Rect FrontNameScreenRect { get { Metrics();return ToScreenRect(FrontNameRect); } }
        internal Rect FrontAddressScreenRect { get { Metrics();return ToScreenRect(FrontAddressRect); } }
        internal Rect FrontPortScreenRect { get { Metrics();return ToScreenRect(FrontPortRect); } }
        internal Rect MainMenuButtonScreenRect(int index){Metrics();return ToScreenRect(MainMenuButtonRect(Mathf.Clamp(index,0,6)));}
        internal Rect RoomPanelScreenRect { get { Metrics();return ToScreenRect(RoomPanelRect); } }
        internal Rect RoomStartScreenRect { get { Metrics();return ToScreenRect(RoomStartRect); } }
        internal Rect RoomCopyScreenRect { get { Metrics();return ToScreenRect(RoomCopyRect); } }
        internal Rect RoomLeaveScreenRect { get { Metrics();return ToScreenRect(RoomLeaveRect); } }
        internal Rect HelpScreenRect { get { Metrics();return ToScreenRect(HelpPanelRect); } }
        internal Rect HelpPanelScreenRect { get { return HelpScreenRect; } }
        internal Rect HelpCloseScreenRect { get { Metrics();Rect r=HelpPanelRect;return ToScreenRect(new Rect(r.xMax-63,r.y+20,39,37)); } }
        internal Rect HelpTabScreenRect(int index){Metrics();return ToScreenRect(HelpTabRect(Mathf.Clamp(index,0,3)));}
        internal Rect GameMenuScreenRect { get { Metrics();return ToScreenRect(GameMenuPanelRect); } }
        internal Rect GameMenuActionScreenRect(int index){Metrics();return ToScreenRect(GameMenuActionRect(Mathf.Clamp(index,0,6)));}
        internal Rect FrontActionScreenRect(string action)
        {
            Metrics();if(action=="back")return ToScreenRect(FrontBackRect);if(action=="primary")return ToScreenRect(FrontPrimaryRect);if(action=="cancel")return ToScreenRect(FrontCancelRect);
            int index=action=="single"?0:action=="host"?1:action=="join"?2:action=="bestiary"?3:action=="settings"?4:action=="help"?5:6;
            return ToScreenRect(MainMenuButtonRect(index));
        }
        internal Rect GameMenuActionScreenRect(string action)
        {int index=action=="continue"?0:action=="bestiary"?1:action=="settings"?2:action=="help"?3:action=="room"?4:action=="main"?5:6;return GameMenuActionScreenRect(index);}

        void FrontVeil()
        {
            float reach=width*.61f;
            for(int i=0;i<30;i++){float t=(float)i/29;Fill(new Rect(reach*i/30,0,reach/30+1,height),new Color(.012f,.035f,.04f,.73f*(1-t)*(1-t)));}
            Fill(new Rect(FrontLeft-25,MainMenuRect.y-26,2,57),new Color(gold.r,gold.g,gold.b,.7f));
        }
        void Menu()
        {
            FrontVeil();
            if(frontPage!="main"&&!app.QuickStarting){FrontConfiguration();return;}
            Rect r=MainMenuRect;
            if(menuLogo==null)menuLogo=new GUIStyle(title){fontSize=64};
            Text(new Rect(r.x,r.y-29,400,28),"BLOCK ARENA",small,gold);
            Text(new Rect(r.x-3,r.y+12,520,88),"方块斗兽场",menuLogo,white);
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!app.QuickStarting;
            if(MenuActionButton(MainMenuButtonRect(0),"single",app.QuickStarting?"进入战场…":"单人试玩",mint,true)&&!app.QuickStarting)app.Host(true);
            if(MenuActionButton(MainMenuButtonRect(1),"host","创建房间",new Color(.10f,.20f,.22f),true))SetFrontPage("host");
            if(MenuActionButton(MainMenuButtonRect(2),"join","加入房间",new Color(.10f,.20f,.22f),true))SetFrontPage("join");
            if(MenuActionButton(MainMenuButtonRect(3),"book","图鉴",new Color(.075f,.15f,.17f)))app.ShowBestiary=true;
            if(MenuActionButton(MainMenuButtonRect(4),"settings","设置",new Color(.075f,.15f,.17f)))ShowSettings=true;
            if(MenuActionButton(MainMenuButtonRect(5),"help","帮助",new Color(.075f,.15f,.17f)))app.ShowHelp=true;
            if(MenuActionButton(MainMenuButtonRect(6),"exit","退出游戏",new Color(.055f,.10f,.12f),true))Application.Quit();
            GUI.enabled=enabled;
        }
        void FrontConfiguration()
        {
            Rect r=FrontConfigRect;bool host=frontPage=="host",waiting=app.ConnectionPending;
            Panel(r,new Color(.025f,.065f,.08f,.95f));
            if(Button(FrontBackRect,"‹",new Color(.07f,.14f,.16f)))BackFrontPage();
            Text(new Rect(r.x+77,r.y+24,r.width-120,38),host?"创建房间":"加入房间",heading,white);
            Text(new Rect(r.x+29,r.y+102,370,26),"指挥官昵称",small,muted);
            bool before=GUI.enabled;GUI.enabled=before&&!waiting;
            app.PlayerName=InputBox(FrontNameRect,"front-name",app.PlayerName,16);
            if(!host){Text(new Rect(r.x+29,r.y+218,278,26),"房主 IP / 域名",small,muted);app.Address=InputBox(FrontAddressRect,"front-address",app.Address,160);}
            Text(new Rect(FrontPortRect.x,r.y+218,FrontPortRect.width,26),"TCP 端口",small,muted);
            app.PortText=InputBox(FrontPortRect,"front-port",app.PortText,5);
            GUI.enabled=before;
            Rect status=new Rect(r.x+28,r.y+337,r.width-56,96);RoundFill(status,new Color(.018f,.045f,.055f,.72f),8);
            ArenaIcons.Draw(new Rect(status.x+14,status.y+16,23,23),waiting?"speed":host?"ready":"range");
            string note=string.IsNullOrEmpty(app.Status)?host?"在本机创建一个四人房间。":"填写房主地址，或 TCP 穿透域名。":Compact(app.Status,110);
            Text(new Rect(status.x+50,status.y+14,status.width-67,72),note,small,waiting?mint:muted);
            GUI.enabled=before&&!waiting;
            if(MenuActionButton(FrontPrimaryRect,host?"host":"join",waiting?"连接中…":host?"创建房间":"加入房间",mint,true)){if(host)app.Host();else app.Join();}
            GUI.enabled=before;
            if(app.HasSession&&Button(FrontCancelRect,"取消连接",new Color(.08f,.14f,.15f),muted))app.CancelConnection();
        }
        void Room()
        {
            if(app.State==null)return;
            bool lobby=app.State.phase=="lobby",host=app.State.hostId==app.PlayerId;
            if(!lobby)Shade();else FrontVeil();
            Rect r=RoomPanelRect;Panel(r,new Color(.025f,.065f,.08f,.95f));
            Text(new Rect(r.x+27,r.y+24,470,41),app.Hosting?"我的房间":"已加入房间",heading,white);
            if((!lobby||app.ShowGameMenu)&&IconButton(new Rect(r.xMax-62,r.y+22,37,35),"close","",new Color(.08f,.15f,.17f)))app.ShowRoom=false;
            Rect address=new Rect(r.x+26,r.y+88,r.width-52,68);RoundFill(address,new Color(.017f,.045f,.055f),8);
            Text(new Rect(address.x+15,address.y+9,address.width-99,23),app.Hosting?"房间地址":"连接地址",micro,muted);
            string endpoint=app.Hosting?HostEndpoint():app.Address.Trim()+":"+app.PortText;
            Text(new Rect(address.x+15,address.y+33,address.width-100,27),Compact(endpoint,46),small,mint);
            if(Button(RoomCopyRect,Time.unscaledTime<copiedUntil?"已复制":"复制",new Color(.09f,.19f,.20f))){GUIUtility.systemCopyBuffer=endpoint;copiedUntil=Time.unscaledTime+1.5f;}
            int count=app.State.players.Count;Text(new Rect(r.x+28,r.y+174,360,26),"指挥官  "+count+" / 4",small,muted);
            for(int i=0;i<4;i++){
                var p=app.State.players.Find(x=>x.slot==i);Rect seat=new Rect(r.x+26+(i%2)*300,r.y+211+(i/2)*109,288,94);
                RoundFill(seat,new Color(.05f,.115f,.13f),9);Color team=Team(i);
                RoundFill(new Rect(seat.x+13,seat.y+17,5,seat.height-34),p==null?new Color(.18f,.27f,.28f):team,2);
                ArenaIcons.Draw(new Rect(seat.x+30,seat.y+23,32,32),p==null?"range":"ready");
                Text(new Rect(seat.x+75,seat.y+15,seat.width-89,32),p==null?"等待加入":Trim(p.name,9),label,p==null?muted:white);
                Text(new Rect(seat.x+76,seat.y+51,seat.width-91,25),p==null?"空位":p.bot?"电脑":p.id==app.State.hostId?"房主":p.id==app.PlayerId?"你":"玩家",micro,p==null?muted:team);
            }
            if(lobby){DrawCommanderSelection();DrawWorldOptions();}
            else Text(new Rect(r.x+29,r.y+463,r.width-58,31),"第 "+app.State.round+" 轮 · "+PhaseName(app.State.phase),small,muted);
            bool before=GUI.enabled;GUI.enabled=before&&(lobby?app.Connected&&host:true);
            if(IconButton(RoomStartRect,lobby?"ready":"close",lobby?(host?"开始对战":"等待房主开始"):ShowSettings?"返回设置":app.ShowGameMenu?"返回菜单":"返回对局",lobby?mint:new Color(.12f,.24f,.25f))){if(lobby)app.Send(new GameCommand{type="start",bots=true});else app.ShowRoom=false;}
            GUI.enabled=before;
            if(Button(RoomLeaveRect,"离开房间",new Color(.12f,.105f,.10f),muted))app.Leave();
            if(!app.Connected&&Button(new Rect(r.xMax-243,r.y+607,216,30),"重新连接",new Color(.10f,.23f,.21f)))app.Reconnect();
            else if(lobby)Text(new Rect(r.x+302,r.y+610,310,27),host?"空位将由电脑补齐":"等待房主开始",micro,muted);
        }
        string HostEndpoint()
        {
            string source=app.LocalAddresses??"";string[] values=source.Split(new[]{',',';','\n','/'},StringSplitOptions.RemoveEmptyEntries);
            foreach(string value in values){System.Net.IPAddress address;if(System.Net.IPAddress.TryParse(value.Trim(),out address)&&address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)return address+":"+app.PortText;}
            return "127.0.0.1:"+app.PortText;
        }
        void GameMenu()
        {
            Shade();Rect r=GameMenuPanelRect;Panel(r,surface);
            if(menuCenteredLabel==null)menuCenteredLabel=new GUIStyle(label){alignment=TextAnchor.MiddleCenter,wordWrap=false};
            int previousSize=menuCenteredLabel.fontSize;menuCenteredLabel.fontSize=28;menuCenteredLabel.fontStyle=FontStyle.Bold;
            Text(new Rect(r.x+26,r.y+25,r.width-52,42),"游戏菜单",menuCenteredLabel,white);
            menuCenteredLabel.fontSize=previousSize;menuCenteredLabel.fontStyle=FontStyle.Normal;
            Fill(new Rect(r.x+26,r.y+76,r.width-52,1),line);
            if(MenuActionButton(GameMenuActionRect(0),"single","继续游戏",mint))app.ShowGameMenu=false;
            if(MenuActionButton(GameMenuActionRect(1),"book","生物图鉴",new Color(.08f,.16f,.18f)))app.ShowBestiary=true;
            if(MenuActionButton(GameMenuActionRect(2),"settings","设置",new Color(.08f,.16f,.18f)))ShowSettings=true;
            if(MenuActionButton(GameMenuActionRect(3),"help","玩法帮助",new Color(.08f,.16f,.18f)))app.ShowHelp=true;
            if(MenuActionButton(GameMenuActionRect(4),"room","房间",new Color(.08f,.16f,.18f)))app.ShowRoom=true;
            if(MenuActionButton(GameMenuActionRect(5),"back","返回主菜单",new Color(.16f,.12f,.105f)))app.Leave();
            if(MenuActionButton(GameMenuActionRect(6),"exit","退出游戏",new Color(.08f,.12f,.13f)))Application.Quit();
            int footerSize=centered.fontSize;centered.fontSize=16;
            Text(new Rect(r.x+26,r.yMax-39,r.width-52,26),app.Hosting?"离开对局会关闭本机房间":"联机对局继续进行",centered,muted);centered.fontSize=footerSize;
        }
        void Help()
        {
            Shade();Rect r=HelpPanelRect;Panel(r,surface);
            ArenaIcons.Draw(new Rect(r.x+25,r.y+24,31,31),"book");Text(new Rect(r.x+72,r.y+21,780,43),"玩法手册",heading,white);
            if(IconButton(new Rect(r.xMax-63,r.y+20,39,37),"close","",new Color(.08f,.16f,.18f)))app.ShowHelp=false;
            Fill(new Rect(r.x+24,r.y+77,r.width-48,1),line);Fill(new Rect(r.x+243,r.y+102,1,r.height-132),line);
            string[] tabs={"基本操作","招募与金币","战术与指挥","联机"};string[] icons={"range","coin","sword","speed"};
            for(int i=0;i<4;i++)if(IconButton(HelpTabRect(i),icons[i],tabs[i],i==helpTab?new Color(.13f,.30f,.26f):new Color(.055f,.12f,.14f)))SetHelpTab(i);
            float x=r.x+270,y=r.y+104;
            Text(new Rect(x,y,705,36),tabs[helpTab],heading,gold);
            if(helpTab==0){
                HelpKey(new Rect(x,y+66,334,103),"W A S D","移动指挥官","点击地面也可自动前往。","speed");
                HelpKey(new Rect(x+358,y+66,334,103),"左键拖动","调整生物位置","准备与战术阶段自由摆位。","range");
                HelpKey(new Rect(x,y+191,334,103),"Shift + 左键","选择多个生物","拖动框选；Ctrl + A 全选。","ready");
                HelpKey(new Rect(x+358,y+191,334,103),"右键","布置路线","选中生物后，在场地布点。","range");
                HelpKey(new Rect(x,y+316,334,103),"滚轮 / 中键","镜头缩放与拖动","F 恢复默认视角。","range");
                HelpKey(new Rect(x+358,y+316,334,103),"B / M / Esc","图鉴 · 商店 · 菜单","悬停查看属性，单击固定。","book");
            }else if(helpTab==1){
                HelpKey(new Rect(x,y+66,334,124),"20 生命 / 12 金币","每轮准备 45 秒","招募、摆位与强化共用倒计时。","heart");
                HelpKey(new Rect(x+358,y+66,334,124),"三选一","招募一只生物","花金币招募；也可跳过。","bag");
                HelpKey(new Rect(x,y+210,334,124),"2 金币","更换招募候选","技能商店也可手动刷新。","refresh");
                HelpKey(new Rect(x+358,y+210,334,124),"返还 50%","遣散已有生物","点击己方生物，在属性框操作。","coin");
                HelpNote(new Rect(x,y+357,692,127),"每轮收入","6 基础 + 最多 5 击杀 + 名次 4 / 3 / 2 / 2。\n击杀领先者每 3 只额外 +1，每轮最多 +3。","coin");
            }else if(helpTab==2){
                HelpKey(new Rect(x,y+66,334,124),"120 秒","战术编排","每组选中生物保存独立路线。","range");
                HelpKey(new Rect(x+358,y+66,334,124),"移动 · 等待 · 伏击","安排节点与条件","路线结束后继续自动索敌。","book");
                HelpKey(new Rect(x,y+210,334,124),"Q → Q / 左键","瞄准并施放指挥技","右键或 Esc 取消瞄准。","sword");
                HelpKey(new Rect(x+358,y+210,334,124),"每场 2 次","把握关键时机","指挥技间隔 8 秒。","shield");
                HelpNote(new Rect(x,y+357,692,127),"地图与天气","准备结束后揭晓地图，再抽取天气。\n在顶部环境条查看本轮地形与天气效果。","book");
            }else{
                HelpKey(new Rect(x,y+66,334,124),"创建房间","房主先开服","朋友使用房主地址和 TCP 端口。","ready");
                HelpKey(new Rect(x+358,y+66,334,124),"加入房间","输入 IP 或穿透域名","同一局域网可直接填内网地址。","speed");
                HelpKey(new Rect(x,y+210,334,124),"默认 TCP 7777","跨网络使用 TCP 穿透","映射到房主同一端口。","range");
                HelpKey(new Rect(x+358,y+210,334,124),"返回大厅","结束后重新集结","所有玩家使用相同游戏版本。","bag");
                HelpNote(new Rect(x,y+357,692,127),"连接检查","核对服务器地址、端口与防火墙。\n地址无需 http://；仅 HTTP 的网页隧道不能使用。","settings");
            }
        }
        void HelpKey(Rect r,string key,string name,string detail,string icon)
        {
            RoundFill(r,new Color(.055f,.12f,.14f),9);ArenaIcons.Draw(new Rect(r.x+15,r.y+17,25,25),icon);
            Text(new Rect(r.x+52,r.y+12,r.width-66,31),key,label,mint);
            Text(new Rect(r.x+16,r.y+48,r.width-32,29),name,small,white);
            Text(new Rect(r.x+16,r.y+80,r.width-32,35),detail,micro,muted);
        }
        void HelpNote(Rect r,string name,string detail,string icon)
        {
            RoundFill(r,new Color(.05f,.105f,.12f),9);ArenaIcons.Draw(new Rect(r.x+17,r.y+19,26,26),icon);
            Text(new Rect(r.x+57,r.y+16,r.width-78,31),name,label,gold);
            Text(new Rect(r.x+20,r.y+57,r.width-40,r.height-68),ArenaDescription.Format(detail),small,white);
        }
    }
}
