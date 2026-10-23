using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        bool showArmy, drawingArmy;
        Vector2 armyScroll;
        float eventPreviewAt = -20, leaderAnnouncedAt = -20, dismissalPendingUntil;
        string dismissalPendingId;
        readonly Spring eventPreviewMotion = new Spring();
        bool ArmyAvailable { get { return app.Me != null && !app.Me.eliminated && app.State != null && (app.State.phase == "prep" || app.State.phase == "recruit") && !app.ShowRoom && !app.ShowBestiary; } }
        Rect ArmyToggleRect { get { return new Rect(18,height-114,220,40); } }
        Rect ArmyPanelRect { get { return new Rect(18,Mathf.Max(80,height-636),390,Mathf.Min(508,height-172)); } }
        Rect EventIntroRect { get { return new Rect(width/2-270,91,540,125); } }
        Rect EventSummaryRect { get { return new Rect(18,83,340,57); } }
        Rect BountySummaryRect { get { return new Rect(18,83,278,57); } }
        internal bool EventPreviewVisible { get { return false; } }
        internal string EventPreviewText { get { return app.State==null||app.State.roundEvent==null?"":EventTitle(app.State.roundEvent)+" · "+EventDescription(app.State.roundEvent); } }
        internal void ToggleArmyForVerification() { showArmy=false; }
        internal Rect ArmyManagementScreenRect { get { Metrics(); return ToScreenRect(ArmyPanelRect); } }
        internal bool ArmyManagementVisible { get { return false; } }
        bool EconomyBlocksUnderlying { get { return PinnedInspectionCovers(Mouse()); } }

        bool EconomyCovers(Vector2 p)
        {
            if (PinnedInspectionCovers(p)) return true;
            if (app.State==null || ModalOpen) return false;
            return app.State.phase=="battle"&&!string.IsNullOrEmpty(app.State.leaderId)&&BountySummaryRect.Contains(p);
        }

        void ObserveEconomy(GameState before,GameState next,bool baseline)
        {
            if(baseline) { showArmy=false; armyScroll=Vector2.zero; eventPreviewAt=leaderAnnouncedAt=-20; dismissalPendingId=null; eventPreviewMotion.value=eventPreviewMotion.velocity=0; }
            if(next==null) return;
            if(next.phase=="battle"&&(before==null||before.phase!="battle")) { leaderAnnouncedAt=Time.unscaledTime; showArmy=false; }
            PlayerState prior=before==null?null:before.players.Find(p=>p.id==app.PlayerId);
            PlayerState me=next.players.Find(p=>p.id==app.PlayerId);
            if(!baseline&&prior!=null&&me!=null&&prior.recruitPending&&!me.recruitPending&&next.roundEvent!=null) {
                eventPreviewAt=Time.unscaledTime; eventPreviewMotion.value=eventPreviewMotion.velocity=0; showArmy=false;
                if(SoundCue!=null) SoundCue(next.roundEvent.kind=="none"?"ready":"recruit");
            }
            if(dismissalPendingId!=null&&(me==null||!me.units.Exists(u=>u.id==dismissalPendingId)||Time.unscaledTime>dismissalPendingUntil)) dismissalPendingId=null;
        }

        static string EventTitle(ArenaEventState e) { return e.kind=="boss"?"中央首领":e.kind=="gold"?"中央金矿":"平静战场"; }
        static string EventDescription(ArenaEventState e)
        {
            return e.kind=="boss"?"优先攻击总生命最高军团，击杀 +"+e.reward+" 金币":e.kind=="gold"?"击破中央资源获得 +"+e.reward+" 金币":"本轮没有中央事件，专注军团交锋";
        }

        void DrawEconomyObjectives()
        {
            if(app.State==null||app.Me==null||app.ShowRoom||app.ShowBestiary||app.Me.recruitPending||app.State.phase=="lobby"||app.State.phase=="finished")return;
            if(app.State.phase=="battle"&&!string.IsNullOrEmpty(app.State.leaderId)) {
                Rect r=BountySummaryRect;Panel(r,new Color(.12f,.095f,.06f,.94f));
                float entry=Mathf.Clamp01((Time.unscaledTime-leaderAnnouncedAt)/.55f);
                Fill(new Rect(r.x+4,r.y+3,(r.width-8)*entry,2),gold);
                ArenaIcons.Draw(new Rect(r.x+12,r.y+14,25,25),"sword");
                Text(new Rect(r.x+49,r.y+7,217,25),PlayerNameFor(app.State.leaderId),small,gold);
                Text(new Rect(r.x+49,r.y+31,217,22),app.Me.id==app.State.leaderId?"悬赏":(app.Me.bountyKills%3)+" / 3    ＋"+app.Me.bountyIncome,micro,muted);
                if(r.Contains(Mouse()))SetHover("领先者悬赏","击杀标记军团的原始生物，每 3 只额外 +1 金币，每位玩家每轮最多 +3。\n按指挥官生命、上轮名次、军团战力判定，开战锁定，完全持平不设悬赏。",HudPointer);
            }
        }
        string PlayerNameFor(string id) { PlayerState p=app.State==null?null:app.State.players.Find(v=>v.id==id);return p==null?"未知军团":Trim(p.name,10); }

        void ToggleArmy() { showArmy=!showArmy;ShowOwnedSkills=false;CloseSkillDetails();ClearHover(); }

        void DrawArmyManagement() { showArmy=false; }
    }
}
