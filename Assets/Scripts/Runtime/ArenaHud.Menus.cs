using System;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        string frontPage="main";
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
        float FrontLeft { get { return Mathf.Max(64,width*.055f); } }
        Rect MainMenuRect { get { return new Rect(FrontLeft,Mathf.Max(100,height*.19f),410,510); } }
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
        Rect GameMenuPanelRect { get { return new Rect((width-416)/2,(height-528)/2,416,528); } }
        Rect MainMenuButtonRect(int index){Rect r=MainMenuRect;if(index<3)return new Rect(r.x,r.y+143+index*66,378,index==0?56:52);if(index<5)return new Rect(r.x+(index-3)*195,r.y+357,183,46);return new Rect(r.x,r.y+432,378,40);}
        Rect GameMenuActionRect(int index){Rect r=GameMenuPanelRect;return new Rect(r.x+25,r.y+94+index*58+(index>3?17:0),r.width-50,49);}
        internal Rect MainMenuScreenRect { get { Metrics();return ToScreenRect(MainMenuRect); } }
        internal Rect FrontPanelScreenRect { get { Metrics();return ToScreenRect(frontPage=="main"?MainMenuRect:FrontConfigRect); } }
        internal Rect FrontConfigScreenRect { get { Metrics();return ToScreenRect(FrontConfigRect); } }
        internal Rect FrontBackScreenRect { get { Metrics();return ToScreenRect(FrontBackRect); } }
        internal Rect FrontPrimaryScreenRect { get { Metrics();return ToScreenRect(FrontPrimaryRect); } }
        internal Rect FrontCancelScreenRect { get { Metrics();return ToScreenRect(FrontCancelRect); } }
        internal Rect FrontNameScreenRect { get { Metrics();return ToScreenRect(FrontNameRect); } }
        internal Rect FrontAddressScreenRect { get { Metrics();return ToScreenRect(FrontAddressRect); } }
        internal Rect FrontPortScreenRect { get { Metrics();return ToScreenRect(FrontPortRect); } }
        internal Rect MainMenuButtonScreenRect(int index){Metrics();return ToScreenRect(MainMenuButtonRect(Mathf.Clamp(index,0,5)));}
        internal Rect RoomPanelScreenRect { get { Metrics();return ToScreenRect(RoomPanelRect); } }
        internal Rect RoomStartScreenRect { get { Metrics();return ToScreenRect(RoomStartRect); } }
        internal Rect RoomCopyScreenRect { get { Metrics();return ToScreenRect(RoomCopyRect); } }
        internal Rect RoomLeaveScreenRect { get { Metrics();return ToScreenRect(RoomLeaveRect); } }
        internal Rect GameMenuScreenRect { get { Metrics();return ToScreenRect(GameMenuPanelRect); } }
        internal Rect GameMenuActionScreenRect(int index){Metrics();return ToScreenRect(GameMenuActionRect(Mathf.Clamp(index,0,5)));}
        internal Rect FrontActionScreenRect(string action)
        {
            Metrics();if(action=="back")return ToScreenRect(FrontBackRect);if(action=="primary")return ToScreenRect(FrontPrimaryRect);if(action=="cancel")return ToScreenRect(FrontCancelRect);
            if(action!="single"&&action!="host"&&action!="join"&&action!="bestiary"&&action!="settings"&&action!="exit")return Rect.zero;
            int index=action=="single"?0:action=="host"?1:action=="join"?2:action=="bestiary"?3:action=="settings"?4:5;
            return ToScreenRect(MainMenuButtonRect(index));
        }
        internal Rect GameMenuActionScreenRect(string action)
        {if(action!="continue"&&action!="bestiary"&&action!="settings"&&action!="room"&&action!="main"&&action!="exit")return Rect.zero;int index=action=="continue"?0:action=="bestiary"?1:action=="settings"?2:action=="room"?3:action=="main"?4:5;return GameMenuActionScreenRect(index);}

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
            if(MenuActionButton(MainMenuButtonRect(5),"exit","退出游戏",new Color(.055f,.10f,.12f),true))Application.Quit();
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
            if(MenuActionButton(GameMenuActionRect(3),"room","房间",new Color(.08f,.16f,.18f)))app.ShowRoom=true;
            if(MenuActionButton(GameMenuActionRect(4),"back","返回主菜单",new Color(.16f,.12f,.105f)))app.Leave();
            if(MenuActionButton(GameMenuActionRect(5),"exit","退出游戏",new Color(.08f,.12f,.13f)))Application.Quit();
            int footerSize=centered.fontSize;centered.fontSize=16;
            Text(new Rect(r.x+26,r.yMax-39,r.width-52,26),app.Hosting?"离开对局会关闭本机房间":"联机对局继续进行",centered,muted);centered.fontSize=footerSize;
        }
    }
}
