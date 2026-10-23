using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using BlockArena.Core;
using BlockArena.Networking;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Shipping snapshots and exact native picks, without a fullscreen vision mask.
    public sealed class ArenaVisionProbe : MonoBehaviour
    {
        [Serializable] sealed class Report {
            public bool success;public string error="";public int width,height;
            public List<string> checks=new List<string>();public List<string> captures=new List<string>();
        }
        readonly Report report=new Report();
        ArenaApp app;ArenaGame authority;string folder;bool failed;
        FighterState ally,concealedEnemy,ordinaryEnemy;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartRequested() {
            var args=Environment.GetCommandLineArgs();
            if(Array.IndexOf(args,"-vision-test")>=0||Array.IndexOf(args,"-concealment-test")>=0)
                new GameObject("Native local concealment verification").AddComponent<ArenaVisionProbe>();
        }
        IEnumerator Start() {
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0){var iterator=stack.Peek();bool moved=false;object next=null;
                try{moved=iterator.MoveNext();if(moved)next=iterator.Current;}catch(Exception error){Fail(error.ToString());}
                if(failed)yield break;if(!moved){stack.Pop();continue;}if(next is IEnumerator){stack.Push((IEnumerator)next);continue;}yield return next;
            }
        }
        IEnumerator Run() {
            folder=Path.Combine(Application.dataPath,"..","Captures","Concealment");
            var args=Environment.GetCommandLineArgs();for(int i=0;i+1<args.Length;i++)if(args[i]=="-capture-dir")folder=args[i+1];
            Directory.CreateDirectory(folder);report.width=Screen.width;report.height=Screen.height;
            yield return new WaitForSecondsRealtime(.4f);app=ArenaApp.Instance;
            Require(app!=null&&app.World!=null,"native concealment world initialized");
            app.VerificationInput=true;app.VerificationPointer=new Vector2(-1000,-1000);
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();app.PortText=((IPEndPoint)listener.LocalEndpoint).Port.ToString();listener.Stop();
            app.Host(false);float deadline=Time.realtimeSinceStartup+12;
            while(!app.Connected&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(app.Connected,"real local TCP session permits production inspection");
            authority=new ArenaGame(app.PlayerId,"隐蔽军团",194);authority.AddPlayer("concealment-enemy","远端军团");
            authority.State.phase="battle";authority.State.round=18;authority.State.timeLeft=120;
            authority.State.battlefield=BattlefieldCatalog.CreateExpanded("courtyards","clear",194);
            authority.State.players[0].steve=new Point2(-600,650);authority.State.players[1].steve=new Point2(600,-650);
            ally=Fighter("concealment-ally",app.PlayerId,0,"skeleton",-160,620);
            ordinaryEnemy=Fighter("ordinary-far","concealment-enemy",1,"skeleton",650,-600);
            concealedEnemy=Fighter("hidden-enemy","concealment-enemy",1,"skeleton",650,600);
            concealedEnemy.hidden=true;concealedEnemy.concealmentSources=1;
            authority.State.fighters.Add(ally);authority.State.fighters.Add(ordinaryEnemy);authority.State.fighters.Add(concealedEnemy);
            authority.State.players[1].units.Add(new UnitState{id=concealedEnemy.unitId,kind=concealedEnemy.kind,x=concealedEnemy.x,y=concealedEnemy.y});
            Show();app.World.ResetView();yield return Settle(1.6f);
            Require(!app.World.VisionFogActive&&app.World.VisionMask==null&&app.World.ViewCamera.GetComponent<ArenaVisionFog>()==null,"camera has no fullscreen army fog component or sight texture");
            Require(app.State.fighters.Exists(f=>f.id==ordinaryEnemy.id)&&Visual(ordinaryEnemy.unitId)!=null,"ordinary enemy remains visible beyond army targeting sight");
            Require(!app.State.fighters.Exists(f=>f.id==concealedEnemy.id)&&Visual(concealedEnemy.unitId)==null,"undiscovered hidden enemy has no JSON snapshot or active model");
            Require(app.World.DiscoveryFeedbackCount==0&&app.World.ActiveDiscoveryPulseCount==0,"first snapshot establishes a silent baseline");
            Texture2D terrainBaseline=ScreenCapture.CaptureScreenshotAsTexture();Save(terrainBaseline,"01-whole-arena-visible");
            ally.hidden=true;ally.concealmentSources=7;
            authority.State.battlefield.zones.Add(new CommandZoneState{id="probe-smoke",kind="smoke",ownerId=app.PlayerId,slot=0,x=ally.x,y=ally.y,radius=115,remaining=5});
            Show();yield return Settle(.35f);
            var own=Visual(ally.unitId);Require(own!=null&&own.FriendlyConcealed,"authoritative friendly hidden flag reaches its active model");
            Require(own.ConcealmentVisibility>.7f&&own.ConcealmentVisibility<.85f&&own.ConcealmentIconVisible&&own.TeamFootprintVisible,"concealed friendly retains its team circle and icon with mild voxel fading");
            Require(app.World.ConcealmentSmokePartCount==9,"smoke uses a bounded local ground cloud group");
            var transported=app.State.fighters.Find(f=>f.id==ally.id);
            Require(transported.concealmentSources==7&&transported.hidden,"all three concealment source bits survive SnapshotFor and shipping JSON codec");
            app.Hud.PinInspection(CreatureCatalog.Get(ally.kind),transported,null,app.Me,Vector2.zero);yield return Settle(.15f);
            string text=app.Hud.PinnedConcealmentText;
            Require(text.Contains("树林")&&text.Contains("烟幕")&&text.Contains("伏击")&&!text.Contains("攻击暴露"),"fixed inspection lists real stacked concealment sources without inventing attack exposure");
            Require(Fits(app.Hud.PinnedInspectionScreenRect)&&Inside(app.Hud.PinnedInspectionScreenRect,app.Hud.PinnedInspectionSkillsScreenRect),"concealment status leaves an on-screen independent skill viewport");
            Capture("02-friendly-concealment-and-sources");app.Hud.ClosePinnedInspection();
            Texture2D terrainConcealed=ScreenCapture.CaptureScreenshotAsTexture();CompareDistantGround(terrainBaseline,terrainConcealed);Destroy(terrainBaseline);Destroy(terrainConcealed);
            concealedEnemy.x=ally.x+65;concealedEnemy.y=ally.y;Show();yield return Settle(.18f);
            Require(app.State.fighters.Exists(f=>f.id==concealedEnemy.id)&&Visual(concealedEnemy.unitId)!=null,"actual close discovery includes the hidden enemy in the shipping snapshot");
            Require(!Visual(concealedEnemy.unitId).FriendlyConcealed&&Visual(concealedEnemy.unitId).ConcealmentVisibility>.99f,"discovered enemy is solid rather than using friendly concealment fading");
            Require(app.World.DiscoveryFeedbackCount==1&&app.World.ActiveDiscoveryPulseCount==1,"actual first discovery produces one lightweight eye and ground pulse");
            for(int i=0;i<6;i++)Show();
            Require(app.World.DiscoveryFeedbackCount==1,"repeated equivalent JSON snapshots do not replay discovery");
            Capture("03-hidden-enemy-discovered");
            Vector2 enemyPixel=Vector2.zero;bool found=false;
            for(int turn=0;turn<4&&!found;turn++){
                found=FindEnemyPixel(concealedEnemy.unitId,out enemyPixel);
                if(!found){app.World.RotateView(1);yield return Settle(.8f);}
            }
            Require(found,"discovered enemy has a genuine visible triangle available for production pointer picking");
            app.VerificationPointer=enemyPixel;app.InspectPointer(enemyPixel);
            Require(app.World.HoveredCreature!=null&&app.World.HoveredCreature.UnitId==concealedEnemy.unitId,"production hover can inspect a discovered concealed enemy");
            Require(app.PinCreatureAt(enemyPixel),"production click can fix a discovered enemy inspection");
            yield return Settle(.1f);Require(app.Hud.PinnedInspectionId==concealedEnemy.id,"fixed inspection retains the authoritative fighter identity");
            concealedEnemy.x=650;concealedEnemy.y=600;Show();
            Require(app.World.ActiveDiscoveryPulseCount==0,"the same hidden snapshot immediately removes the enemy discovery eye and footprint without a lingering position cue");
            yield return Settle(.2f);
            Rect gone=app.Hud.PinnedInspectionScreenRect;
            Require(app.Hud.PinnedInspectionId==null&&gone==Rect.zero,"enemy disappearing into concealment closes fixed inspection without roster-position fallback");
            Require(Visual(concealedEnemy.unitId)==null&&app.World.HoveredCreature==null&&!app.Hud.InspectionExpanded,"hidden enemy leaves no model, hover text or selected-body residue");
            Capture("04-enemy-conceals-again");
            yield return Settle(1.25f);concealedEnemy.x=ally.x+65;concealedEnemy.y=ally.y;Show();yield return Settle(.15f);
            Require(app.World.DiscoveryFeedbackCount==2,"a later real conceal-to-visible transition can produce one fresh discovery pulse");
            Show();Require(app.World.DiscoveryFeedbackCount==2,"the second transition is also deduplicated");
            int discoveries=app.World.DiscoveryFeedbackCount;
            authority.State.fighters.Add(Fighter("normal-new",ordinaryEnemy.ownerId,1,"zombie",ally.x+140,ally.y));
            var summon=Fighter("summoned-hidden",ordinaryEnemy.ownerId,1,"skeleton",ally.x+50,ally.y+20);summon.summoned=true;summon.hidden=true;summon.concealmentSources=2;authority.State.fighters.Add(summon);
            Show();Require(app.World.DiscoveryFeedbackCount==discoveries,"ordinary enemy arrivals and concealed summons do not create birth announcements");
            ally.hidden=false;ally.attackRevealUntil=authority.State.battlefield.elapsed+1.7f;Show();yield return Settle(.3f);
            transported=app.State.fighters.Find(f=>f.id==ally.id);
            app.Hud.PinInspection(CreatureCatalog.Get(ally.kind),transported,null,app.Me,Vector2.zero);yield return Settle(.1f);
            Require(!Visual(ally.unitId).FriendlyConcealed&&!Visual(ally.unitId).ConcealmentIconVisible&&Visual(ally.unitId).ConcealmentVisibility>.99f,"attacking restores the friendly body and removes its hidden icon");
            Require(Mathf.Abs(transported.attackRevealRemaining-1.7f)<.001f&&app.Hud.PinnedConcealmentText.Contains("攻击暴露 1.7 秒"),"independent attack exposure seconds survive actual JSON and appear in inspection");
            Require(app.Hud.PinnedConcealmentText.Contains("树林")&&app.Hud.PinnedConcealmentText.Contains("烟幕")&&app.Hud.PinnedConcealmentText.Contains("伏击"),"attack exposure retains the reasons concealment may resume");
            Capture("05-attack-exposure-countdown");
            ally.attackRevealUntil=authority.State.battlefield.elapsed+.4f;Show();Require(app.Hud.PinnedConcealmentText.Contains("攻击暴露 0.4 秒"),"exposure timer refreshes from the latest authoritative snapshot");
            ally.attackRevealUntil=authority.State.battlefield.elapsed;ally.concealmentSources=4;Show();Require(!app.Hud.PinnedConcealmentText.Contains("攻击暴露"),"ambush progression alone is never labelled as an attack reveal");
            ally.concealmentSources=0;Show();Require(app.Hud.PinnedConcealmentText=="","ordinary fighter inspection removes stale concealment text");app.Hud.ClosePinnedInspection();
            for(int i=0;i<20;i++)authority.State.battlefield.terrain.Add(new TerrainState{id="hint-budget-"+i,kind="forest",x=0,y=0,halfWidth=20,halfHeight=20});
            Show();Require(app.World.ConcealmentRegionCount==16&&app.World.ConcealmentSmokePartCount==9,"all gameplay smoke is allocated before decorative forest hints exhaust the fixed 16-region budget");
            authority.State.battlefield.weather="fog";Show();yield return Settle(.7f);
            Require(!app.World.VisionFogActive&&app.World.WeatherParticleCount>0,"thick fog retains atmospheric particles without fullscreen black army shading");
            Capture("06-local-smoke-and-weather-mist");
            app.World.Clear();Show();yield return Settle(.2f);
            Require(app.World.DiscoveryFeedbackCount==0&&app.World.ActiveDiscoveryPulseCount==0,"reconnect-style scene baseline does not replay already visible hidden enemies");
            ally.hidden=true;ally.concealmentSources=1;ally.dead=true;Show();yield return Settle(.2f);
            own=Visual(ally.unitId);Require(own==null||!own.FriendlyConcealed&&!own.ConcealmentIconVisible,"death clears friendly concealment icon and material state");
            authority.State.phase="results";Show();yield return Settle(.2f);
            Require(app.World.ConcealmentRegionCount==0&&app.World.ActiveDiscoveryPulseCount==0&&!app.World.VisionFogActive,"settlement clears local concealment cues and never darkens the crystal ceremony");
            app.VerificationState=null;app.Leave();yield return Settle(.1f);
            Require(app.World.ConcealmentRegionCount==0&&app.World.DiscoveryFeedbackCount==0,"leaving clears concealment observations and pooled cues");
            report.success=true;WriteReport();Debug.Log("ARENA_VISION_OK concealment "+report.checks.Count+" checks / "+report.captures.Count+" captures");Debug.Log("ARENA_CONCEALMENT_OK");Application.Quit(0);
        }
        FighterState Fighter(string id,string owner,int slot,string kind,float x,float y) {
            return new FighterState{id=id,unitId="unit-"+id,kind=kind,ownerId=owner,slot=slot,x=x,y=y,hp=45,maxHp=45,attack=8,attackSpeed=1,range=145,speed=40};
        }
        void Show() {
            var codec=new UnityWireCodec();app.VerificationState=codec.Decode(codec.Encode(new WirePacket{state=authority.SnapshotFor(app.PlayerId)})).state;
            app.ShowRoom=app.ShowShop=app.ShowBestiary=app.ShowGameMenu=false;app.VerificationPointer=new Vector2(-1000,-1000);app.World.SetState(app.State,app.PlayerId);
        }
        VoxelCreature Visual(string unit) {foreach(var view in app.World.GetComponentsInChildren<VoxelCreature>())if(view.Kind!="steve"&&view.Battle&&view.UnitId==unit&&view.gameObject.activeInHierarchy)return view;return null;}
        bool FindEnemyPixel(string unit,out Vector2 pixel) {
            var view=Visual(unit);pixel=Vector2.zero;if(view==null)return false;
            Bounds bounds;if(!app.World.TryGetBodyBounds(view,out bounds))return false;
            Vector3 projected=app.World.ViewCamera.WorldToScreenPoint(bounds.center);
            for(int distance=0;distance<=20;distance+=2)for(int y=-distance;y<=distance;y+=2)for(int x=-distance;x<=distance;x+=2){
                Vector2 p=new Vector2(projected.x+x,projected.y+y);if(p.x<5||p.y<5||p.x>Screen.width-5||p.y>Screen.height-5||app.Hud.Contains(p))continue;
                VoxelCreature picked;FighterState fighter;if(app.World.TryInspectCreature(p,out picked,out fighter)&&picked==view){pixel=p;return true;}
            }
            return false;
        }
        void CompareDistantGround(Texture2D before,Texture2D after) {
            int unchanged=0;
            for(int x=-700;x<=700;x+=90)for(int y=-700;y<=700;y+=90){
                float dx=x-ally.x,dy=y-ally.y;if(dx*dx+dy*dy<600*600)continue;
                Vector3 at=app.World.WorldSurfacePoint(x,y),screen=app.World.ViewCamera.WorldToScreenPoint(at);
                Vector2 pixel=screen;if(screen.z<=0||pixel.x<8||pixel.y<8||pixel.x>Screen.width-8||pixel.y>Screen.height-8||app.Hud.Contains(pixel)||!app.World.IsTacticalPointVisible(at))continue;
                float a=before.GetPixel(Mathf.RoundToInt(pixel.x),Mathf.RoundToInt(pixel.y)).grayscale,b=after.GetPixel(Mathf.RoundToInt(pixel.x),Mathf.RoundToInt(pixel.y)).grayscale;
                if(a>.05f&&b>a*.8f)unchanged++;
            }
            Require(unchanged>=8,"at least eight real distant terrain pixels remain lit while friendly concealment changes ("+unchanged+")");
        }
        static bool Fits(Rect r){return r.width>0&&r.height>0&&r.x>=-.5f&&r.y>=-.5f&&r.xMax<=Screen.width+.5f&&r.yMax<=Screen.height+.5f;}
        static bool Inside(Rect outer,Rect inner){return inner.width>0&&inner.height>0&&outer.Contains(inner.min+Vector2.one)&&outer.Contains(inner.max-Vector2.one);}
        IEnumerator Settle(float seconds){yield return new WaitForSecondsRealtime(seconds);yield return new WaitForEndOfFrame();}
        void Capture(string name){var texture=ScreenCapture.CaptureScreenshotAsTexture();Save(texture,name);Destroy(texture);}
        void Save(Texture2D texture,string name) {
            int samples=0,pink=0,lit=0;for(int y=12;y<texture.height;y+=19)for(int x=12;x<texture.width;x+=19){Color c=texture.GetPixel(x,y);samples++;if(c.grayscale>.02f)lit++;if(c.r>.85f&&c.b>.85f&&c.g<.12f)pink++;}
            Require(lit>samples*.45f,"capture "+name+" contains rendered scene pixels");Require(pink<samples*.02f,"capture "+name+" has no missing shaders");
            File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());report.captures.Add(name+".png");
        }
        void Require(bool condition,string description){if(!condition)throw new InvalidOperationException(description);report.checks.Add(description);}
        void WriteReport(){if(folder!=null)File.WriteAllText(Path.Combine(folder,"verification.json"),JsonUtility.ToJson(report,true));}
        void Fail(string error){failed=true;report.error=error;WriteReport();if(app!=null)app.Leave();Debug.LogError("ARENA_VISION_FAILED "+error);Application.Quit(2);}
    }
}
