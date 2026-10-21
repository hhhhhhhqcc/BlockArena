using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        bool drawingModal, drawingMarket, drawingSkillDetails;
        int modalLayer;
        bool commanderTooltip;
        Rect commanderTooltipAnchor, hoverTooltipRect;
        internal Rect LastHoverScreenRect {
            get {
                Metrics();
                if(Time.unscaledTime>hoverUntil||AudioBlocksUnderlying||app.State==null||app.ShowGameMenu||app.ShowHelp||app.ShowBestiary)return Rect.zero;
                if(inspectionDefinition!=null)return inspectionTooltip;
                return hoverTooltipRect.width>0?ToScreenRect(hoverTooltipRect):Rect.zero;
            }
        }
        internal Rect HoverTooltipScreenRect { get { return LastHoverScreenRect; } }
        readonly Color surface = new Color(.035f,.072f,.085f,.97f);
        readonly Color line = new Color(.24f,.36f,.37f,.7f);
        Rect CommandDockRect { get { return new Rect(width/2-446,height-106,892,88); } }
        bool CommandDockVisible { get { return app.State!=null&&app.Me!=null&&!app.Me.eliminated&&!ModalOpen&&
            (app.State.phase=="prep"||app.State.phase=="recruit"||app.State.phase=="tactics"||app.State.phase=="battle"); } }
        internal Rect CommandDockScreenRect { get { Metrics();return CommandDockVisible?ToScreenRect(CommandDockRect):Rect.zero; } }
        internal Rect SessionSummaryScreenRect { get { Metrics();return ToScreenRect(HeaderRect); } }
        internal Rect ToolbarScreenRect { get { return Rect.zero; } }
        internal Rect SecondaryActionScreenRect { get { Metrics();return ToScreenRect(OwnedSkillToggleRect); } }
        internal Rect PrimaryActionScreenRect { get { Metrics();return ToScreenRect(app.State!=null&&app.State.phase=="tactics"?TacticsReadyRect:ReadyRect); } }
        internal bool UnderlayInputBlockedForVerification { get { return !LayerAcceptsPointer(); } }

        // Occlusion is an input concern. It must never change a functional button's colour.
        // Keep this separate from GUI.enabled, which only expresses game-rule availability.
        bool LayerAcceptsPointer()
        {
            Vector2 p=Mouse();
            if(drawingAudio)return SettingsControlsAvailable;
            if(ShowSettings){
                if(!drawingModal)return false;
                int settingsFront=app.ShowHelp?6:app.ShowBestiary?5:app.State!=null&&app.ShowRoom?3:-1;
                return modalLayer==settingsFront;
            }
            if(audioPointerOwned||AudioCovers(p))return false;
            if(drawingModal){
                int front=app.ShowHelp?6:app.ShowBestiary?5:app.State!=null&&app.ShowRoom?3:app.ShowGameMenu?7:app.State!=null&&app.State.phase=="lobby"?3:RecruitVisible?2:app.State!=null&&app.State.phase=="finished"?4:1;
                return modalLayer==front;
            }
            if(ModalOpen&&!(drawingCommander&&RecruitVisible&&!app.ShowGameMenu)&&!(drawingCommander&&app.State!=null&&app.State.phase=="lobby"&&!app.ShowGameMenu))return false;
            if(drawingCommander)return true;
            if(CommanderOverlayCovers(p))return false;
            if(drawingArmy)return true;
            if(PinnedInspectionCovers(p))return false;
            if(!drawingSkills&&(skillPointerOwned||SkillUiCovers(p)))return false;
            if(drawingSkills&&!drawingSkillDetails&&HasSkillDetails&&SkillDetailsRect.Contains(p))return false;
            if(!drawingMarket&&(marketPointerOwned||marketDragging||MarketVisible&&MarketRect.Contains(p)))return false;
            return true;
        }
        void ClickFeedback(string cue)
        {
            if(cue!=null&&SoundCue!=null)SoundCue("ui_click");
        }
        void DrawModal(int layer,System.Action draw)
        {
            bool before=drawingModal;int previous=modalLayer;drawingModal=true;modalLayer=layer;
            try{draw();}finally{drawingModal=before;modalLayer=previous;}
        }
        void SetCommanderHover(string titleText,string bodyText,Vector2 screen)
        {
            if(!LayerAcceptsPointer())return;
            SetHover(titleText,bodyText,screen);commanderTooltip=true;
            commanderTooltipAnchor=app.Tactics.SkillPickerOpen&&CommanderPickerRect.Contains(Mouse())?CommanderPickerRect:CommanderRect;
        }
        Rect CommanderTooltipRect(float w,float h)
        {
            Rect anchor=commanderTooltipAnchor;
            float x=anchor.xMax+12;
            if(x+w>width-18)x=anchor.x-w-12;
            float bottom=!RecruitVisible&&app.State!=null&&app.State.phase!="lobby"?CommandDockRect.y-12:height-20;
            float y=Mathf.Clamp(anchor.y,84,Mathf.Max(84,bottom-h));
            return new Rect(Mathf.Clamp(x,18,Mathf.Max(18,width-w-18)),y,w,h);
        }
        Vector2 BeginUiScroll(Rect viewport,Vector2 scroll,Rect content)
        {
            bool enabled=GUI.enabled;
            GUI.enabled=enabled&&(LayerAcceptsPointer()||Event.current.type==EventType.Repaint||Event.current.type==EventType.Layout);
            Vector2 result=GUI.BeginScrollView(viewport,scroll,content);GUI.enabled=enabled;return result;
        }
        void PanelHeading(Rect r,string icon,string name,string detail=null)
        {
            if(!string.IsNullOrEmpty(icon))ArenaIcons.Draw(new Rect(r.x+17,r.y+15,25,25),icon);
            Text(new Rect(r.x+(string.IsNullOrEmpty(icon)?17:53),r.y+12,r.width-106,31),name,label,white);
            Fill(new Rect(r.x+17,r.y+51,r.width-34,1),line);
            if(!string.IsNullOrEmpty(detail))Text(new Rect(r.x+17,r.y+62,r.width-34,26),detail,micro,muted);
        }
        void DrawCommandDock()
        {
            if(!CommandDockVisible)return;
            Rect r=CommandDockRect;RoundedPanel(r,surface);
            Fill(new Rect(r.x+256,r.y+16,1,r.height-32),line);
            Fill(new Rect(r.x+556,r.y+16,1,r.height-32),line);
            if(app.State.phase=="battle"){
                Rect status=new Rect(r.x+572,r.y+22,302,45);
                ArenaIcons.Draw(new Rect(status.x+10,status.y+9,25,25),"sword");
                Text(new Rect(status.x+49,status.y+7,239,31),"自动交战",small,muted);
            }
            if(!app.CanPrepare){
                Rect count=MarketToggleRect;ArenaIcons.Draw(new Rect(count.x+9,count.y+10,24,24),"heart");
                Text(new Rect(count.x+43,count.y+9,82,29),app.Me.units.Count+" 只",small,muted);
            }
        }
    }
}
