using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlockArena.Core;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Opt-in smoke driver for the built native player; it is inactive in ordinary play.
    public sealed class ArenaSmokeProbe : MonoBehaviour
    {
        readonly Dictionary<string,int> combatBalances=new Dictionary<string,int>();
        bool watchCombat,realtimeIncomeObserved,incomeFailed;
        string captureFolder;

        void Update()
        {
            var app=ArenaApp.Instance;
            if(!watchCombat||incomeFailed||app==null||app.State==null||app.State.phase!="battle")return;
            foreach(var player in app.State.players)
            {
                int startingBalance;
                if(combatBalances.TryGetValue(player.id,out startingBalance)&&player.gold!=startingBalance+Mathf.Min(5,player.roundKills)+player.bountyIncome+player.eventIncome)
                {incomeFailed=true;Fail("capped kills, bounty and event gold not awarded in real time to "+player.name);return;}
                if(player.roundKills>0&&!realtimeIncomeObserved)
                {
                    realtimeIncomeObserved=true;
                    ScreenCapture.CaptureScreenshot(Path.Combine(captureFolder,"12-realtime-income.png"));
                }
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            string[] args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-smoke-test")>=0)
                new GameObject("Native player verification").AddComponent<ArenaSmokeProbe>();
        }
        IEnumerator Start()
        {
            string[] args=Environment.GetCommandLineArgs();
            string folder=Path.Combine(Application.dataPath,"..","Captures");
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);
            captureFolder=folder;
            var app=ArenaApp.Instance;
            app.VerificationInput=true;
            yield return new WaitForSecondsRealtime(1.5f);
            if(app.Sound==null||app.Sound.ClipsLoaded!=27||app.Sound.MusicTrackCount!=3||!app.Sound.MusicIsPlaying)
            {Fail("original music or sound assets not loaded");yield break;}
            string menuTrack=app.Sound.CurrentMusic;
            int soundBaseline=app.Sound.SoundCount;
            float savedMusic=app.Sound.MusicVolume,savedEffects=app.Sound.SfxVolume;
            bool savedMute=app.Sound.Muted;
            app.Sound.SetVolumes(savedMusic,.6f);
            app.Sound.Play("click",Vector3.zero,1);
            app.Sound.SetVolumes(savedMusic,savedEffects,savedMute);
            if(app.Sound.SoundCount<=soundBaseline){Fail("sound source did not play the click cue");yield break;}
            Texture2D cursorPreview=app.VerifyCursor();
            File.WriteAllBytes(Path.Combine(folder,"00-soul-cursors.png"),cursorPreview.EncodeToPNG());
            Destroy(cursorPreview);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"01-menu.png"));
            yield return new WaitForSecondsRealtime(.5f);
            app.Host(true);
            float deadline=Time.realtimeSinceStartup+18;
            while((app.State==null||app.State.phase!="recruit")&&Time.realtimeSinceStartup<deadline)yield return null;
            if(app.State==null||app.State.phase!="recruit"){Fail("recruit handshake failed: "+app.Status);yield break;}
            yield return new WaitForSecondsRealtime(1.2f);
            app.Hud.ShowAudio=true;
            yield return new WaitForSecondsRealtime(.3f);
            if(!app.Hud.Contains(app.Hud.AudioScreenRect.center)){Fail("audio panel pointer region missing");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"16-audio-controls.png"));
            yield return new WaitForSecondsRealtime(.2f);
            app.Hud.ShowAudio=false;
            if(app.Hud.CoinFeedbackCount!=0){Fail("opening balance replayed as combat income");yield break;}
            int expectedRecruitSkills=0;
            string recruitEffects=app.Hud.RecruitEffectText;
            foreach(string kind in app.Me.shop)
                foreach(string id in CreatureCatalog.Get(kind).skills)
                {
                    expectedRecruitSkills++;
                    if(!recruitEffects.Contains(CreatureAbilities.Get(id).name))
                    {Fail("recruit card omitted an innate skill from its effect introduction: "+kind);yield break;}
                }
            bool quantifiedRecruitEffect=false;
            foreach(char c in recruitEffects)if(char.IsDigit(c)){quantifiedRecruitEffect=true;break;}
            if(!app.Hud.RecruitEffectsFit||app.Hud.RecruitSkillCount!=expectedRecruitSkills||!quantifiedRecruitEffect)
            {Fail("recruit cards do not show all concise quantified skill effects within their card bounds");yield break;}
            // Measure the real card layout for every species, including three-skill bosses.
            // This avoids depending on the randomly offered first-round candidates.
            foreach(string kind in CreatureCatalog.Kinds)
                if(!app.Hud.RecruitCreatureEffectsFit(CreatureCatalog.Get(kind)))
                {Fail("recruit skill effects do not fit the actual card layout: "+kind);yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"02-recruit.png"));
            yield return new WaitForSecondsRealtime(.5f);
            int choice=-1,price=999;
            for(int i=0;i<app.Me.shop.Length;i++)if(CreatureCatalog.Get(app.Me.shop[i]).price<price){choice=i;price=CreatureCatalog.Get(app.Me.shop[i]).price;}
            // Save the initial 12 coins to exercise the skill-only shop in every random room.
            bool recruit=false;int originalGold=app.Me.gold;
            app.Send(recruit?new GameCommand{type="choose",index=choice}:new GameCommand{type="skip"});
            yield return new WaitForSecondsRealtime(1.5f);
            if(app.Me==null||app.Me.recruitPending||app.Me.units.Count!=(recruit?3:2)||app.Me.gold!=originalGold-(recruit?price:0)){Fail("paid recruit / skip accounting failed");yield break;}
            var unit=app.Me.units[0];
            VoxelCreature placed=null;
            foreach(var candidate in app.World.GetComponentsInChildren<VoxelCreature>())if(candidate.UnitId==unit.id){placed=candidate;break;}
            if(placed==null){Fail("preparation creature not rendered");yield break;}
            Camera camera=app.World.ViewCamera;
            Vector3 original=placed.transform.position;
            Vector2 grab=camera.WorldToScreenPoint(original+Vector3.up*(placed.Height*placed.Size*.5f));
            app.ProcessPlacementPointer(grab,true,true,false);
            yield return new WaitForSecondsRealtime(.12f);
            if(app.SelectedId!=unit.id||Vector3.Distance(original,placed.transform.position)>.03f){Fail("stationary grab changed creature position");yield break;}
            Point2 position=ArenaGame.LocalToWorld(app.Me.slot,70,230);
            Vector3 destination=new Vector3(position.x/25f,.06f,position.y/25f);
            Vector2 drop=grab+(Vector2)(camera.WorldToScreenPoint(destination)-camera.WorldToScreenPoint(original));
            app.ProcessPlacementPointer(drop,false,true,false);
            yield return new WaitForSecondsRealtime(.4f);
            // Leave the authoritative client snapshot stale while the real TCP server accepts the release.
            app.SnapshotPauseUntil=Time.unscaledTime+.7f;
            app.ProcessPlacementPointer(drop,false,false,true);
            float staleUntil=Time.unscaledTime+.44f;
            while(Time.unscaledTime<staleUntil)
            {
                if(Vector3.Distance(destination,placed.transform.position)>.14f){Fail("released creature snapped back during delayed snapshot");yield break;}
                yield return null;
            }
            if(Mathf.Abs(app.Me.units[0].x-position.x)<.1f&&Mathf.Abs(app.Me.units[0].y-position.y)<.1f){Fail("delay regression did not exercise a stale snapshot");yield break;}
            deadline=Time.realtimeSinceStartup+5;
            while(string.IsNullOrEmpty(app.Me.units[0].placementId)&&Time.realtimeSinceStartup<deadline)yield return null;
            if(string.IsNullOrEmpty(app.Me.units[0].placementId)||Mathf.Abs(app.Me.units[0].x-position.x)>1||Mathf.Abs(app.Me.units[0].y-position.y)>1){Fail("drag release did not receive matching server placement");yield break;}
            // Exercise the same pointer path as play, without changing any server-owned army or health.
            app.ShowShop=false;app.Hud.ShowOwnedSkills=false;app.Hud.CloseSkillDetails();
            app.VerificationPointer=new Vector2(-200,-200);
            app.InspectPointer(app.VerificationPointer.Value);
            yield return new WaitForSecondsRealtime(.9f);
            if(app.Hud.RosterPinned||app.Hud.RosterVisibility>.06f)
            {Fail("preparation commander roster did not collapse when pointer was away");yield break;}
            Rect edge=app.Hud.RosterEdgeScreenRect;
            if(edge.width<=0||edge.height<=0||!new Rect(0,0,Screen.width,Screen.height).Contains(edge.center)||!app.Hud.Contains(edge.center))
            {Fail("collapsed commander roster has no usable intercepted edge handle");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"21-roster-collapsed.png"));
            app.VerificationPointer=edge.center;
            yield return new WaitForSecondsRealtime(.6f);
            if(app.Hud.RosterPinned||app.Hud.RosterVisibility<.94f)
            {Fail("preparation roster did not reveal on edge hover");yield break;}
            Rect shownRoster=app.Hud.RosterScreenRect;
            Vector2 rosterBody=shownRoster.center;
            if(shownRoster.xMin<-.5f||shownRoster.xMax>Screen.width+.5f||!app.Hud.Contains(rosterBody))
            {Fail("revealed commander roster is clipped or does not intercept input");yield break;}
            app.VerificationPointer=rosterBody;
            yield return new WaitForSecondsRealtime(.35f);
            if(app.Hud.RosterVisibility<.94f)
            {Fail("commander roster collapsed while pointer entered its panel");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"22-roster-revealed.png"));
            app.VerificationPointer=new Vector2(-200,-200);
            yield return new WaitForSecondsRealtime(1.3f);
            if(app.Hud.RosterVisibility>.06f||app.Hud.Contains(rosterBody))
            {Fail("collapsed commander roster still blocks its old empty arena space");yield break;}

            app.InspectPointer(app.VerificationPointer.Value);
            float hoverStarted=Time.unscaledTime;
            bool baseCaptured=false;
            while(Time.unscaledTime-hoverStarted<1.25f)
            {
                app.VerificationPointer=InspectionPoint(camera,placed);
                app.InspectPointer(app.VerificationPointer.Value);
                if(app.World.HoveredCreature==null||app.World.HoveredCreature.UnitId!=placed.UnitId)
                {Fail("preparation delayed hover lost the intended creature");yield break;}
                float hoveredFor=Time.unscaledTime-hoverStarted;
                if(hoveredFor<.75f&&app.Hud.InspectionExpanded)
                {Fail("creature skill details appeared before one second of uninterrupted hover");yield break;}
                if(!baseCaptured&&hoveredFor>.3f)
                {
                    if(app.Hud.InspectionSkillCount!=0||!TooltipFits(app.Hud.InspectionTooltipScreenRect))
                    {Fail("instant creature tooltip contains delayed skills or falls outside the display");yield break;}
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,"19-hover-base.png"));baseCaptured=true;
                }
                yield return null;
            }
            if(!app.Hud.InspectionExpanded||app.Hud.InspectionSkillCount<1||!TooltipFits(app.Hud.InspectionTooltipScreenRect)||!HasSkillExplanation(app.Hud.InspectionSkillText,CreatureCatalog.Get(placed.Kind)))
            {Fail("one-second creature hover did not reveal readable concise skill effects");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"20-hover-skills.png"));
            yield return null;
            // A second instance of the same species must not inherit the first instance's timer.
            var sameSpecies=new UnitState{id="verification-other-"+unit.id,kind=unit.kind,x=unit.x,y=unit.y};
            app.Hud.SetInspection(CreatureCatalog.Get(unit.kind),null,sameSpecies,app.Me,app.VerificationPointer.Value);
            if(app.Hud.InspectionExpanded||app.Hud.InspectionSkillCount!=0)
            {Fail("switching creature identity within one species preserved expanded skill details");yield break;}
            app.InspectPointer(app.VerificationPointer.Value);
            if(app.Hud.InspectionExpanded){Fail("returning to a previous creature inherited its old hover timer");yield break;}
            float firstUnitUntil=Time.unscaledTime+.45f;
            while(Time.unscaledTime<firstUnitUntil)
            {
                app.VerificationPointer=InspectionPoint(camera,placed);app.InspectPointer(app.VerificationPointer.Value);
                yield return null;
            }
            VoxelCreature otherPlaced=null;
            foreach(var candidate in app.World.GetComponentsInChildren<VoxelCreature>())
                if(candidate.Kind!="steve"&&candidate.Slot==app.Me.slot&&candidate.UnitId!=placed.UnitId){otherPlaced=candidate;break;}
            if(otherPlaced==null){Fail("second preparation creature missing for hover-switch regression");yield break;}
            float switchedUntil=Time.unscaledTime+.7f;
            while(Time.unscaledTime<switchedUntil)
            {
                app.VerificationPointer=InspectionPoint(camera,otherPlaced);app.InspectPointer(app.VerificationPointer.Value);
                if(app.World.HoveredCreature==null||app.World.HoveredCreature.UnitId!=otherPlaced.UnitId||app.Hud.InspectionExpanded)
                {Fail("switching real creatures carried the previous creature's hover duration");yield break;}
                yield return null;
            }
            app.VerificationPointer=new Vector2(-200,-200);app.InspectPointer(app.VerificationPointer.Value);
            if(app.Hud.InspectionExpanded||app.Hud.InspectionSkillCount!=0)
            {Fail("leaving a creature did not immediately reset delayed skill details");yield break;}
            app.InspectPointer(InspectionPoint(camera,placed));
            if(app.Hud.InspectionExpanded){Fail("re-entering after pointer leave restored an expired hover timer");yield break;}
            app.InspectPointer(app.VerificationPointer.Value);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"03-preparation.png"));
            yield return new WaitForSecondsRealtime(.5f);
            app.ShowShop=true;
            yield return new WaitForSecondsRealtime(.65f);
            Rect marketBefore=app.Hud.MarketScreenRect;
            Vector2 titleGrab=new Vector2(marketBefore.x+marketBefore.width*.3f,marketBefore.yMax-20);
            Vector2 movedTitle=titleGrab+new Vector2(-90,55);
            string selectedBefore=app.SelectedId;
            Point2 steveBefore=app.Me.steve;
            Vector3 creatureBefore=placed.transform.position;
            app.Hud.ProcessMarketPointer(titleGrab,true,true,false);
            if(!app.Hud.PointerCaptured){Fail("market title did not capture pointer");yield break;}
            app.ProcessPlacementPointer(titleGrab,true,true,false);
            app.Hud.ProcessMarketPointer(movedTitle,false,true,false);
            app.ProcessPlacementPointer(movedTitle,false,true,false);
            app.Hud.ProcessMarketPointer(movedTitle,false,false,true);
            app.ProcessPlacementPointer(movedTitle,false,false,true);
            yield return new WaitForSecondsRealtime(.25f);
            Rect marketAfter=app.Hud.MarketScreenRect;
            if((marketAfter.position-marketBefore.position).sqrMagnitude<25||app.Hud.PointerCaptured||!app.Hud.Contains(marketAfter.center))
            {Fail("market drag position, release or hit area failed");yield break;}
            if(app.SelectedId!=selectedBefore||Vector3.Distance(creatureBefore,placed.transform.position)>.04f||Mathf.Abs(app.Me.steve.x-steveBefore.x)>.1f||Mathf.Abs(app.Me.steve.y-steveBefore.y)>.1f)
            {Fail("market drag leaked into creature or commander movement");yield break;}
            Vector2 clampGrab=new Vector2(marketAfter.x+marketAfter.width*.3f,marketAfter.yMax-20);
            app.Hud.ProcessMarketPointer(clampGrab,true,true,false);
            app.Hud.ProcessMarketPointer(new Vector2(-Screen.width,Screen.height*2),false,true,false);
            app.Hud.ProcessMarketPointer(new Vector2(-Screen.width,Screen.height*2),false,false,true);
            Rect clamped=app.Hud.MarketScreenRect;
            if(clamped.x<0||clamped.y<0||clamped.xMax>Screen.width+.5f||clamped.yMax>Screen.height+.5f)
            {Fail("market can be dragged outside the display");yield break;}
            // Return the window to its useful saved location before checking close/reopen persistence.
            Vector2 restoreGrab=new Vector2(clamped.x+clamped.width*.3f,clamped.yMax-20);
            Vector2 restoreDrop=restoreGrab+(marketAfter.position-clamped.position);
            app.Hud.ProcessMarketPointer(restoreGrab,true,true,false);
            app.Hud.ProcessMarketPointer(restoreDrop,false,true,false);
            app.Hud.ProcessMarketPointer(restoreDrop,false,false,true);
            Rect persisted=app.Hud.MarketScreenRect;
            app.ShowShop=false;yield return new WaitForSecondsRealtime(.3f);
            app.ShowShop=true;yield return new WaitForSecondsRealtime(.6f);
            if((app.Hud.MarketScreenRect.position-persisted.position).sqrMagnitude>1)
            {Fail("market close/reopen lost the dragged location");yield break;}
            ShopOffer skill=null;
            foreach(var offer in app.Me.market)
            {
                if(offer.kind!="skill"){Fail("equipment still offered by the skill shop");yield break;}
                if(skill==null&&offer.price<=app.Me.gold)skill=offer;
            }
            if(skill==null){Fail("no affordable skill for native market verification");yield break;}
            int balance=app.Me.gold;string skillId=skill.itemId,offerId=skill.id;
            app.Send(new GameCommand{type="purchase",offerId=offerId});
            yield return new WaitForSecondsRealtime(.6f);
            if(app.Me.gold!=balance-skill.price||!app.Me.upgrades.Contains(skillId)){Fail("native skill purchase was not synchronized");yield break;}
            if(app.Hud.CoinFeedbackCount!=0){Fail("spending generated a positive income animation");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"06-market.png"));
            yield return new WaitForSecondsRealtime(.4f);
            app.Hud.PreviewSkillForVerification("zombie_resilience");
            app.VerificationPointer=app.Hud.SkillDetailsScreenRect.center;
            yield return new WaitForSecondsRealtime(.35f);
            int expectedSpecies=0,expectedMatches=0;
            var testedSkill=TradeCatalog.Get("zombie_resilience");
            foreach(string kind in CreatureCatalog.Kinds)if(TradeCatalog.Applies(testedSkill,CreatureCatalog.Get(kind)))expectedSpecies++;
            foreach(var owned in app.Me.units)if(TradeCatalog.Applies(testedSkill,CreatureCatalog.Get(owned.kind)))expectedMatches++;
            if(app.Hud.SkillDetailSpeciesCount!=expectedSpecies||app.Hud.SkillDetailMatchingUnits!=expectedMatches||expectedMatches==0)
            {Fail("skill description does not list all applicable creatures or current army matches");yield break;}
            if(app.Hud.HighlightedSkillId!="zombie_resilience"||app.World.SkillHighlightCount!=expectedMatches)
            {Fail("hovering the skill does not highlight exactly the matching friendly creatures");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"17-skill-compatibility.png"));
            yield return new WaitForSecondsRealtime(.25f);
            app.Hud.CloseSkillDetails(); app.Hud.ShowOwnedSkills=true;
            app.VerificationPointer=app.Hud.OwnedSkillsScreenRect.center;
            yield return new WaitForSecondsRealtime(.3f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"18-owned-skills.png"));
            yield return new WaitForSecondsRealtime(.25f);
            app.Hud.ShowOwnedSkills=false;app.ShowShop=false;app.VerificationPointer=new Vector2(-200,-200);
            yield return new WaitForSecondsRealtime(.2f);
            if(app.World.SkillHighlightCount!=0){Fail("skill highlight persisted after pointer left the skill UI");yield break;}
            app.ShowShop=false;app.ShowBestiary=true;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"07-bestiary.png"));
            yield return new WaitForSecondsRealtime(.5f);
            app.ShowBestiary=false;
            if(!app.Me.ready)app.Send(new GameCommand{type="ready"});
            Debug.Log("ARENA_SMOKE_READY_SENT "+Describe(app));
            deadline=Time.realtimeSinceStartup+15;
            while(app.State.phase!="battle"&&Time.realtimeSinceStartup<deadline)yield return null;
            if(app.State.phase!="battle"){ScreenCapture.CaptureScreenshot(Path.Combine(folder,"failure.png"));Fail("battle transition failed: "+Describe(app));yield break;}
            int roundStartingGold=app.Me.gold-Mathf.Min(5,app.Me.roundKills)-app.Me.bountyIncome-app.Me.eventIncome;
            foreach(var player in app.State.players)combatBalances[player.id]=player.gold-Mathf.Min(5,player.roundKills)-player.bountyIncome-player.eventIncome;
            watchCombat=true;
            yield return new WaitForSecondsRealtime(2);
            if(app.Sound.CurrentMusic==menuTrack){Fail("battle music did not change from the preparation theme");yield break;}
            if(!app.Hud.RosterPinned||app.Hud.RosterVisibility<.94f||!app.Hud.Contains(app.Hud.RosterScreenRect.center))
            {Fail("battle commander roster was not pinned visible with pointer away");yield break;}
            if(VisibleBars(app)!=0){Fail("battle health bars visible without hover");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"04-battle.png"));
            yield return new WaitForSecondsRealtime(.15f);
            float previousSize=camera.orthographicSize;
            app.World.Zoom(2.5f,new Vector2(Screen.width*.5f,Screen.height*.5f));
            yield return new WaitForSecondsRealtime(.9f);
            if(camera.orthographicSize>=previousSize*.93f){Fail("mouse-wheel camera zoom did not magnify arena");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"08-battle-zoom.png"));
            yield return new WaitForSecondsRealtime(.25f);
            VoxelCreature inspected=null;
            var candidates=app.World.GetComponentsInChildren<VoxelCreature>();
            Array.Sort(candidates,(a,b)=>(a.Kind=="skeleton"?0:1).CompareTo(b.Kind=="skeleton"?0:1));
            foreach(var candidate in candidates)
            {
                if(candidate.Kind=="steve"||candidate.Dead||candidate.Slot==app.Me.slot)continue;
                Vector2 screen=camera.WorldToScreenPoint(candidate.transform.position+Vector3.up*(candidate.Height*candidate.Size*.5f));
                app.InspectPointer(screen);
                if(app.World.HoveredCreature!=null&&app.World.HoveredCreature.Slot!=app.Me.slot){inspected=app.World.HoveredCreature;break;}
            }
            if(inspected==null){Fail("enemy hover picking failed");yield break;}
            float inspectUntil=Time.unscaledTime+.6f;
            bool captured=false;
            while(Time.unscaledTime<inspectUntil)
            {
                app.VerificationPointer=camera.WorldToScreenPoint(inspected.transform.position+Vector3.up*(inspected.Height*inspected.Size*.5f));
                app.InspectPointer(app.VerificationPointer.Value);
                if(!captured&&Time.unscaledTime>inspectUntil-.35f)
                {
                    if(app.World.HoveredCreature==null||!app.World.HoveredCreature.HealthBarVisible||VisibleBars(app)!=1){Fail("hover must show exactly one creature health bar");yield break;}
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,"05-hover.png"));captured=true;
                }
                yield return null;
            }
            app.VerificationPointer=new Vector2(-200,-200);
            yield return null;
            if(VisibleBars(app)!=0){Fail("health bar persisted after mouse leave");yield break;}
            app.VerificationPointer=null;
            deadline=Time.realtimeSinceStartup+ArenaGame.BattleSeconds+8;
            while(app.State.phase=="battle"&&Time.realtimeSinceStartup<deadline)yield return null;
            watchCombat=false;
            if(incomeFailed)yield break;
            if(!realtimeIncomeObserved){Fail("combat did not exercise real-time kill income");yield break;}
            int expectedRankIncome=app.Me.lastRank==1?4:app.Me.lastRank==2?3:2;
            int expectedRoundIncome=6+Mathf.Min(5,app.Me.roundKills)+expectedRankIncome+app.Me.bountyIncome+app.Me.eventIncome;
            if(app.State.phase!="results"||app.Me.lastIncome!=expectedRoundIncome||app.Me.killIncome!=Mathf.Min(5,app.Me.roundKills)||app.Me.rankIncome!=expectedRankIncome||app.Me.gold!=roundStartingGold+app.Me.lastIncome)
            {Fail("native round settlement or income synchronization failed");yield break;}
            yield return null;
            int expectedHits=0;foreach(var placement in app.State.placements)if(placement.damage>0)expectedHits++;
            int hitCount=app.World.CommanderHitCount;
            if(hitCount!=expectedHits){Fail("commander hit count differs from authoritative damage: "+hitCount+" / "+expectedHits);yield break;}
            app.World.SetState(app.State,app.PlayerId);
            if(app.World.CommanderHitCount!=hitCount){Fail("duplicate snapshot replayed commander impact");yield break;}
            int coinCount=app.Hud.CoinFeedbackCount;
            app.Hud.ObserveState(app.State,app.State);
            if(coinCount==0||app.Hud.CoinFeedbackCount!=coinCount){Fail("income feedback missing or replayed for same snapshot");yield break;}
            Debug.Log("ARENA_IMPACT_ARMED time="+app.State.timeLeft+" hits="+hitCount+" active="+app.World.HasCommanderHitAnimation);
            yield return new WaitForSecondsRealtime(.7f);
            if(expectedHits>0&&!app.World.HasCommanderHitAnimation){Fail("commander impact ended before the animated feedback; phase="+app.State.phase+" time="+app.State.timeLeft+" hits="+app.World.CommanderHitCount);yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"13-ritual-rise.png"));
            deadline=Time.realtimeSinceStartup+5;
            while(app.World.RitualActive&&app.World.RitualAge<1.75f&&Time.realtimeSinceStartup<deadline)yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"14-crystal-attack.png"));
            while(app.World.RitualActive&&app.World.RitualImpactCount<expectedHits&&Time.realtimeSinceStartup<deadline)yield return null;
            if(app.World.RitualImpactCount!=expectedHits||app.World.RitualLaunchCount!=expectedHits)
            {Fail("crystal ritual failed to launch and land once per defeated commander");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"10-income-results.png"));
            deadline=Time.realtimeSinceStartup+10;
            while(app.State.phase=="results"&&Time.realtimeSinceStartup<deadline)yield return null;
            if(app.State.phase!="recruit"||!app.Me.recruitPending){Fail("next paid recruitment failed");yield break;}
            yield return new WaitForSecondsRealtime(.3f);
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"11-next-recruit.png"));
            yield return new WaitForSecondsRealtime(.4f);
            app.Leave();app.enabled=false;
            var gallery=new GameState{phase="battle",timeLeft=55};
            var curator=new PlayerState{id="gallery",name="图鉴",slot=0,hp=20,steve=new Point2(0,440)};gallery.players.Add(curator);
            for(int page=0;page<3;page++)
            {
                gallery.fighters.Clear();int start=page*30,end=Math.Min(start+30,CreatureCatalog.Kinds.Length);
                for(int i=start;i<end;i++)
                {
                    string kind=CreatureCatalog.Kinds[i];var f=ArenaGame.PreviewFighter(curator,new UnitState{id="gallery-"+i,kind=kind});
                    int cell=i-start;f.x=(cell%6-2.5f)*84;f.y=(cell/6-2)*95;gallery.fighters.Add(f);
                }
                app.World.ResetView();app.World.SetState(gallery,"gallery");
                app.World.Zoom(3,new Vector2(Screen.width*.5f,Screen.height*.5f));
                yield return new WaitForSecondsRealtime(1);
                int count=0;foreach(var v in app.World.GetComponentsInChildren<VoxelCreature>())if(v.Kind!="steve")count++;
                if(count!=end-start){Fail("bestiary model page "+page+" rendered "+count+" instead of "+(end-start));yield break;}
                ScreenCapture.CaptureScreenshot(Path.Combine(folder,"09-creatures-"+(page+1)+".png"));
                yield return new WaitForSecondsRealtime(.4f);
            }
            Debug.Log("ARENA_SMOKE_OK native host / recruit cards show all concise quantified innate effects / all 89 species including three-skill bosses fit recruit cards / paid recruit or skip / draggable skill-only market / captured input / screen clamp / saved window position / skill purchase and ownership / full skill species list and current army matches / friendly skill highlight clears on leave / real drag / 700ms stale snapshot / placement acknowledgement / instant creature stats and one-second concise skills / per-instance hover reset on switch and leave / commander roster edge reveal, panel hold, collapsed-space input pass-through and battle pinning / hidden bars / enemy hover / zoom / real-time kill income / settlement without duplicate gold / rising crystal pillar / sequential ritual launch and impact without replay / original music and 27 audio clips / audio controls / next recruitment / 89 creature models / screenshots");
            if(Array.IndexOf(args,"-smoke-exit")>=0)Application.Quit(0);
            else {app.VerificationInput=false;app.enabled=true;app.World.Clear();}
            Destroy(gameObject);
        }
        static Vector2 InspectionPoint(Camera camera,VoxelCreature creature)
        {return camera.WorldToScreenPoint(creature.transform.position+Vector3.up*(creature.Height*creature.Size*.5f));}
        static bool TooltipFits(Rect rect)
        {return rect.width>0&&rect.height>0&&rect.xMin>=-.5f&&rect.yMin>=-.5f&&rect.xMax<=Screen.width+.5f&&rect.yMax<=Screen.height+.5f;}
        static bool HasSkillExplanation(string text,CreatureDefinition creature)
        {
            if(string.IsNullOrEmpty(text))return false;
            bool quantified=false;foreach(char c in text)if(char.IsDigit(c)){quantified=true;break;}
            string effects=text;foreach(string id in creature.skills)effects=effects.Replace(CreatureAbilities.Get(id).name,"");
            return quantified&&effects.Trim().Length>=6;
        }
        static int VisibleBars(ArenaApp app){int count=0;foreach(var v in app.World.GetComponentsInChildren<VoxelCreature>())if(v.HealthBarVisible)count++;return count;}
        static string Describe(ArenaApp app){string value="connected="+app.Connected+" phase="+app.State.phase+" time="+app.State.timeLeft+" status="+app.Status;foreach(var p in app.State.players)value+=" | "+p.name+" ready="+p.ready+" pending="+p.recruitPending+" bot="+p.bot;return value;}
        static void Fail(string message){Debug.LogError("ARENA_SMOKE_FAILED "+message);Application.Quit(2);}
    }
}
