using System;
using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        // All battle meshes/materials are shared. Saturation drops decorative detail first;
        // neither gameplay nor the separately pooled commander ceremony depends on this pool.
        const int MaxCombatVfx = 128, MaxCombatParticles = 800, ParticleBudgetPerFrame = 100;
        enum CombatMotion { Ring, Slash, Projectile, Beam, Rise, Flash }
        sealed class CombatVfx
        {
            public Transform transform;
            public MeshRenderer renderer;
            public MeshFilter filter;
            public CombatMotion motion;
            public Vector3 start, end, scale;
            public Quaternion rotation;
            public Color color;
            public float age, duration, nextTrail, arc, spin;
            public string family;
        }
        readonly List<CombatVfx> combatVfx = new List<CombatVfx>(MaxCombatVfx);
        readonly Stack<CombatVfx> combatVfxPool = new Stack<CombatVfx>(MaxCombatVfx);
        readonly Dictionary<string,float> combatShields = new Dictionary<string,float>();
        readonly MaterialPropertyBlock combatTint = new MaterialPropertyBlock();
        static readonly Dictionary<string,Mesh> combatMeshes = new Dictionary<string,Mesh>();
        static readonly int CombatTintId = Shader.PropertyToID("_Tint");
        Material combatMaterial;
        ParticleSystem combatParticles;
        int combatAllocated, combatParticleFrame = -1, combatParticlesThisFrame, combatStatusCursor, combatShieldRound=-1;
        float nextCombatStatus;
        internal int CombatVfxActiveCount { get { return combatVfx.Count; } }
        internal int CombatVfxAllocatedCount { get { return combatAllocated; } }
        internal int CombatVfxCapacity { get { return MaxCombatVfx; } }
        internal int CombatVfxSeenCount { get { return seenEffects.Count; } }
        internal int CombatParticleCount { get { return combatParticles == null ? 0 : combatParticles.particleCount; } }

        void EnsureCombatVfx()
        {
            if(combatMaterial != null) return;
            Shader shader=Resources.Load<Shader>("ArenaCombat");
            combatMaterial = new Material(shader) { name="Shared fading combat voxel material" };
            GameObject g = new GameObject("Budgeted combat fragments");
            g.transform.SetParent(effectRoot,false);
            combatParticles = g.AddComponent<ParticleSystem>();
            var main=combatParticles.main;
            main.loop=false; main.playOnAwake=false; main.startSpeed=0;
            main.startLifetime=.5f; main.startSize=.1f; main.gravityModifier=.65f;
            main.simulationSpace=ParticleSystemSimulationSpace.World; main.maxParticles=MaxCombatParticles;
            var emission=combatParticles.emission; emission.enabled=false;
            var shape=combatParticles.shape; shape.enabled=false;
            var size=combatParticles.sizeOverLifetime; size.enabled=true;
            size.size=new ParticleSystem.MinMaxCurve(1,AnimationCurve.EaseInOut(0,1,1,0));
            var fade=combatParticles.colorOverLifetime; fade.enabled=true;
            var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.9f,.55f),new GradientAlphaKey(0,1)});
            fade.color=new ParticleSystem.MinMaxGradient(gradient);
            var renderer=g.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode=ParticleSystemRenderMode.Mesh; renderer.mesh=VoxelAssets.Block(Color.white);
            renderer.sharedMaterial=combatMaterial; renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        }

        CombatVfx CombatObject(CombatMotion motion, Mesh mesh, Vector3 at, Color color,
            float duration, Vector3 scale, Quaternion rotation)
        {
            if(combatVfx.Count>=MaxCombatVfx) return null;
            EnsureCombatVfx();
            CombatVfx v;
            if(combatVfxPool.Count>0) v=combatVfxPool.Pop();
            else
            {
                GameObject g=new GameObject("Pooled battle accent"); g.transform.SetParent(effectRoot,false);
                v=new CombatVfx { transform=g.transform,filter=g.AddComponent<MeshFilter>(),renderer=g.AddComponent<MeshRenderer>() };
                v.renderer.sharedMaterial=combatMaterial; v.renderer.shadowCastingMode=ShadowCastingMode.Off;
                v.renderer.receiveShadows=false; combatAllocated++;
            }
            v.motion=motion; v.filter.sharedMesh=mesh; v.start=v.end=at; v.color=color;
            v.age=v.nextTrail=v.arc=v.spin=0; v.duration=Mathf.Max(.08f,duration); v.scale=scale;
            v.rotation=rotation; v.family=null;
            v.transform.SetPositionAndRotation(at,rotation); v.transform.localScale=scale;
            TintCombat(v,1); v.transform.gameObject.SetActive(true); combatVfx.Add(v); return v;
        }
        void TintCombat(CombatVfx v,float alpha)
        {
            Color tint=v.color.linear; tint.a=v.color.a*alpha;
            combatTint.SetColor(CombatTintId,tint); v.renderer.SetPropertyBlock(combatTint);
        }
        static Mesh CombatMesh(string shape)
        {
            Mesh cached;
            if(combatMeshes.TryGetValue(shape,out cached))return cached;
            cached=VoxelAssets.Cached("combat-v1/"+shape,b=>{
                Color white=Color.white;
                switch(shape)
                {
                    case "ring": b.Ring(1,.92f,white); break;
                    case "rune":
                        b.Ring(1,.94f,white);
                        for(int i=0;i<8;i++){float a=i*Mathf.PI/4; b.Box(new Vector3(Mathf.Cos(a)*1.1f,0,Mathf.Sin(a)*1.1f),new Vector3(.15f,.05f,.15f),white);}
                        break;
                    case "slash":
                        for(int i=0;i<11;i++){
                            float a=(-65+i*13)*Mathf.Deg2Rad;
                            b.Box(new Vector3(Mathf.Cos(a)*.85f,Mathf.Sin(a)*.85f,0),new Vector3(.11f,.26f,.045f),white,Quaternion.Euler(0,0,a*Mathf.Rad2Deg));
                        } break;
                    case "claw":
                        for(int i=-1;i<=1;i++) b.Box(new Vector3(i*.22f,0,0),new Vector3(.07f,.8f-i*.1f,.04f),white,Quaternion.Euler(0,0,-25));
                        break;
                    case "star":
                        b.Box(Vector3.zero,new Vector3(.7f,.10f,.045f),white);
                        b.Box(Vector3.zero,new Vector3(.10f,.7f,.045f),white);
                        b.Box(Vector3.zero,new Vector3(.3f,.3f,.045f),white,Quaternion.Euler(0,0,45)); break;
                    case "cross":
                        b.Box(Vector3.zero,new Vector3(.44f,.12f,.08f),white);
                        b.Box(Vector3.zero,new Vector3(.12f,.44f,.08f),white); break;
                    case "ice":
                        b.Box(Vector3.zero,new Vector3(.2f,.55f,.2f),white,Quaternion.Euler(0,0,35));
                        b.Box(new Vector3(.2f,-.1f,0),new Vector3(.14f,.35f,.14f),white,Quaternion.Euler(0,0,-25)); break;
                    case "arrow":
                        b.Box(Vector3.zero,new Vector3(.055f,.055f,.9f),white);
                        b.Box(new Vector3(0,0,.43f),new Vector3(.18f,.12f,.16f),white);
                        b.Box(new Vector3(0,0,-.35f),new Vector3(.25f,.06f,.18f),white); break;
                    case "trident":
                        b.Box(Vector3.zero,new Vector3(.06f,.06f,1.1f),white);
                        b.Box(new Vector3(0,0,.35f),new Vector3(.46f,.07f,.07f),white);
                        for(int i=-1;i<=1;i++)b.Box(new Vector3(i*.2f,0,.52f),new Vector3(.06f,.07f,.38f),white); break;
                    case "skull":
                        b.Box(Vector3.zero,new Vector3(.38f,.32f,.4f),white);
                        b.Box(new Vector3(0,-.17f,.09f),new Vector3(.24f,.10f,.2f),white);
                        b.Box(new Vector3(-.10f,.02f,.205f),new Vector3(.07f,.09f,.02f),new Color(.1f,.1f,.12f));
                        b.Box(new Vector3(.10f,.02f,.205f),new Vector3(.07f,.09f,.02f),new Color(.1f,.1f,.12f)); break;
                    case "orb":
                        b.Box(Vector3.zero,new Vector3(.35f,.35f,.35f),white,Quaternion.Euler(35,45,0));
                        b.Box(new Vector3(0,0,-.25f),new Vector3(.17f,.17f,.2f),white*.7f); break;
                    case "shield":
                        for(int i=-1;i<=1;i++)b.Box(new Vector3(i*.2f,0,0),new Vector3(.08f,.7f-Mathf.Abs(i)*.2f,.08f),white);
                        b.Box(new Vector3(0,-.4f,0),new Vector3(.3f,.08f,.08f),white); break;
                    case "beam":
                        b.Box(new Vector3(0,0,.5f),new Vector3(1,1,1),white); break;
                    default: b.Box(Vector3.zero,Vector3.one,white); break;
                }
            });
            combatMeshes.Add(shape,cached);return cached;
        }
        void CombatParticle(Vector3 at,Vector3 velocity,Color color,float size=.11f,float lifetime=.45f)
        {
            if(combatParticleFrame!=Time.frameCount){combatParticleFrame=Time.frameCount; combatParticlesThisFrame=0;}
            if(combatParticlesThisFrame>=ParticleBudgetPerFrame) return;
            EnsureCombatVfx(); combatParticlesThisFrame++;
            var emit=new ParticleSystem.EmitParams { position=at,velocity=velocity,startColor=color,
                startSize=size,startLifetime=lifetime,rotation3D=UnityEngine.Random.insideUnitSphere*180 };
            combatParticles.Emit(emit,1);
        }
        void CombatBurst(Vector3 at,Color color,int count,float force=2.2f,float size=.11f)
        {
            for(int i=0;i<count;i++){
                Vector3 direction=UnityEngine.Random.onUnitSphere; direction.y=Mathf.Abs(direction.y)*.8f+.2f;
                CombatParticle(at,direction*force,i%4==0?Color.Lerp(color,Color.white,.65f):color,size,.3f+UnityEngine.Random.value*.35f);
            }
        }
        void CombatRing(Vector3 at,Color color,float radius=1,float duration=.42f,bool rune=false)
        {
            at.y=.12f;
            CombatObject(CombatMotion.Ring,CombatMesh(rune?"rune":"ring"),at,color,duration,new Vector3(radius,1,radius),Quaternion.identity);
        }
        void CombatGlyph(Vector3 at,Color color,string shape,float size=.9f,float duration=.5f)
        {
            CombatObject(CombatMotion.Rise,CombatMesh(shape),at,color,duration,Vector3.one*size,viewCamera.transform.rotation);
        }
        VoxelCreature EffectCreature(string id,Vector3 fallback,bool alive=false)
        {
            VoxelCreature v;
            if(!string.IsNullOrEmpty(id)&&creatures.TryGetValue("fighter:"+id,out v))return v;
            // Coordinate fallback also keeps older v0.8 hosts readable.
            VoxelCreature nearest=null;float distance=.9f*.9f;
            foreach(var pair in fighterViews){
                if(alive&&pair.Value.dead)continue;
                float d=(pair.Key.Target-fallback).sqrMagnitude;
                if(d<distance){distance=d;nearest=pair.Key;}
            }
            return nearest;
        }
        static Vector3 CombatCenter(VoxelCreature v,Vector3 fallback)
        {return v==null?fallback+Vector3.up*.8f:v.CombatEffectCenter;}
        static Vector3 CombatTop(VoxelCreature v,Vector3 fallback)
        {return v==null?fallback+Vector3.up*1.8f:v.CombatEffectCenter+Vector3.up*Mathf.Clamp(v.Height*v.Size*.52f,.5f,3.2f);}
        Vector3 CombatSurface(VoxelCreature v,Vector3 center)
        {return center-viewCamera.transform.forward*(v==null?.4f:Mathf.Clamp(v.Size*.5f,.35f,1.5f));}
        static Color CombatColor(string family)
        {
            switch(family){
                case "fire":return VoxelAssets.Hex("#FFAF52");
                case "frost":return VoxelAssets.Hex("#91E7FF");
                case "poison":return VoxelAssets.Hex("#B8EE71");
                case "soul":return VoxelAssets.Hex("#C598EF");
                case "water":return VoxelAssets.Hex("#6FF5DA");
                case "wind":return VoxelAssets.Hex("#D1F1F6");
                default:return VoxelAssets.Hex("#FFE9B6");
            }
        }
        static string RangedFamily(string kind)
        {
            switch(kind){
                case "blaze":case "ghast":case "happy_ghast":case "magma_cube":return "fire";
                case "snow_golem":case "stray":return "frost";
                case "witch":case "bogged":return "poison";
                case "wither":case "ender_dragon":case "shulker":return "soul";
                case "guardian":case "elder_guardian":case "drowned":return "water";
                case "breeze":case "warden":return "wind";
                default:return "normal";
            }
        }
        void PlayCombatEffect(EffectState effect)
        {
            Vector3 from=WorldSurfacePoint(effect.x,effect.y),to=WorldSurfacePoint(effect.targetX,effect.targetY);
            VoxelCreature source=EffectCreature(effect.sourceId,from),target=EffectCreature(effect.targetId,to);
            string kind=string.IsNullOrEmpty(effect.kind)?source==null?"":source.Kind:effect.kind;
            Vector3 start=CombatCenter(source,from),end=CombatCenter(target,to);
            Color color=CombatColor(RangedFamily(kind));
            switch(effect.type)
            {
                case "strike":
                    if(source!=null)source.PlayCombatAttack(to,false);
                    bool claws=kind=="spider"||kind=="cave_spider"||kind=="wolf"||kind=="cat"||kind=="ocelot"||kind=="polar_bear";
                    Vector3 slashAt=Vector3.Lerp(start,end,.65f);
                    var slash=CombatObject(CombatMotion.Slash,CombatMesh(claws?"claw":"slash"),slashAt,
                        color,.23f,Vector3.one*(claws?.9f:1.1f),viewCamera.transform.rotation*Quaternion.Euler(0,0,(effect.slot%2==0?-25:25)));
                    if(slash!=null)slash.spin=effect.slot%2==0?75:-75;
                    break;
                case "arrow":
                    if(source!=null)source.PlayCombatAttack(to,true);
                    PlayCombatProjectile(kind,start,end,color);break;
                case "hit":
                    target=EffectCreature(effect.targetId,from);
                    end=CombatCenter(target,from);
                    if(target!=null)target.PlayCombatHit(source==null?end-Vector3.forward:source.Target,Mathf.Clamp(effect.value/20,.25f,1.5f));
                    Vector3 hitSurface=CombatSurface(target,end);
                    CombatObject(CombatMotion.Flash,CombatMesh("star"),hitSurface,Color.Lerp(color,Color.white,.6f),.16f,
                        Vector3.one*Mathf.Lerp(.45f,.9f,Mathf.Clamp01(effect.value/35)),viewCamera.transform.rotation);
                    CombatBurst(hitSurface,color,4,1.7f,.07f);
                    if(effect.value>=1&&combatVfx.Count<MaxCombatVfx-24)
                        CombatObject(CombatMotion.Rise,VoxelAssets.DamageNumber(Mathf.RoundToInt(effect.value)),CombatTop(target,from),
                            Color.white,.64f,Vector3.one*.9f,viewCamera.transform.rotation);
                    break;
                case "burn":
                    CombatBurst(CombatSurface(target,end),CombatColor("fire"),10,1.6f,.12f);CombatRing(to,CombatColor("fire"),.8f,.38f);break;
                case "poison":
                    CombatBurst(CombatSurface(target,end),CombatColor("poison"),8,1.25f,.12f);CombatRing(to,CombatColor("poison"),.7f,.5f);break;
                case "slow":
                    if(kind=="spider"||kind=="cave_spider"||kind=="squid"||kind=="llama"||kind=="trader_llama"||kind=="witch")
                    {CombatRing(to,kind=="witch"?CombatColor("poison"):CombatColor("normal"),.8f,.55f,true);}
                    else{CombatGlyph(CombatTop(target,to),CombatColor("frost"),"ice",.85f,.5f);CombatBurst(CombatSurface(target,end),CombatColor("frost"),6,1,.1f);}
                    break;
                case "stun":
                    CombatRing(to,CombatColor("normal"),1.1f,.4f);
                    CombatGlyph(CombatTop(target,to),CombatColor("normal"),"star",.7f,.55f);break;
                case "knockback":
                    PlayCombatBeam(start,end,CombatColor("normal"),.13f,.14f);
                    CombatRing(to,CombatColor("normal"),1.2f,.35f);CombatBurst(end,color,7,2.7f);break;
                case "sweep":
                    CombatRing(to,color,Mathf.Clamp(effect.value*WorldScale,.9f,3),.42f);
                    CombatBurst(end,color,8,2.2f);break;
                case "thorns":
                    PlayCombatBeam(start,end,CombatColor("poison"),.06f,.22f);
                    CombatGlyph(end,CombatColor("poison"),"star",.65f,.3f);break;
                case "heal":
                    CombatRing(to,VoxelAssets.Hex("#7BE9AE"),.9f,.55f,true);
                    CombatGlyph(CombatTop(target,to),VoxelAssets.Hex("#9CFFC4"),"cross",1.05f,.75f);
                    if((start-end).sqrMagnitude>1)PlayCombatBeam(start,end,VoxelAssets.Hex("#81CDB0"),.035f,.28f);
                    for(int i=0;i<7;i++)CombatParticle(end+UnityEngine.Random.insideUnitSphere*.5f,Vector3.up*(.7f+UnityEngine.Random.value),VoxelAssets.Hex("#91F3B1"),.09f,.65f);break;
                case "shield":
                    CombatGlyph(CombatTop(target,to)+viewCamera.transform.right*.6f,VoxelAssets.Hex("#9DD5FF"),"shield",1.1f,.65f);
                    CombatRing(to,VoxelAssets.Hex("#97D8F6"),1,.5f,true);break;
                case "teleport":
                    CombatRing(from,CombatColor("soul"),1,.5f,true);
                    // Use the post-teleport authoritative source position for the arrival.
                    Vector3 arrival=source==null?to:source.Target;
                    CombatRing(arrival,CombatColor("soul"),1.2f,.55f,true);
                    CombatBurst(from+Vector3.up*.8f,CombatColor("soul"),10,2);
                    CombatBurst(arrival+Vector3.up*.8f,CombatColor("soul"),10,2);break;
                case "spawn":
                    CombatRing(from,CombatColor("soul"),.9f,.6f,true);
                    CombatBurst(CombatCenter(EffectCreature(effect.targetId,from),from),CombatColor("soul"),12,1.5f);
                    break;
                case "explode":
                    float radius=Mathf.Clamp(effect.value*WorldScale,1.3f,3.7f);
                    CombatRing(from,CombatColor("fire"),radius,.58f);
                    CombatRing(from,CombatColor("normal"),radius*.6f,.38f,true);
                    CombatObject(CombatMotion.Flash,CombatMesh("star"),start,VoxelAssets.Hex("#FFF3CA"),.23f,Vector3.one*2,viewCamera.transform.rotation);
                    CombatBurst(start,CombatColor("fire"),32,4.8f,.2f);
                    break;
                case "death": PlayCombatDeath(kind,source,from);break;
                case "cleanse":
                    CombatGlyph(CombatTop(target,from),VoxelAssets.Hex("#AEFFE5"),"cross",1,.65f);
                    CombatRing(from,VoxelAssets.Hex("#91F3D1"),1,.65f,true);
                    CombatBurst(end,VoxelAssets.Hex("#AEFFE5"),7,1.4f);break;
                case "command_drums":case "command_ward":case "command_focus":case "command_cleanse":
                case "command_smoke":case "command_trap":case "command_decoy":case "command_rift":
                    Color commandColor=VoxelAssets.Hex(effect.type=="command_cleanse"?"#AEFFE5":effect.type=="command_focus"?"#FFAA91":effect.type=="command_smoke"||effect.type=="command_rift"?"#C5ACFF":"#9FE8DA");
                    CombatRing(from,commandColor,Mathf.Clamp(effect.value*WorldScale,1.2f,5),.8f,true);
                    CombatGlyph(from+Vector3.up*1.5f,commandColor,effect.type=="command_cleanse"?"cross":effect.type=="command_ward"?"shield":"rune",1.2f,.75f);
                    CombatBurst(from+Vector3.up*.3f,commandColor,16,2.3f);break;
                case "lightning":
                    PlayCombatBeam(from+Vector3.up*12,from+Vector3.up*.2f,VoxelAssets.Hex("#DFD1FF"),.17f,.3f);
                    CombatRing(from,VoxelAssets.Hex("#CEB7FF"),Mathf.Clamp(effect.value*WorldScale,.8f,2.5f),.45f);
                    CombatBurst(from+Vector3.up*.2f,VoxelAssets.Hex("#DFD1FF"),22,4);break;
                case "eruption":
                    CombatRing(from,CombatColor("fire"),Mathf.Clamp(effect.value*WorldScale,1,4),.7f);
                    CombatBurst(from+Vector3.up*.2f,CombatColor("fire"),30,4.5f,.18f);break;
                case "cover_break":CombatBurst(from+Vector3.up*.5f,VoxelAssets.Hex("#B5AB92"),24,3,.21f);break;
            }
        }

        void PlayCombatProjectile(string kind,Vector3 start,Vector3 end,Color color)
        {
            string family=RangedFamily(kind);
            if(kind=="guardian"||kind=="elder_guardian"){
                PlayCombatBeam(start,end,color,.10f,.25f);
                for(int i=1;i<=5;i++)CombatGlyph(Vector3.Lerp(start,end,i/6f),color,"star",.22f,.20f);
                return;
            }
            if(kind=="warden"){
                PlayCombatBeam(start,end,color,.09f,.3f);
                Vector3 direction=end-start;
                for(int i=1;i<=4;i++)CombatObject(CombatMotion.Flash,CombatMesh("ring"),Vector3.Lerp(start,end,i/5f),color,.3f,
                    Vector3.one*(.3f+i*.13f),Quaternion.LookRotation(direction)*Quaternion.Euler(90,0,0));
                return;
            }
            string shape=kind=="drowned"?"trident":kind=="wither"?"skull":
                family=="normal"||kind=="stray"||kind=="bogged"?"arrow":"orb";
            if(kind=="llama"||kind=="trader_llama"||kind=="villager"||kind=="wandering_trader")shape="orb";
            float flight=Mathf.Clamp(Vector3.Distance(start,end)/28,.12f,.27f);
            var projectile=CombatObject(CombatMotion.Projectile,CombatMesh(shape),start,color,flight,Vector3.one,Quaternion.identity);
            if(projectile==null)return;
            projectile.end=end;projectile.family=family;
            projectile.arc=kind=="witch"?.65f:family=="normal"?.12f:.24f;
            if(kind=="ghast"||kind=="ender_dragon")projectile.scale=Vector3.one*1.65f;
            projectile.transform.rotation=Quaternion.LookRotation((end-start).sqrMagnitude>.001f?end-start:Vector3.forward);
        }
        void PlayCombatBeam(Vector3 start,Vector3 end,Color color,float width,float duration)
        {
            Vector3 delta=end-start;
            if(delta.sqrMagnitude<.002f)return;
            var beam=CombatObject(CombatMotion.Beam,CombatMesh("beam"),start,color,duration,
                new Vector3(width,width,delta.magnitude),Quaternion.LookRotation(delta));
            if(beam!=null)beam.end=end;
        }
        void PlayCombatDeath(string kind,VoxelCreature source,Vector3 from)
        {
            var definition=string.IsNullOrEmpty(kind)?null:CreatureCatalog.Get(kind);
            Color body=definition==null?CombatColor("normal"):VoxelAssets.Hex(definition.color);
            Vector3 center=CombatCenter(source,from);
            bool slime=kind=="slime"||kind=="magma_cube";
            string family=RangedFamily(kind);
            if(kind=="enderman"||kind=="endermite"||kind=="vex")family="soul";
            CombatBurst(center,body,slime?22:16,slime?2.5f:2,.13f);
            if(family!="normal")CombatBurst(center,CombatColor(family),9,1.5f,.09f);
            CombatRing(from,Color.Lerp(body,Color.white,.25f),slime?1.2f:.85f,.55f);
            if(!slime){
                var soul=CombatObject(CombatMotion.Rise,CombatMesh("orb"),center,Color.Lerp(body,Color.white,.65f),.8f,Vector3.one*.65f,viewCamera.transform.rotation);
                if(soul!=null)soul.spin=60;
            }
        }

        void UpdateCombatVfx(float dt,float time)
        {
            for(int i=combatVfx.Count-1;i>=0;i--){
                CombatVfx v=combatVfx[i];v.age+=dt;float t=v.age/v.duration;
                if(t>=1){
                    // Projectile impacts happen once on arrival; authoritative damage is immediate.
                    if(v.motion==CombatMotion.Projectile){CombatBurst(v.end,CombatColor(v.family),v.family=="normal"?3:7,1.8f,.08f);}
                    v.transform.gameObject.SetActive(false);combatVfxPool.Push(v);combatVfx.RemoveAt(i);continue;
                }
                float pulse=Mathf.Sin(t*Mathf.PI),fade=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.35f,1,t));
                switch(v.motion){
                    case CombatMotion.Projectile:
                        Vector3 previous=v.transform.position;
                        Vector3 position=Vector3.Lerp(v.start,v.end,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*v.arc;
                        Vector3 direction=position-previous;
                        v.transform.position=position;
                        if(direction.sqrMagnitude>.00001f)v.transform.rotation=Quaternion.LookRotation(direction);
                        v.transform.localScale=v.scale;
                        if(v.age>=v.nextTrail){v.nextTrail=v.age+.035f;CombatParticle(position,-direction.normalized*.4f,CombatColor(v.family),v.family=="normal"?.045f:.10f,.18f);}
                        fade=1;break;
                    case CombatMotion.Ring:
                        float spread=.35f+.9f*(1-(1-t)*(1-t));
                        v.transform.localScale=new Vector3(v.scale.x*spread,1,v.scale.z*spread);
                        break;
                    case CombatMotion.Slash:
                        v.transform.localRotation=v.rotation*Quaternion.Euler(0,0,v.spin*(t-.5f));
                        v.transform.localScale=v.scale*(.8f+pulse*.35f);break;
                    case CombatMotion.Beam:
                        v.transform.localScale=new Vector3(v.scale.x*(.5f+pulse),v.scale.y*(.5f+pulse),v.scale.z);break;
                    case CombatMotion.Rise:
                        v.transform.position=v.start+Vector3.up*(t*.95f);
                        v.transform.rotation=viewCamera.transform.rotation*Quaternion.Euler(0,0,v.spin*t);
                        v.transform.localScale=v.scale*(.85f+pulse*.2f);break;
                    case CombatMotion.Flash:
                        v.transform.localScale=v.scale*(.35f+pulse*.8f);break;
                }
                TintCombat(v,fade);
            }
            if(time>=nextCombatStatus){nextCombatStatus=time+.18f;UpdateCombatStatuses(time);}
        }
        void UpdateCombatStatuses(float time)
        {
            int shown=0,index=0,total=fighterViews.Count;
            if(total==0)return;
            // Rotate a bounded sampling window so large armies all receive status feedback.
            int cursor=combatStatusCursor%total;
            foreach(var pair in fighterViews){
                int current=index++;
                if((current-cursor+total)%total>=32||pair.Value.dead)continue;
                FighterState f=pair.Value;VoxelCreature v=pair.Key;
                Vector3 at=CombatCenter(v,v.Target);
                float a=time*3+current*2.4f;
                Vector3 orbit=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.45f;
                if(f.burnTime>0)CombatParticle(at+orbit,new Vector3(0,1.2f,0),CombatColor("fire"),.12f,.35f);
                if(f.poisonTime>0)CombatParticle(at+orbit*.8f,Vector3.up*.55f,CombatColor("poison"),.12f,.5f);
                if(f.slowTime>0&&shown++<10)CombatGlyph(v.Target+orbit+Vector3.up*.25f,CombatColor("frost"),"ice",.4f,.32f);
                if(f.stunTime>0&&shown++<12)CombatGlyph(at+Vector3.up*.8f+orbit,CombatColor("normal"),"star",.45f,.3f);
            }
            combatStatusCursor=(cursor+32)%total;
        }
        void SyncCombatShields()
        {
            if(state!=null&&combatShieldRound!=state.round){combatShields.Clear();combatShieldRound=state.round;}
            foreach(var pair in fighterViews){
                var f=pair.Value;float before;
                bool known=combatShields.TryGetValue(f.id,out before);
                if(!f.dead&&f.shield>0&&!known){
                    CombatRing(pair.Key.Target,VoxelAssets.Hex("#A6D6FF"),.8f,.6f,true);
                }
                if(known&&before>0&&f.shield<=0){
                    Vector3 at=CombatCenter(pair.Key,pair.Key.Target);
                    CombatBurst(at,VoxelAssets.Hex("#A6D6FF"),9,2.5f,.11f);
                    CombatGlyph(at,VoxelAssets.Hex("#DAF2FF"),"shield",1,.23f);
                }
                combatShields[f.id]=f.shield;
            }
        }
        internal void ClearCombatVfxForVerification()
        {
            for(int i=0;i<combatVfx.Count;i++){
                combatVfx[i].transform.gameObject.SetActive(false);combatVfxPool.Push(combatVfx[i]);
            }
            combatVfx.Clear();combatShields.Clear();combatShieldRound=-1;seenEffects.Clear();
            if(combatParticles!=null)combatParticles.Clear();
            nextCombatStatus=0;combatStatusCursor=0;combatParticleFrame=-1;combatParticlesThisFrame=0;
        }
        void OnDestroy(){DisposeTargetingPresentation();DisposeBattlefieldPresentation();if(combatMaterial!=null)Destroy(combatMaterial);}
    }
}
