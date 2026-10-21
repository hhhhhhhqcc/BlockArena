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
    // Opt-in native-player regression. Every economy command travels through the real TCP session.
    public sealed class ArenaEconomyProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-economy-test")>=0)
                new GameObject("Recruitment and inspection verification").AddComponent<ArenaEconomyProbe>();
        }
        IEnumerator Start()
        {
            string[] args=Environment.GetCommandLineArgs();string folder=Path.Combine(Application.dataPath,"..","Captures","Economy");
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);ArenaApp app=ArenaApp.Instance;app.VerificationInput=true;
            app.VerificationPointer=new Vector2(-200,-200);yield return new WaitForSecondsRealtime(1.5f);
            if(WireProtocol.Version!=7){Fail("native player did not use protocol v7");yield break;}
            app.Host(true);float deadline=Time.unscaledTime+18;
            while((app.State==null||app.State.phase!="recruit")&&Time.unscaledTime<deadline)yield return null;
            if(app.State==null||app.State.phase!="recruit"||app.Me==null){Fail("native recruitment handshake failed: "+app.Status);yield break;}
            int balance=app.Me.gold,unitCount=app.Me.units.Count;string revision=app.Me.recruitOfferId,candidates=Signature(app.Me.shop);
            if(string.IsNullOrEmpty(revision)||unitCount!=2||app.Me.units.Exists(u=>u.paidPrice!=0)||app.State.roundEvent!=null)
            {Fail("starter pricing, private offer or removal of central event was incorrect");yield break;}
            app.Send(new GameCommand{type="recruit_refresh",offerId=revision});deadline=Time.unscaledTime+5;
            while(app.Me.recruitOfferId==revision&&Time.unscaledTime<deadline)yield return null;
            if(app.Me.recruitOfferId==revision||app.Me.gold!=balance-2||Signature(app.Me.shop)==candidates||!app.Me.recruitPending)
            {Fail("native refresh did not change revision and candidates for exactly two coins");yield break;}
            string freshRevision=app.Me.recruitOfferId;yield return new WaitForSecondsRealtime(.3f);yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"01-refreshed-recruitment.png"));yield return null;
            app.Status="";app.Send(new GameCommand{type="choose",index=0,offerId=revision});yield return WaitForError(app);
            if(string.IsNullOrEmpty(app.Status)||app.Me.gold!=balance-2||app.Me.units.Count!=unitCount||!app.Me.recruitPending||app.Me.recruitOfferId!=freshRevision)
            {Fail("stale recruitment changed the army, balance or current offer");yield break;}
            var foreign=app.State.players.Find(p=>p.id!=app.PlayerId&&p.units.Count>0);
            if(foreign==null){Fail("foreign army unavailable for ownership check");yield break;}
            string foreignId=foreign.units[0].id;app.Status="";app.Send(new GameCommand{type="dismiss",unitId=foreignId});yield return WaitForError(app);
            if(string.IsNullOrEmpty(app.Status)||app.Me.units.Count!=unitCount||app.Me.gold!=balance-2||!app.State.players.Exists(p=>p.units.Exists(u=>u.id==foreignId)))
            {Fail("foreign dismissal changed an army or refunded money");yield break;}
            app.Hud.SetRecruitVisible(false);yield return new WaitForSecondsRealtime(.25f);
            string starter=app.Me.units[0].id;VoxelCreature view=Find(app,starter);
            if(view==null||app.Hud.RecruitVisible||!app.CanArrangeCreatures){Fail("hidden recruitment did not return control to the real army");yield break;}
            Vector2 at=InspectPoint(app,view);
            if(app.Hud.Contains(at)||!app.Hud.Contains(app.Hud.RecruitRestoreScreenRect.center))
            {Fail("hidden recruitment blocks the army or leaves its restore button unprotected");yield break;}
            app.ProcessPlacementPointer(at,true,true,false);app.ProcessPlacementPointer(at,false,false,true);
            app.VerificationPointer=new Vector2(-200,-200);yield return new WaitForEndOfFrame();
            Rect panel=app.Hud.PinnedInspectionScreenRect,button=app.Hud.PinnedDismissScreenRect;
            if(app.Hud.PinnedInspectionId!=starter||!Fits(panel)||!Fits(button)||!panel.Contains(button.center)||!app.Hud.Contains(button.center)||app.Hud.ArmyManagementVisible)
            {Fail("creature click did not pin a valid protected dismissal panel without the old army window");yield break;}
            app.ProcessPlacementPointer(button.center,true,true,false);app.ProcessPlacementPointer(button.center,false,false,true);
            if(app.PlacementDragging||app.Me.units.Count!=unitCount){Fail("fixed-panel click leaked into world placement");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"02-hidden-recruit-pinned-dismissal.png"));yield return null;
            app.Hud.SetRecruitVisible(true);yield return new WaitForEndOfFrame();
            if(!app.Hud.RecruitVisible||!app.Hud.Contains(at)||app.Hud.PinnedInspectionScreenRect.width>0||app.Me.recruitOfferId!=freshRevision)
            {Fail("reopening recruitment lost the offer or failed to cover world and fixed stats");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"03-restored-recruitment.png"));yield return null;
            app.Hud.ToggleRecruit();app.Send(new GameCommand{type="dismiss",unitId=starter});deadline=Time.unscaledTime+4;
            while(app.Me.units.Count==unitCount&&Time.unscaledTime<deadline)yield return null;
            if(app.Me.units.Count!=1||app.Me.units.Exists(u=>u.id==starter)||app.Me.gold!=balance-2||!app.Me.recruitPending)
            {Fail("real TCP starter dismissal did not preserve one unit, pending recruitment and zero refund");yield break;}
            string retained=app.Me.units[0].id;app.Status="";app.Send(new GameCommand{type="dismiss",unitId=retained});yield return WaitForError(app);
            if(string.IsNullOrEmpty(app.Status)||app.Me.units.Count!=1||app.Me.units[0].id!=retained||app.Me.gold!=balance-2)
            {Fail("last-unit dismissal was accepted or refunded money");yield break;}
            app.Hud.ClosePinnedInspection();view=Find(app,retained);
            if(view==null||!app.PinCreatureAt(InspectPoint(app,view))){Fail("last retained creature could not be pinned from its real model");yield break;}
            yield return new WaitForEndOfFrame();
            if(!Fits(app.Hud.PinnedInspectionScreenRect)||!app.Hud.Contains(app.Hud.PinnedDismissScreenRect.center))
            {Fail("last retained creature lacks protected fixed stats");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"04-retained-last-creature.png"));yield return null;
            app.Send(new GameCommand{type="skip"});deadline=Time.unscaledTime+6;
            while(app.Me.recruitPending&&Time.unscaledTime<deadline)yield return null;
            if(app.Me.recruitPending||app.Hud.RecruitVisible||app.State.roundEvent!=null||app.Hud.EventPreviewVisible)
            {Fail("skipping recruitment left a recruit overlay or revived the removed central event");yield break;}
            app.Hud.ClosePinnedInspection();app.Send(new GameCommand{type="ready"});deadline=Time.unscaledTime+20;
            while((app.State.battlefield==null||app.State.phase!="reveal")&&Time.unscaledTime<deadline)yield return null;
            if(app.State.battlefield==null||app.State.roundEvent!=null||app.State.battlefield.objectives.Count==0)
            {Fail("real ready flow did not generate fixed map objectives without a central guard event");yield break;}
            while(app.State.phase=="reveal"&&app.State.revealStage!="climate"&&Time.unscaledTime<deadline)yield return null;
            yield return new WaitForSecondsRealtime(.5f);yield return new WaitForEndOfFrame();
            if(app.State.roundEvent!=null||app.World.ActiveNeutralEventCount!=0||app.World.GoldResourceVisible||app.Hud.EventPreviewVisible||app.World.FieldObjectiveCount==0)
            {Fail("map reveal introduced an old neutral event or omitted its native objectives");yield break;}
            ScreenCapture.CaptureScreenshot(Path.Combine(folder,"05-native-map-objectives.png"));yield return new WaitForSecondsRealtime(.2f);
            Debug.Log("ARENA_ECONOMY_OK protocol7 / real TCP refresh revision and cost / stale choice rejection / foreign dismissal rejection / recruit hide and restore / actual creature click pins protected stats and dismissal layout / zero-refund starter dismissal / final creature retained / skip / natural map objectives without central guard event / captures5");
            if(Array.IndexOf(args,"-smoke-exit")>=0)Application.Quit(0);
            else{app.Leave();app.VerificationInput=false;app.VerificationPointer=null;Destroy(gameObject);}
        }
        static IEnumerator WaitForError(ArenaApp app){float deadline=Time.unscaledTime+4;while(string.IsNullOrEmpty(app.Status)&&Time.unscaledTime<deadline)yield return null;yield return new WaitForSecondsRealtime(.15f);}
        static VoxelCreature Find(ArenaApp app,string id){foreach(var view in app.World.GetComponentsInChildren<VoxelCreature>())if(view.UnitId==id)return view;return null;}
        static Vector2 InspectPoint(ArenaApp app,VoxelCreature view){return app.World.ViewCamera.WorldToScreenPoint(view.transform.position+Vector3.up*(view.Height*view.Size*.5f));}
        static bool Fits(Rect r){return r.width>0&&r.height>0&&r.xMin>=-.5f&&r.yMin>=-.5f&&r.xMax<=Screen.width+.5f&&r.yMax<=Screen.height+.5f;}
        static string Signature(string[] choices){var list=new List<string>(choices);list.Sort(StringComparer.Ordinal);return string.Join(",",list.ToArray());}
        static void Fail(string message){Debug.LogError("ARENA_ECONOMY_FAILED "+message);Application.Quit(2);}
    }
}
