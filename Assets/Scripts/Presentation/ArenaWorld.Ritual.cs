using UnityEngine;
using BlockArena.Core;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        const float RitualRiseSeconds=1.2f, RitualChargeSeconds=.3f, RitualFlightSeconds=.45f;
        const float RitualShotSpacing=.32f, RitualDuration=4.5f;
        Transform ritualRoot,ritualColumn,ritualCrystal,ritualCage,ritualCore,ritualRing;
        readonly Transform[] ritualShards=new Transform[5];
        Light ritualLight;
        bool ritualActive,cameraBaseValid;
        float ritualStartedAt,ritualAge,nextRitualParticle,shakeStartedAt=-10,shakeMagnitude;
        int ritualLaunchCount,ritualImpactCount;
        Vector3 cameraBasePosition;
        Quaternion cameraBaseRotation;

        internal bool RitualActive { get { return ritualActive; } }
        internal float RitualAge { get { return ritualAge; } }
        internal int RitualLaunchCount { get { return ritualLaunchCount; } }
        internal int RitualImpactCount { get { return ritualImpactCount; } }
        internal float CameraShakeStrength
        {
            get { float remaining=1-Mathf.Clamp01((Time.unscaledTime-shakeStartedAt)/.3f);return shakeMagnitude*remaining*remaining; }
        }
        internal bool CommanderImpactPending(string id)
        {
            string key="steve:"+id;
            for(int i=0;i<commanderImpacts.Count;i++)if(commanderImpacts[i].key==key&&!commanderImpacts[i].struck)return true;
            return false;
        }

        void EnsureRitualGeometry()
        {
            if(ritualRoot!=null)return;
            ritualRoot=NewRoot("End crystal judgment ceremony",effectRoot);
            ritualColumn=NewRoot("Rising obsidian pillar",ritualRoot);
            VoxelAssets.Part(ritualColumn,"Obsidian masonry",VoxelAssets.Cached("ritual/obsidian-pillar",b=>{
                Color dark=VoxelAssets.Hex("#1B1829"),edge=VoxelAssets.Hex("#343047"),facet=VoxelAssets.Hex("#29213C");
                b.Box(new Vector3(0,.16f,0),new Vector3(2.65f,.32f,2.65f),edge);
                b.Box(new Vector3(0,.42f,0),new Vector3(2.27f,.25f,2.27f),dark);
                for(int layer=0;layer<7;layer++){
                    float y=.85f+layer*.49f;
                    b.Box(new Vector3(0,y,0),new Vector3(1.74f,.46f,1.74f),layer%2==0?dark:facet);
                    for(int side=0;side<4;side++){
                        Quaternion q=Quaternion.Euler(0,side*90,0);
                        b.Box(q*new Vector3((layer%3-1)*.34f,y,-.886f),new Vector3(.53f,.13f,.035f),edge,q);
                        if((side+layer)%3==0)b.Box(q*new Vector3(.39f,y+.14f,-.887f),new Vector3(.2f,.08f,.04f),VoxelAssets.Hex("#473457"),q);
                    }
                }
                b.Box(new Vector3(0,4.19f,0),new Vector3(2.18f,.23f,2.18f),edge);
                b.Box(new Vector3(0,4.35f,0),new Vector3(1.97f,.12f,1.97f),dark);
            }),Vector3.zero);
            VoxelAssets.Part(ritualColumn,"Etched violet runes",VoxelAssets.Cached("ritual/pillar-runes",b=>{
                Color c=VoxelAssets.Hex("#AA76D6");
                for(int side=0;side<4;side++){
                    Quaternion q=Quaternion.Euler(0,side*90,0);
                    for(int i=0;i<5;i++){
                        Vector3 p=new Vector3((i%2==0?-.1f:.1f),1.05f+i*.52f,-.897f);
                        b.Box(q*p,new Vector3(.055f,.23f,.03f),c,q);
                        b.Box(q*(p+Vector3.right*.08f),new Vector3(.16f,.045f,.03f),c,q);
                    }
                }
            }),Vector3.zero,VoxelAssets.Glow,false);
            ritualCrystal=NewRoot("Floating end crystal",ritualColumn);
            ritualCrystal.localPosition=new Vector3(0,5.35f,0);
            ritualCore=VoxelAssets.Part(ritualCrystal,"Pink crystal heart",VoxelAssets.Cached("ritual/crystal-heart",b=>{
                b.Box(Vector3.zero,Vector3.one*.74f,VoxelAssets.Hex("#E672CF"));
                b.Box(Vector3.zero,new Vector3(.4f,1.05f,.4f),VoxelAssets.Hex("#FFD3F3"));
                b.Box(Vector3.zero,new Vector3(1.05f,.4f,.4f),VoxelAssets.Hex("#F7A1DF"));
                b.Box(Vector3.zero,new Vector3(.4f,.4f,1.05f),VoxelAssets.Hex("#F7A1DF"));
            }),Vector3.zero,VoxelAssets.Glow,false);
            ritualCage=VoxelAssets.Part(ritualCrystal,"Spinning crystal lattice",VoxelAssets.Cached("ritual/crystal-lattice",b=>{
                Color c=VoxelAssets.Hex("#C9B8E8");float r=.9f;
                for(int a=-1;a<=1;a+=2)for(int z=-1;z<=1;z+=2){
                    b.Box(new Vector3(0,a*r,z*r),new Vector3(r*2,.065f,.065f),c);
                    b.Box(new Vector3(a*r,0,z*r),new Vector3(.065f,r*2,.065f),c);
                    b.Box(new Vector3(a*r,z*r,0),new Vector3(.065f,.065f,r*2),c);
                }
            }),Vector3.zero,VoxelAssets.Glow,false);
            for(int i=0;i<ritualShards.Length;i++)ritualShards[i]=VoxelAssets.Part(ritualColumn,"Orbiting crystal shard "+i,VoxelAssets.Cached("ritual/orbit-shard",b=>{
                b.Box(Vector3.zero,new Vector3(.14f,.32f,.14f),VoxelAssets.Hex("#EDB5EF"),Quaternion.Euler(0,0,38));
            }),Vector3.zero,VoxelAssets.Glow,false);
            ritualRing=VoxelAssets.Part(ritualRoot,"Ground summoning sigil",VoxelAssets.Cached("ritual/ground-sigil",b=>{
                Color c=VoxelAssets.Hex("#B589CF");b.Ring(2.65f,2.57f,c);b.Ring(2.18f,2.13f,VoxelAssets.Hex("#E6B2DF"));
                for(int i=0;i<8;i++){
                    Quaternion q=Quaternion.Euler(0,i*45,0);
                    b.Box(q*new Vector3(0,0,2.38f),new Vector3(.13f,.045f,.28f),c,q);
                    b.Box(q*new Vector3(0,0,2.83f),new Vector3(.26f,.045f,.1f),c,q);
                }
            }),new Vector3(0,.085f,0),VoxelAssets.Glow,false);
            GameObject lamp=new GameObject("End crystal glow");lamp.transform.SetParent(ritualCrystal,false);
            ritualLight=lamp.AddComponent<Light>();ritualLight.type=LightType.Point;ritualLight.color=VoxelAssets.Hex("#DA89EB");
            ritualLight.range=7;ritualLight.intensity=.5f;ritualLight.shadows=LightShadows.None;
            ritualRoot.gameObject.SetActive(false);
        }

        void StartCommanderImpact(PlayerState player,int damage)
        {
            string key="steve:"+player.id;VoxelCreature steve;
            if(!creatures.TryGetValue(key,out steve))return;
            if(!ritualActive){
                EnsureRitualGeometry();ritualActive=true;ritualStartedAt=Time.unscaledTime;ritualAge=nextRitualParticle=0;
                ritualLaunchCount=ritualImpactCount=0;ritualRoot.gameObject.SetActive(true);ritualColumn.localPosition=Vector3.down*7;
                if(SoundCue!=null)SoundCue("ritual_rise",Vector3.zero,.65f);
            }
            float chargeAt=RitualRiseSeconds+commanderImpacts.Count*RitualShotSpacing;
            commanderImpacts.Add(new CommanderImpact{key=key,damage=damage,chargeAt=chargeAt,launchAt=chargeAt+RitualChargeSeconds,hitAt=chargeAt+RitualChargeSeconds+RitualFlightSeconds});
            commanderHitCount++;
        }

        void UpdateCommanderImpacts()
        {
            if(!ritualActive)return;
            // Absolute unscaled time avoids animation speed changing with frame rate or a stalled frame.
            ritualAge=Mathf.Max(0,Time.unscaledTime-ritualStartedAt);
            float rise=Mathf.SmoothStep(0,1,Mathf.Clamp01(ritualAge/RitualRiseSeconds));
            float sink=Mathf.SmoothStep(0,1,Mathf.Clamp01((ritualAge-3.75f)/.75f));
            ritualColumn.localPosition=Vector3.down*7*(1-rise+sink);
            ritualCrystal.localPosition=new Vector3(0,5.35f+Mathf.Sin(ritualAge*3.8f)*.12f,0);
            ritualCore.localRotation=Quaternion.Euler(45+Mathf.Sin(ritualAge)*12,ritualAge*78,45);
            ritualCage.localRotation=Quaternion.Euler(ritualAge*24,45-ritualAge*32,ritualAge*17);
            float charge=0;
            for(int i=0;i<commanderImpacts.Count;i++){
                CommanderImpact impact=commanderImpacts[i];VoxelCreature steve;
                if(!creatures.TryGetValue(impact.key,out steve))continue;
                float strength=Mathf.Clamp01(.2f+Mathf.Sqrt(Mathf.Clamp01(impact.damage/20f))*.8f);
                if(!impact.charged&&ritualAge>=impact.chargeAt){impact.charged=true;if(SoundCue!=null)SoundCue("ritual_charge",ritualCrystal.position,strength);}
                if(ritualAge>=impact.chargeAt&&ritualAge<impact.launchAt)charge=Mathf.Max(charge,Mathf.Clamp01((ritualAge-impact.chargeAt)/RitualChargeSeconds));
                if(!impact.launched&&ritualAge>=impact.launchAt){
                    impact.launched=true;ritualLaunchCount++;LaunchRitualBolt(impact,steve);
                    if(SoundCue!=null)SoundCue("ritual_launch",ritualCrystal.position,strength);
                }
                if(!impact.struck&&ritualAge>=impact.hitAt){
                    impact.struck=true;ritualImpactCount++;StrikeCommander(impact,steve,strength);
                }
            }
            ritualCore.localScale=Vector3.one*(.87f+charge*.36f+Mathf.Sin(ritualAge*6)*.035f);
            ritualCage.localScale=Vector3.one*(1+charge*.12f);
            float ringSize=(.45f+rise*.55f)*(1-sink)*(1+Mathf.Sin(ritualAge*7)*.025f);
            ritualRing.localScale=new Vector3(ringSize,1,ringSize);ritualRing.localRotation=Quaternion.Euler(0,ritualAge*10,0);
            ritualLight.intensity=(.45f+charge*2.1f)*(1-sink);
            for(int i=0;i<ritualShards.Length;i++){
                float angle=ritualAge*(1.4f+charge*2)+i*Mathf.PI*2/ritualShards.Length;
                ritualShards[i].localPosition=new Vector3(Mathf.Cos(angle)*(1.45f-charge*.35f),5.35f+Mathf.Sin(angle*2)*.35f,Mathf.Sin(angle)*(1.45f-charge*.35f));
                ritualShards[i].localRotation=Quaternion.Euler(25,angle*Mathf.Rad2Deg,35);
            }
            if(ritualAge>=nextRitualParticle){
                nextRitualParticle=ritualAge+.05f;
                if(ritualAge<RitualRiseSeconds){
                    for(int i=0;i<3;i++){
                        float angle=ritualAge*12+i*2.094f;Vector3 edge=new Vector3(Mathf.Cos(angle)*1.65f,.15f,Mathf.Sin(angle)*1.65f);
                        EmitRitualParticle(edge,Vector3.up*1.5f,VoxelAssets.Hex("#6B5878"),.15f,.55f);
                    }
                }
                if(charge>0){
                    Vector3 offset=new Vector3(Mathf.Cos(ritualAge*28),Mathf.Sin(ritualAge*37)*.6f,Mathf.Sin(ritualAge*28))*1.35f;
                    EmitRitualParticle(ritualCrystal.position+offset,-offset*3,VoxelAssets.Hex("#FBC5F2"),.13f,.32f);
                }
            }
            if(ritualAge>=RitualDuration){ritualActive=false;ritualRoot.gameObject.SetActive(false);commanderImpacts.Clear();}
        }

        void LaunchRitualBolt(CommanderImpact impact,VoxelCreature steve)
        {
            FlyingEffect bolt=EffectObject();bolt.start=ritualCrystal.position;bolt.end=steve.Target+Vector3.up*1.15f;
            bolt.targetKey=impact.key;bolt.duration=RitualFlightSeconds;bolt.soul=true;bolt.size=1+Mathf.Sqrt(impact.damage/20f)*.3f;
            bolt.filter.sharedMesh=VoxelAssets.Cached("ritual/judgment-bolt",b=>{
                b.Box(Vector3.zero,new Vector3(.24f,.24f,.66f),VoxelAssets.Hex("#FFD9F6"),Quaternion.Euler(0,0,45));
                b.Box(new Vector3(0,0,-.42f),new Vector3(.16f,.16f,.34f),VoxelAssets.Hex("#D58DED"));
                b.Box(new Vector3(0,0,-.72f),new Vector3(.08f,.08f,.22f),VoxelAssets.Hex("#B7A6F6"));
            });
            bolt.transform.position=bolt.start;
        }

        void StrikeCommander(CommanderImpact impact,VoxelCreature steve,float strength)
        {
            steve.PlayDefeatImpact(strength);Vector3 at=steve.Target+Vector3.up*1.15f;
            for(int p=0;p<20;p++){
                float angle=p*Mathf.PI*2/20;
                Vector3 velocity=new Vector3(Mathf.Cos(angle)*(1.8f+strength),.9f+Mathf.Abs(Mathf.Sin(angle))*.9f,Mathf.Sin(angle)*(1.8f+strength));
                EmitRitualParticle(at,velocity,p%3==0?VoxelAssets.Hex("#FFF0DB"):VoxelAssets.Hex("#EE98D8"),.11f+strength*.055f,.52f);
            }
            FlyingEffect number=EffectObject();number.start=steve.Target+Vector3.up*2.45f;number.end=number.start;
            number.duration=1.05f;number.number=true;number.size=1.65f;number.filter.sharedMesh=VoxelAssets.CommanderDamageNumber(impact.damage);
            shakeMagnitude=.025f+Mathf.Sqrt(Mathf.Clamp01(impact.damage/20f))*.14f;shakeStartedAt=Time.unscaledTime;
            if(SoundCue!=null)SoundCue("commander_hit",at,strength);
        }

        void EmitRitualParticle(Vector3 position,Vector3 velocity,Color color,float size,float lifetime)
        {
            if(sparks==null)return;ParticleSystem.EmitParams emit=new ParticleSystem.EmitParams();emit.position=position;emit.velocity=velocity;
            emit.startColor=color;emit.startSize=size;emit.startLifetime=lifetime;sparks.Emit(emit,1);
        }
        void ApplyCameraShake()
        {
            // Save the freshly computed base, never the previous frame's shaken transform.
            cameraBasePosition=viewCamera.transform.position;cameraBaseRotation=viewCamera.transform.rotation;cameraBaseValid=true;
            float amplitude=CameraShakeStrength;if(amplitude<=0)return;
            float age=Time.unscaledTime-shakeStartedAt;
            Vector3 jitter=new Vector3(Mathf.Sin(age*137),Mathf.Sin(age*173+.7f)*.65f,0)*amplitude;
            viewCamera.transform.position=cameraBasePosition+cameraBaseRotation*jitter;
            viewCamera.transform.rotation=cameraBaseRotation*Quaternion.Euler(0,0,Mathf.Sin(age*109)*amplitude*1.9f);
        }
        void ResetRitual()
        {
            ritualActive=false;ritualAge=0;ritualLaunchCount=ritualImpactCount=0;shakeMagnitude=0;shakeStartedAt=-10;
            if(ritualRoot!=null)ritualRoot.gameObject.SetActive(false);
            if(viewCamera!=null&&cameraBaseValid){viewCamera.transform.position=cameraBasePosition;viewCamera.transform.rotation=cameraBaseRotation;}
            foreach(VoxelCreature creature in creatures.Values)if(creature.Kind=="steve")creature.CancelDefeatImpact();
        }
    }
}
