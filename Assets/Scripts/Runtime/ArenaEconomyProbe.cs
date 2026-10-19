using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlockArena.Core;
using BlockArena.Networking;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Explicit native-player verification only; ordinary play never creates this component.
    public sealed class ArenaEconomyProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-economy-test")>=0)
                new GameObject("Recruitment and event verification").AddComponent<ArenaEconomyProbe>();
        }

        IEnumerator Start()
        {
            string[] args=Environment.GetCommandLineArgs();
            string folder=Path.Combine(Application.dataPath,"..","Captures","Economy");
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);
            ArenaApp app=ArenaApp.Instance;app.VerificationInput=true;
            app.VerificationPointer=new Vector2(-200,-200);
            yield return new WaitForSecondsRealtime(1.5f);
            if(WireProtocol.Version!=4){Fail("native player did not use protocol v4");yield break;}
            app.Host(true);
            float deadline=Time.unscaledTime+18;
            while((app.State==null||app.State.phase!="recruit")&&Time.unscaledTime<deadline)yield return null;
            if(app.State==null||app.State.phase!="recruit"||app.Me==null)
            {Fail("native recruitment handshake failed: "+app.Status);yield break;}
            int balance=app.Me.gold,unitCount=app.Me.units.Count;
            string revision=app.Me.recruitOfferId,candidates=Signature(app.Me.shop);
            if(string.IsNullOrEmpty(revision)||unitCount!=2||app.Me.units.Exists(u=>u.paidPrice!=0))
            {Fail("starter price or private recruitment revision missing from native snapshot");yield break;}
            app.Send(new GameCommand{type="recruit_refresh",offerId=revision});
            deadline=Time.unscaledTime+5;
            while(app.Me.recruitOfferId==revision&&Time.unscaledTime<deadline)yield return null;
            if(app.Me.recruitOfferId==revision||app.Me.gold!=balance-2||Signature(app.Me.shop)==candidates||!app.Me.recruitPending)
            {Fail("native recruit refresh did not change revision/candidates for exactly two coins");yield break;}
            string freshRevision=app.Me.recruitOfferId;
            yield return new WaitForSecondsRealtime(.4f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"01-refreshed-recruitment.png"));
            yield return null;
            app.Status="";
            app.Send(new GameCommand{type="choose",index=0,offerId=revision});
            deadline=Time.unscaledTime+4;
            while(string.IsNullOrEmpty(app.Status)&&Time.unscaledTime<deadline)yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            if(string.IsNullOrEmpty(app.Status)||app.Me.gold!=balance-2||app.Me.units.Count!=unitCount||!app.Me.recruitPending||app.Me.recruitOfferId!=freshRevision)
            {Fail("native stale recruit choice was accepted or changed the army/balance");yield break;}
            var foreign=app.State.players.Find(p=>p.id!=app.PlayerId&&p.units.Count>0);
            if(foreign==null){Fail("native room lacks a foreign army for dismissal ownership verification");yield break;}
            string foreignId=foreign.units[0].id;
            app.Status="";app.Send(new GameCommand{type="dismiss",unitId=foreignId});
            deadline=Time.unscaledTime+4;
            while(string.IsNullOrEmpty(app.Status)&&Time.unscaledTime<deadline)yield return null;
            if(string.IsNullOrEmpty(app.Status)||app.Me.units.Count!=unitCount||app.Me.gold!=balance-2||!app.State.players.Exists(p=>p.units.Exists(u=>u.id==foreignId)))
            {Fail("native dismissal ownership rejection changed an army or refunded money");yield break;}
            string starter=app.Me.units[0].id;
            app.Send(new GameCommand{type="dismiss",unitId=starter});
            deadline=Time.unscaledTime+4;
            while(app.Me.units.Count==unitCount&&Time.unscaledTime<deadline)yield return null;
            if(app.Me.units.Count!=1||app.Me.units.Exists(u=>u.id==starter)||app.Me.gold!=balance-2||!app.Me.recruitPending)
            {Fail("native starter dismissal did not preserve one unit and the pending recruitment with zero refund");yield break;}
            string retained=app.Me.units[0].id;
            app.Status="";app.Send(new GameCommand{type="dismiss",unitId=retained});
            deadline=Time.unscaledTime+4;
            while(string.IsNullOrEmpty(app.Status)&&Time.unscaledTime<deadline)yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            if(string.IsNullOrEmpty(app.Status)||app.Me.units.Count!=1||app.Me.units[0].id!=retained||app.Me.gold!=balance-2)
            {Fail("native last-unit dismissal was accepted or generated a refund");yield break;}
            app.Send(new GameCommand{type="skip"});
            deadline=Time.unscaledTime+6;
            while(app.Me.recruitPending&&Time.unscaledTime<deadline)yield return null;
            if(app.Me.recruitPending){Fail("native recruitment did not finish after skip");yield break;}
            app.SelectedId=retained;
            yield return new WaitForSecondsRealtime(.4f);
            app.Hud.ToggleArmyForVerification();
            yield return new WaitForSecondsRealtime(.3f);
            if(!app.Hud.ArmyManagementVisible||!app.Hud.Contains(app.Hud.ArmyManagementScreenRect.center))
            {Fail("native army management did not open or intercept its visible panel");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"02-retained-last-creature.png"));
            yield return new WaitForSecondsRealtime(.2f);
            app.Hud.ToggleArmyForVerification();
            yield return new WaitForSecondsRealtime(.2f);

            // Isolated client-only presentation snapshots make rare events reproducible.
            // The real TCP server and its economy remain unchanged.
            GameState announced=Copy(app.State);
            announced.round=2;announced.phase="recruit";announced.timeLeft=20;announced.leaderId=app.PlayerId;
            announced.players.Find(p=>p.id==app.PlayerId).recruitPending=true;
            announced.roundEvent=new ArenaEventState{id="verification-gold",kind="gold",round=2,reward=3,x=0,y=0,targetPlayerId=app.PlayerId};
            app.VerificationState=announced;
            yield return new WaitForSecondsRealtime(.2f);
            GameState prepared=Copy(announced);prepared.phase="prep";prepared.timeLeft=35;
            prepared.players.Find(p=>p.id==app.PlayerId).recruitPending=false;
            app.VerificationState=prepared;
            yield return new WaitForSecondsRealtime(.7f);
            if(!app.Hud.EventPreviewVisible||string.IsNullOrEmpty(app.Hud.EventPreviewText))
            {Fail("central gold event was not previewed after recruitment finished");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"03-central-gold-preview.png"));
            yield return new WaitForSecondsRealtime(.2f);
            int arrivalsBefore=app.World.ArenaEventArrivalCount,claimsBefore=app.World.ArenaEventClaimCount;
            GameState goldBattle=Copy(prepared);goldBattle.phase="battle";goldBattle.timeLeft=50;
            AddPlayerFighters(goldBattle);
            var neutralOwner=new PlayerState{id="neutral",name="中央领主",slot=-1};
            var resource=ArenaGame.PreviewFighter(neutralOwner,new UnitState{id="verification-gold-resource",kind="iron_golem",x=0,y=0});
            resource.neutral=true;resource.attack=resource.range=resource.speed=0;resource.scale=.65f;
            goldBattle.fighters.Add(resource);goldBattle.roundEvent.active=true;goldBattle.roundEvent.fighterId=resource.id;
            app.World.ResetView();app.VerificationState=goldBattle;
            yield return new WaitForSecondsRealtime(.8f);
            if(app.World.ActiveNeutralEventCount!=1||!app.World.GoldResourceVisible||app.World.ArenaEventArrivalCount!=arrivalsBefore+1)
            {Fail("central gold event did not render its resource and one arrival cue");yield break;}
            if(!LeaderMarksMatch(app,goldBattle))
            {Fail("leader bounty marks include neutrals or omit the leader's original fighters");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"07-central-gold-resource.png"));
            yield return new WaitForSecondsRealtime(.2f);
            GameState claimed=Copy(goldBattle);claimed.roundEvent.active=false;claimed.roundEvent.claimedBy=app.PlayerId;
            claimed.fighters.Find(f=>f.id==resource.id).dead=true;claimed.fighters.Find(f=>f.id==resource.id).hp=0;
            var rewarded=claimed.players.Find(p=>p.id==app.PlayerId);rewarded.eventIncome=3;rewarded.gold+=3;
            app.VerificationState=claimed;
            yield return new WaitForSecondsRealtime(.35f);
            if(app.World.ActiveNeutralEventCount!=0||app.World.GoldResourceVisible||app.World.ArenaEventClaimCount!=claimsBefore+1)
            {Fail("claimed gold resource remained active or omitted its single claim cue");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"08-central-gold-claimed.png"));
            app.VerificationState=Copy(claimed);
            yield return new WaitForSecondsRealtime(.2f);
            if(app.World.ArenaEventClaimCount!=claimsBefore+1||app.World.ArenaEventArrivalCount!=arrivalsBefore+1)
            {Fail("duplicate claimed snapshot replayed an event arrival or reward cue");yield break;}
            announced=Copy(prepared);announced.round=3;announced.phase="recruit";
            announced.players.Find(p=>p.id==app.PlayerId).recruitPending=true;
            announced.roundEvent=new ArenaEventState{id="verification-boss",kind="boss",round=3,reward=4,x=0,y=0,targetPlayerId=app.PlayerId};
            app.VerificationState=announced;
            yield return new WaitForSecondsRealtime(.2f);
            prepared=Copy(announced);prepared.phase="prep";
            prepared.players.Find(p=>p.id==app.PlayerId).recruitPending=false;
            app.VerificationState=prepared;
            yield return new WaitForSecondsRealtime(.7f);
            if(!app.Hud.EventPreviewVisible||string.IsNullOrEmpty(app.Hud.EventPreviewText))
            {Fail("central boss event was not previewed after recruitment finished");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"04-central-boss-preview.png"));
            yield return new WaitForSecondsRealtime(.2f);
            GameState battle=Copy(prepared);battle.phase="battle";battle.timeLeft=50;AddPlayerFighters(battle);
            // Reuse the gold fighter key as an ordinary owned golem to catch pooled resource decoration leaks.
            var regular=ArenaGame.PreviewFighter(battle.players.Find(p=>p.id==app.PlayerId),new UnitState{id=resource.id,kind="iron_golem",x=70,y=70});
            battle.fighters.Add(regular);
            var neutral=ArenaGame.PreviewFighter(neutralOwner,new UnitState{id="verification-neutral",kind="ravager",x=0,y=0});
            neutral.neutral=true;battle.fighters.Add(neutral);battle.roundEvent.active=true;battle.roundEvent.fighterId=neutral.id;
            app.World.ResetView();app.VerificationState=battle;
            yield return new WaitForSecondsRealtime(1.2f);
            if(!app.Hud.RosterPinned||app.Hud.RosterVisibility<.94f)
            {Fail("leader/event battle screenshot lacks the pinned commander roster");yield break;}
            if(!LeaderMarksMatch(app,battle)||app.World.ActiveNeutralEventCount!=1||app.World.GoldResourceVisible)
            {Fail("boss battle did not preserve leader marks and exactly one non-gold neutral event");yield break;}
            VoxelCreature boss=null,restored=null;
            foreach(var visual in app.World.GetComponentsInChildren<VoxelCreature>())
            {
                if(visual.Slot==-1&&visual.Kind=="ravager")boss=visual;
                if(visual.UnitId==resource.id&&visual.Slot==app.Me.slot)restored=visual;
            }
            if(boss==null){Fail("neutral central boss was not rendered from the presentation snapshot");yield break;}
            if(boss.BountyMarked||restored==null||restored.NeutralEventActive||restored.GoldResourceVisible)
            {Fail("neutral boss was bounty-marked or reused owned golem retained its gold-resource presentation");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"05-leader-and-neutral-boss.png"));
            float hoverUntil=Time.unscaledTime+1.3f;
            while(Time.unscaledTime<hoverUntil)
            {
                app.VerificationPointer=app.World.ViewCamera.WorldToScreenPoint(boss.transform.position+Vector3.up*(boss.Height*boss.Size*.5f));
                app.InspectPointer(app.VerificationPointer.Value);
                yield return null;
            }
            if(app.World.HoveredCreature!=boss||!app.Hud.InspectionExpanded||app.Hud.InspectionSkillCount<1)
            {Fail("neutral boss inspection cannot show delayed actual skill effects");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"06-neutral-boss-inspection.png"));
            yield return new WaitForSecondsRealtime(.2f);
            Debug.Log("ARENA_ECONOMY_OK protocol4 / native real TCP recruit refresh cost and revision / stale recruit rejection / foreign creature ownership / starter zero refund / last creature retained / army management panel / skip / client-only gold and boss previews / gold resource arrival, claim and duplicate suppression / leader original fighter bounty marks excluding neutrals / gold resource decoration resets on owned reuse / neutral central boss model and one-second skill inspection");
            app.VerificationState=null;
            if(Array.IndexOf(args,"-smoke-exit")>=0)Application.Quit(0);
            else{app.Leave();app.VerificationInput=false;app.VerificationPointer=null;Destroy(gameObject);}
        }

        static void AddPlayerFighters(GameState state)
        {
            state.fighters.Clear();
            foreach(var player in state.players)foreach(var unit in player.units)state.fighters.Add(ArenaGame.PreviewFighter(player,unit));
        }
        static bool LeaderMarksMatch(ArenaApp app,GameState state)
        {
            int expected=0;
            foreach(var fighter in state.fighters)if(!fighter.dead&&!fighter.neutral&&!fighter.summoned&&fighter.ownerId==state.leaderId)expected++;
            return expected>0&&app.World.LeaderBountyVisible&&app.World.BountyMarkedCreatureCount==expected;
        }
        static GameState Copy(GameState state){return JsonUtility.FromJson<GameState>(JsonUtility.ToJson(state));}
        static string Signature(string[] choices){var list=new List<string>(choices);list.Sort(StringComparer.Ordinal);return string.Join(",",list.ToArray());}
        static void Fail(string message){Debug.LogError("ARENA_ECONOMY_FAILED "+message);Application.Quit(2);}
    }
}
