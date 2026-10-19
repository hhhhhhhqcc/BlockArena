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
        bool ArmyAvailable { get { return app.Me != null && !app.Me.eliminated && app.State != null && (app.State.phase == "prep" || app.State.phase == "recruit") && !app.ShowRoom && !app.ShowHelp && !app.ShowBestiary; } }
        Rect ArmyToggleRect { get { return new Rect(18,height-114,220,40); } }
        Rect ArmyPanelRect { get { return new Rect(18,Mathf.Max(80,height-636),390,Mathf.Min(508,height-172)); } }
        Rect EventIntroRect { get { return new Rect(width/2-270,91,540,125); } }
        Rect EventSummaryRect { get { return new Rect(18,83,340,57); } }
        Rect BountySummaryRect { get { return new Rect(18,app.State!=null&&app.State.roundEvent!=null&&app.State.roundEvent.kind!="none"?150:83,340,79); } }
        internal bool EventPreviewVisible { get { return app.State != null && app.Me != null && !app.Me.recruitPending && !app.ShowShop && !showArmy && !ShowAudio && (app.State.phase=="prep"||app.State.phase=="recruit") && app.State.roundEvent != null && Time.unscaledTime-eventPreviewAt < 5; } }
        internal string EventPreviewText { get { return app.State==null||app.State.roundEvent==null?"":EventTitle(app.State.roundEvent)+" · "+EventDescription(app.State.roundEvent); } }
        internal void ToggleArmyForVerification() { ToggleArmy(); }
        internal Rect ArmyManagementScreenRect { get { Metrics(); return ToScreenRect(ArmyPanelRect); } }
        internal bool ArmyManagementVisible { get { return showArmy && ArmyAvailable; } }
        bool EconomyBlocksUnderlying { get { return showArmy && ArmyAvailable && ArmyPanelRect.Contains(Mouse()); } }

        bool EconomyCovers(Vector2 p)
        {
            if (ArmyAvailable && ((showArmy&&ArmyPanelRect.Contains(p)) || (app.CanPrepare&&ArmyToggleRect.Contains(p)))) return true;
            if (app.State==null || ModalOpen) return false;
            if(EventPreviewVisible && EventIntroRect.Contains(p)) return true;
            if(app.State.roundEvent!=null && app.State.roundEvent.kind!="none" && EventSummaryRect.Contains(p)) return true;
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
            if(app.State==null||app.Me==null||app.ShowRoom||app.ShowHelp||app.ShowBestiary||app.Me.recruitPending||app.State.phase=="lobby"||app.State.phase=="finished")return;
            ArenaEventState e=app.State.roundEvent;
            if(EventPreviewVisible&&!ShowAudio) {
                float age=Time.unscaledTime-eventPreviewAt;
                eventPreviewMotion.Step(age<4.4f?1:0,20,.65f);
                Rect r=EventIntroRect;r.y-=(1-Mathf.Clamp(eventPreviewMotion.value,0,1.12f))*24;
                Panel(r,new Color(.055f,.10f,.11f,.98f));
                float pulse=1+Mathf.Sin(age*4)*.045f;
                ArenaIcons.Draw(new Rect(r.x+21+27*(1-pulse),r.y+29+27*(1-pulse),54*pulse,54*pulse),e.kind=="boss"?"skull":e.kind=="gold"?"coin":"ready");
                Text(new Rect(r.x+98,r.y+16,420,35),"下一战 · "+EventTitle(e),heading,e.kind=="none"?mint:gold);
                Text(new Rect(r.x+98,r.y+61,420,52),ArenaDescription.Format(EventDescription(e)),small,white);
                Fill(new Rect(r.x+18,r.yMax-10,(r.width-36)*Mathf.Clamp01(eventPreviewMotion.value),2),e.kind=="boss"?gold:mint);
            } else if(e!=null&&e.kind!="none") {
                Rect r=EventSummaryRect;Panel(r,new Color(.045f,.09f,.1f,.92f));
                ArenaIcons.Draw(new Rect(r.x+12,r.y+14,28,28),e.kind=="boss"?"skull":"coin");
                bool claimed=!string.IsNullOrEmpty(e.claimedBy);
                Text(new Rect(r.x+50,r.y+7,278,25),EventTitle(e)+(claimed?" · 已领取":" · +"+e.reward+" 金币"),small,claimed?muted:gold);
                Text(new Rect(r.x+50,r.y+31,278,22),claimed?PlayerNameFor(e.claimedBy)+" 获得奖励":app.State.phase=="results"?"本轮未领取":e.kind=="boss"?"追击总生命最高军团":"击破资源即可领取",micro,muted);
                if(r.Contains(Mouse())) SetHover(EventTitle(e),EventDescription(e)+(claimed?"\n已由 "+PlayerNameFor(e.claimedBy)+" 领取":"\n事件奖励独立于击杀收入上限。"),HudPointer);
            }
            if(app.State.phase=="battle"&&!string.IsNullOrEmpty(app.State.leaderId)) {
                Rect r=BountySummaryRect;Panel(r,new Color(.12f,.095f,.06f,.94f));
                float entry=Mathf.Clamp01((Time.unscaledTime-leaderAnnouncedAt)/.55f);
                Fill(new Rect(r.x+4,r.y+3,(r.width-8)*entry,2),gold);
                ArenaIcons.Draw(new Rect(r.x+12,r.y+14,27,27),"sword");
                Text(new Rect(r.x+49,r.y+9,278,26),"悬赏 · "+PlayerNameFor(app.State.leaderId),label,gold);
                Text(new Rect(r.x+13,r.y+46,314,24),app.Me.id==app.State.leaderId?"你被悬赏了 · 守住原始军团":app.Me.bountyIncome>=ArenaGame.BountyIncomeCap?"本轮悬赏已领满 · +3 金币":"进度 "+(app.Me.bountyKills%3)+"/3 · 奖励 "+app.Me.bountyIncome+"/3 金币",micro,white);
                if(r.Contains(Mouse()))SetHover("领先者悬赏","击杀标记军团的原始生物，每 3 只额外 +1 金币，每位玩家每轮最多 +3。\n按指挥官生命、上轮名次、军团战力判定，开战锁定，完全持平不设悬赏。",HudPointer);
            }
        }
        string PlayerNameFor(string id) { PlayerState p=app.State==null?null:app.State.players.Find(v=>v.id==id);return p==null?"未知军团":Trim(p.name,10); }

        void ToggleArmy() { showArmy=!showArmy;ShowOwnedSkills=false;CloseSkillDetails();ClearHover(); }

        void DrawArmyManagement()
        {
            if(!ArmyAvailable){showArmy=false;return;}
            bool enabled=GUI.enabled;
            drawingArmy=true;
            if(app.CanPrepare) {
                GUI.enabled=!AudioBlocksUnderlying&&!SkillBlocksUnderlying;
                if(IconButton(ArmyToggleRect,"bag","军团 · "+app.Me.units.Count,new Color(.15f,.23f,.23f))) ToggleArmy();
            }
            if(showArmy) {
                GUI.enabled=!AudioBlocksUnderlying;
                Rect r=ArmyPanelRect;Panel(r,new Color(.055f,.1f,.11f,.99f));
                Text(new Rect(r.x+17,r.y+15,r.width-70,30),"军团 · 调整阵容",label,gold);
                if(IconButton(new Rect(r.xMax-48,r.y+12,34,31),"close","",new Color(.17f,.22f,.23f)))showArmy=false;
                Text(new Rect(r.x+17,r.y+54,r.width-34,46),"遣散返还实付 50% · 至少保留 1 只\n免费初始生物返还 0 金币",micro,muted);
                Rect viewport=new Rect(r.x+12,r.y+110,r.width-24,r.height-122);
                armyScroll=GUI.BeginScrollView(viewport,armyScroll,new Rect(0,0,viewport.width-22,Mathf.Max(viewport.height,app.Me.units.Count*86)));
                for(int i=0;i<app.Me.units.Count;i++) {
                    UnitState unit=app.Me.units[i];CreatureDefinition d=CreatureCatalog.Get(unit.kind);
                    Rect row=new Rect(0,i*86,viewport.width-24,78);Fill(row,new Color(.10f,.16f,.16f,.94f));
                    ArenaIcons.Creature(new Rect(8,row.y+13,46,46),d.model??d.id,MobColor(d));
                    Text(new Rect(63,row.y+9,row.width-68,27),d.name,small,white);
                    int refund=ArenaGame.DismissRefund(unit);
                    Text(new Rect(63,row.y+42,119,25),"实付 "+unit.paidPrice, micro,muted);
                    GUI.enabled=!AudioBlocksUnderlying&&app.Connected&&app.Me.units.Count>1&&dismissalPendingId==null;
                    if(IconButton(new Rect(row.width-141,row.y+37,132,31),"coin","遣散 +"+refund,new Color(.3f,.24f,.15f))) {
                        dismissalPendingId=unit.id;dismissalPendingUntil=Time.unscaledTime+2;
                        app.Send(new GameCommand{type="dismiss",unitId=unit.id});ClearHover();
                    }
                    GUI.enabled=!AudioBlocksUnderlying;
                }
                GUI.EndScrollView();
            }
            drawingArmy=false;GUI.enabled=enabled;
        }
    }
}
