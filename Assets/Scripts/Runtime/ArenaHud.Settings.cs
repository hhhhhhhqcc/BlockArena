using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        bool showSettings, resetDisplayDraft;
        int settingsTab;
        GUIStyle navigationLabel, menuCenteredLabel;
        public bool ShowAudio {
            get { return showSettings; }
            set {
                if(showSettings==value)return;
                showSettings=value;audioPointerOwned=false;audioSlider=-1;bindingAction=null;bindingFeedback="";ClearHover();resetMenuFocus=true;
                if(value){
                    settingsTab=0;resetDisplayDraft=true;
                    marketPointerOwned=marketDragging=false;skillPointerOwned=false;
                    CloseSkillDetails();ShowOwnedSkills=false;
                }
            }
        }
        public bool ShowSettings { get { return ShowAudio; } set { ShowAudio=value; } }
        public bool SettingsVisible { get { return ShowSettings; } }
        bool SettingsControlsAvailable { get { return ShowSettings&&!app.ShowBestiary&&!(app.State!=null&&app.ShowRoom); } }
        Rect SettingsPanelRect { get { return new Rect(FrontLeft,Mathf.Max(72,(height-710)/2),1040,710); } }
        Rect SettingsCloseRect { get { Rect r=SettingsPanelRect;return new Rect(r.x+25,r.y+25,42,42); } }
        Rect SettingsContentRect { get { Rect r=SettingsPanelRect;return new Rect(r.x+263,r.y+112,r.width-291,r.height-147); } }
        Rect SettingsSoundRect { get { Rect r=SettingsContentRect;return new Rect(r.x,r.y,r.width,259); } }
        Rect SettingsAssistRect { get { Rect r=SettingsContentRect;return new Rect(r.x,r.y+282,r.width,217); } }
        Rect AudioMusicSliderRect { get { Rect r=SettingsSoundRect;return new Rect(r.x+25,r.y+111,r.width-50,30); } }
        Rect AudioSfxSliderRect { get { Rect r=SettingsSoundRect;return new Rect(r.x+25,r.y+198,r.width-50,30); } }
        Rect SettingsMuteRect { get { Rect r=SettingsSoundRect;return new Rect(r.xMax-141,r.y+17,116,36); } }
        Rect CommanderRangeToggleRect { get { Rect r=SettingsAssistRect;return new Rect(r.x+22,r.y+68,r.width-44,51); } }
        Rect CreatureRangeToggleRect { get { Rect r=SettingsAssistRect;return new Rect(r.x+22,r.y+135,r.width-44,51); } }
        Rect SettingsTabRect(int index){Rect r=SettingsPanelRect;return new Rect(r.x+25,r.y+119+index*66,207,53);}
        Rect SettingsNavigationRect(int index){Rect r=SettingsPanelRect;return new Rect(r.x+25,r.y+376+index*61,207,48);}
        Rect SettingsWindowRect { get { Rect r=SettingsContentRect;return new Rect(r.x+25,r.y+79,(r.width-62)/2,61); } }
        Rect SettingsFullscreenRect { get { Rect r=SettingsWindowRect;return new Rect(r.xMax+12,r.y,r.width,r.height); } }
        Rect SettingsResolutionRect { get { Rect r=SettingsContentRect;return new Rect(r.x+84,r.y+243,r.width-168,63); } }
        Rect SettingsResolutionPrevRect { get { Rect r=SettingsResolutionRect;return new Rect(r.x-59,r.y,47,r.height); } }
        Rect SettingsResolutionNextRect { get { Rect r=SettingsResolutionRect;return new Rect(r.xMax+12,r.y,47,r.height); } }
        Rect SettingsApplyRect { get { Rect r=SettingsContentRect;return new Rect(r.xMax-217,r.y+466,192,53); } }
        Rect SettingsResetRect { get { Rect r=SettingsContentRect;return new Rect(r.x+25,r.y+472,161,42); } }
        internal Rect SettingsPanelScreenRect { get { Metrics();return ToScreenRect(SettingsPanelRect); } }
        internal Rect SettingsScreenRect { get { return SettingsPanelScreenRect; } }
        internal Rect AudioScreenRect { get { return SettingsPanelScreenRect; } }
        internal Rect SettingsCloseScreenRect { get { Metrics();return ToScreenRect(SettingsCloseRect); } }
        internal Rect SettingsToggleScreenRect { get { return ShowSettings?SettingsCloseScreenRect:Rect.zero; } }
        internal Rect SettingsMusicSliderScreenRect { get { Metrics();return ToScreenRect(AudioMusicSliderRect); } }
        internal Rect SettingsSfxSliderScreenRect { get { Metrics();return ToScreenRect(AudioSfxSliderRect); } }
        internal Rect SettingsMuteScreenRect { get { Metrics();return ToScreenRect(SettingsMuteRect); } }
        internal Rect CommanderRangeToggleScreenRect { get { Metrics();return ToScreenRect(CommanderRangeToggleRect); } }
        internal Rect CreatureRangeToggleScreenRect { get { Metrics();return ToScreenRect(CreatureRangeToggleRect); } }
        internal Rect SettingsModeScreenRect { get { Metrics();return ToScreenRect(SettingsFullscreenRect); } }
        internal Rect SettingsWindowScreenRect { get { Metrics();return ToScreenRect(SettingsWindowRect); } }
        internal Rect SettingsFullscreenScreenRect { get { Metrics();return ToScreenRect(SettingsFullscreenRect); } }
        internal Rect SettingsResolutionScreenRect { get { Metrics();return ToScreenRect(SettingsResolutionRect); } }
        internal Rect SettingsResolutionPrevScreenRect { get { Metrics();return ToScreenRect(SettingsResolutionPrevRect); } }
        internal Rect SettingsResolutionNextScreenRect { get { Metrics();return ToScreenRect(SettingsResolutionNextRect); } }
        internal Rect SettingsApplyScreenRect { get { Metrics();return ToScreenRect(SettingsApplyRect); } }
        internal Rect SettingsResetScreenRect { get { Metrics();return ToScreenRect(SettingsResetRect); } }
        internal Rect SettingsTabScreenRect(int index){Metrics();return ToScreenRect(SettingsTabRect(Mathf.Clamp(index,0,2)));}
        internal Rect SettingsNavigationScreenRect(string action){Metrics();return action=="bestiary"||action=="room"?ToScreenRect(SettingsNavigationRect(action=="bestiary"?0:1)):Rect.zero;}
        internal Rect SettingsActionScreenRect(string action)
        {
            switch(action){
                case "close":return SettingsCloseScreenRect;
                case "windowed":return SettingsWindowScreenRect;
                case "fullscreen":return SettingsFullscreenScreenRect;
                case "resolution-prev":return SettingsResolutionPrevScreenRect;
                case "resolution-next":return SettingsResolutionNextScreenRect;
                case "apply":return SettingsApplyScreenRect;
                case "music":return SettingsMusicSliderScreenRect;
                case "effects":return SettingsSfxSliderScreenRect;
                case "mute":return SettingsMuteScreenRect;
                case "commander-range":return CommanderRangeToggleScreenRect;
                case "creature-range":return CreatureRangeToggleScreenRect;
                case "bestiary":case "room":return SettingsNavigationScreenRect(action);
                default:return Rect.zero;
            }
        }
        internal int SettingsTabForVerification { get { return settingsTab; } }
        public void SetSettingsTab(int tab){settingsTab=Mathf.Clamp(tab,0,2);bindingAction=null;bindingFeedback="";ClearHover();audioSlider=-1;}

        void DrawAudio()
        {
            if(!ShowSettings)return;
            if(resetDisplayDraft&&app.Display!=null){app.Display.ResetDraft();resetDisplayDraft=false;}
            bool previousDrawing=drawingAudio;drawingAudio=true;
            try {
                // This is an independent page. The right side intentionally exposes its workshop scene.
                FrontVeil();Rect r=SettingsPanelRect;Panel(r,new Color(.027f,.065f,.077f,.975f));
                if(MenuActionButton(SettingsCloseRect,"back","",new Color(.08f,.16f,.18f)))ShowSettings=false;
                Text(new Rect(r.x+87,r.y+22,700,42),"设置",heading,white);
                Text(new Rect(r.x+89,r.y+65,700,22),"BLOCK ARENA  /  OPTIONS",micro,gold);
                Fill(new Rect(r.x+25,r.y+96,r.width-50,1),line);
                Fill(new Rect(r.x+246,r.y+120,1,r.height-153),line);
                if(MenuActionButton(SettingsTabRect(0),"sound","声音与辅助",settingsTab==0?new Color(.14f,.30f,.27f):new Color(.055f,.12f,.14f),true))SetSettingsTab(0);
                if(MenuActionButton(SettingsTabRect(1),"display","显示设置",settingsTab==1?new Color(.14f,.30f,.27f):new Color(.055f,.12f,.14f),true))SetSettingsTab(1);
                if(MenuActionButton(SettingsTabRect(2),"keys","按键设置",settingsTab==2?new Color(.14f,.30f,.27f):new Color(.055f,.12f,.14f),true))SetSettingsTab(2);
                Text(new Rect(r.x+34,r.y+322,190,27),"探索",micro,muted);
                Fill(new Rect(r.x+34,r.y+353,184,1),line);
                if(MenuActionButton(SettingsNavigationRect(0),"book","生物图鉴",new Color(.055f,.12f,.14f),true))app.ShowBestiary=true;
                bool enabled=GUI.enabled;GUI.enabled=enabled&&app.State!=null&&!app.QuickStarting;
                if(MenuActionButton(SettingsNavigationRect(1),"room","房间",new Color(.055f,.12f,.14f),true))app.ShowRoom=true;
                GUI.enabled=enabled;
                Text(new Rect(r.x+34,r.yMax-59,190,29),"Esc  返回",micro,muted);
                if(settingsTab==0)DrawSoundAndAssistSettings();else if(settingsTab==1)DrawDisplaySettings();else DrawKeyBindingsSettings();
            } finally {drawingAudio=previousDrawing;}
        }
        void DrawSoundAndAssistSettings()
        {
            Rect r=SettingsSoundRect;SettingsCard(r,"sound","声音");
            bool silent=MusicVolume<=.001f&&SfxVolume<=.001f;
            if(Button(SettingsMuteRect,silent?"恢复声音":"静音",new Color(.10f,.20f,.22f))){
                if(silent)ChangeAudioVolumes(rememberedMusic,rememberedSfx);
                else{rememberedMusic=MusicVolume;rememberedSfx=SfxVolume;ChangeAudioVolumes(0,0);}
            }
            ArenaIcons.Draw(new Rect(r.x+26,r.y+77,23,23),"music");Text(new Rect(r.x+61,r.y+72,410,33),"背景音乐",label,white);
            Text(new Rect(r.xMax-87,r.y+76,65,30),Mathf.RoundToInt(MusicVolume*100)+"%",small,gold);
            DrawAudioSlider(AudioMusicSliderRect,MusicVolume,audioSlider==0);
            ArenaIcons.Draw(new Rect(r.x+26,r.y+164,23,23),"sound");Text(new Rect(r.x+61,r.y+159,410,33),"游戏音效",label,white);
            Text(new Rect(r.xMax-87,r.y+163,65,30),Mathf.RoundToInt(SfxVolume*100)+"%",small,gold);
            DrawAudioSlider(AudioSfxSliderRect,SfxVolume,audioSlider==1);
            SettingsCard(SettingsAssistRect,"range","辅助显示");
            if(SettingsRangeToggle(CommanderRangeToggleRect,"Steve 施法范围","range",ShowCommanderRange)){
                ShowCommanderRange=!ShowCommanderRange;if(CommanderRangeChanged!=null)CommanderRangeChanged(ShowCommanderRange);
            }
            if(SettingsRangeToggle(CreatureRangeToggleRect,"生物攻击范围","sword",ShowCreatureRange)){
                ShowCreatureRange=!ShowCreatureRange;if(CreatureRangeChanged!=null)CreatureRangeChanged(ShowCreatureRange);
            }
        }
        void DrawDisplaySettings()
        {
            Rect r=SettingsContentRect;SettingsCard(new Rect(r.x,r.y,r.width,545),"display","显示");
            var display=app.Display;
            if(display==null)return;
            bool enabled=GUI.enabled;GUI.enabled=enabled&&!display.Applying;
            if(MenuActionButton(SettingsWindowRect,"window","窗口",display.DraftMode==FullScreenMode.Windowed?new Color(.14f,.32f,.28f):new Color(.065f,.14f,.16f)))display.SetMode(FullScreenMode.Windowed);
            if(MenuActionButton(SettingsFullscreenRect,"fullscreen","全屏",display.DraftMode==FullScreenMode.FullScreenWindow?new Color(.14f,.32f,.28f):new Color(.065f,.14f,.16f)))display.SetMode(FullScreenMode.FullScreenWindow);
            Text(new Rect(r.x+25,r.y+196,r.width-50,31),"分辨率",label,white);
            if(MenuActionButton(SettingsResolutionPrevRect,"back","",new Color(.075f,.16f,.18f)))display.StepResolution(-1);
            RoundFill(SettingsResolutionRect,new Color(.018f,.045f,.055f),8);
            Text(SettingsResolutionRect,display.DraftWidth+" × "+display.DraftHeight,centered,white);
            if(MenuActionButton(SettingsResolutionNextRect,"next","",new Color(.075f,.16f,.18f)))display.StepResolution(1);
            Fill(new Rect(r.x+25,r.y+342,r.width-50,1),line);
            Text(new Rect(r.x+25,r.y+365,150,29),"当前",small,muted);
            Text(new Rect(r.x+113,r.y+363,r.width-138,51),display.CurrentWidth+" × "+display.CurrentHeight+"  ·  "+DisplayModeName(display.CurrentMode),label,white);
            Text(new Rect(r.x+25,r.y+420,r.width-50,32),display.Status,small,display.HasChanges?gold:mint);
            if(Button(SettingsResetRect,"恢复当前",new Color(.075f,.14f,.16f),muted))display.ResetDraft();
            GUI.enabled=enabled&&!display.Applying&&display.HasChanges;
            if(MenuActionButton(SettingsApplyRect,"ready",display.Applying?"应用中…":"应用设置",mint))display.Apply();
            GUI.enabled=enabled;
        }
        static string DisplayModeName(FullScreenMode mode){return mode==FullScreenMode.Windowed?"窗口":"全屏";}
        void SettingsCard(Rect r,string icon,string name)
        {
            RoundFill(r,new Color(.045f,.10f,.12f,.94f),10);
            DrawMenuGlyph(new Rect(r.x+24,r.y+20,26,26),icon,mint);
            Text(new Rect(r.x+64,r.y+15,r.width-90,37),name,label,white);
        }

        bool MenuActionButton(Rect r,string icon,string text,Color background,bool leftAligned=false)
        {
            bool pressed=Button(r,"",background);
            bool hasText=!string.IsNullOrEmpty(text);
            float size=r.height<48?24:27;
            Rect glyph=new Rect(hasText?r.x+(r.width<150?12:19):r.center.x-size/2,r.center.y-size/2+lastButtonOffset,size,size);
            Color ink=background.g>.45f?new Color(.035f,.15f,.13f):mint;
            DrawMenuGlyph(glyph,icon,GUI.enabled?ink:muted);
            if(hasText){
                if(navigationLabel==null)navigationLabel=new GUIStyle(label){alignment=TextAnchor.MiddleLeft,wordWrap=false};
                if(menuCenteredLabel==null)menuCenteredLabel=new GUIStyle(label){alignment=TextAnchor.MiddleCenter,wordWrap=false};
                GUIStyle style=leftAligned?navigationLabel:menuCenteredLabel;style.fontSize=r.width<225?17:20;
                Rect textRect=leftAligned?new Rect(r.x+62,r.y+lastButtonOffset,r.width-80,r.height):new Rect(r.x+47,r.y+lastButtonOffset,r.width-94,r.height);
                if(r.width<150){textRect=new Rect(r.x+39,r.y+lastButtonOffset,r.width-46,r.height);style=menuCenteredLabel;style.fontSize=17;}
                Text(textRect,text,style,GUI.enabled?(background.g>.45f?new Color(.035f,.1f,.08f):white):muted);
            }
            return pressed;
        }
        void DrawMenuGlyph(Rect r,string icon,Color tint)
        {
            float u=r.width/24;
            if(icon=="back"||icon=="next"){
                bool right=icon=="next";for(int i=0;i<7;i++){float x=right?7+i:15-i;Fill(new Rect(r.x+x*u,r.y+(5+i)*u,2*u,2*u),tint);Fill(new Rect(r.x+x*u,r.y+(17-i)*u,2*u,2*u),tint);}return;
            }
            if(icon=="display"||icon=="window"||icon=="fullscreen"){
                Fill(new Rect(r.x+2*u,r.y+3*u,20*u,15*u),tint);Fill(new Rect(r.x+4*u,r.y+5*u,16*u,11*u),new Color(.04f,.11f,.13f));
                if(icon=="window")Fill(new Rect(r.x+5*u,r.y+6*u,14*u,2*u),tint);
                else if(icon=="fullscreen"){Fill(new Rect(r.x+6*u,r.y+7*u,4*u,2*u),tint);Fill(new Rect(r.x+6*u,r.y+7*u,2*u,4*u),tint);Fill(new Rect(r.x+14*u,r.y+12*u,4*u,2*u),tint);Fill(new Rect(r.x+16*u,r.y+10*u,2*u,4*u),tint);}
                Fill(new Rect(r.x+10*u,r.y+18*u,4*u,3*u),tint);Fill(new Rect(r.x+6*u,r.y+21*u,12*u,2*u),tint);return;
            }
            if(icon=="keys"){
                RoundFill(new Rect(r.x+u,r.y+5*u,22*u,15*u),tint,2*u);
                for(int row=0;row<2;row++)for(int col=0;col<5;col++)Fill(new Rect(r.x+(3+col*4)*u,r.y+(8+row*4)*u,2*u,2*u),new Color(.04f,.11f,.13f));
                Fill(new Rect(r.x+7*u,r.y+16*u,10*u,2*u),new Color(.04f,.11f,.13f));return;
            }
            if(icon=="single"){
                for(int i=0;i<8;i++)Fill(new Rect(r.x+(5+i)*u,r.y+(3+i)*u,2*u,(18-i*2)*u),tint);return;
            }
            if(icon=="room"){
                Fill(new Rect(r.x+2*u,r.y+2*u,8*u,8*u),tint);Fill(new Rect(r.x+14*u,r.y+2*u,8*u,8*u),gold);
                Fill(new Rect(r.x+2*u,r.y+14*u,8*u,8*u),gold);Fill(new Rect(r.x+14*u,r.y+14*u,8*u,8*u),tint);return;
            }
            if(icon=="host"){
                Fill(new Rect(r.x+3*u,r.y+6*u,18*u,16*u),tint);Fill(new Rect(r.x+5*u,r.y+8*u,14*u,12*u),new Color(.04f,.11f,.13f));
                Fill(new Rect(r.x+7*u,r.y+2*u,10*u,4*u),tint);Fill(new Rect(r.x+11*u,r.y+10*u,2*u,8*u),tint);Fill(new Rect(r.x+8*u,r.y+13*u,8*u,2*u),tint);return;
            }
            if(icon=="join"||icon=="exit"){
                Fill(new Rect(r.x+13*u,r.y+2*u,9*u,20*u),tint);Fill(new Rect(r.x+15*u,r.y+4*u,5*u,16*u),new Color(.04f,.11f,.13f));
                Fill(new Rect(r.x+1*u,r.y+11*u,13*u,2*u),tint);for(int i=0;i<5;i++){Fill(new Rect(r.x+(9+i)*u,r.y+(7+i)*u,2*u,2*u),tint);Fill(new Rect(r.x+(9+i)*u,r.y+(15-i)*u,2*u,2*u),tint);}return;
            }
            if(icon=="help"){
                Fill(new Rect(r.x+4*u,r.y+3*u,16*u,3*u),tint);Fill(new Rect(r.x+17*u,r.y+6*u,3*u,6*u),tint);Fill(new Rect(r.x+10*u,r.y+10*u,10*u,3*u),tint);Fill(new Rect(r.x+10*u,r.y+12*u,3*u,4*u),tint);Fill(new Rect(r.x+10*u,r.y+19*u,3*u,3*u),gold);return;
            }
            ArenaIcons.Draw(r,icon);
        }
    }
}
