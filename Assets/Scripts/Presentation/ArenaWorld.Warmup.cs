using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlockArena.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        // Work is performed only before battle. A single Unity model build cannot be
        // preempted, so the time limit is a soft budget; at most one model / two small
        // jobs run per rendered frame. No warmed body is ever active in the scene.
        const double WarmupBudgetMs=1.5;
        const int WarmupJobsPerFrame=2, FlyingWarmupCapacity=16;
        static readonly string[] WarmupCombatShapes={"ring","rune","slash","claw","star","cross","ice","arrow","trident","skull","orb","shield","beam","block"};
        static readonly string[] WarmupShaders={"ArenaCombat","ArenaVoxelLit","ArenaConcealedVoxel"};
        readonly Dictionary<string,int> warmupTargets=new Dictionary<string,int>();
        readonly Dictionary<string,int> warmupAvailable=new Dictionary<string,int>();
        readonly List<string> warmupKinds=new List<string>();
        Transform warmupRoot;
        int warmupStaticCursor,warmupKindCursor,creatureBuildCount,creaturePoolReuseCount,creatureMigrationCount;
        int warmupBuildCount,warmupFrameJobs,warmupFrameModels,warmupPeakJobs,warmupPeakModels,warmupBudgetOverruns,warmupImpactMeshBuilds;
        float nextWarmupPlan;
        double warmupFrameMs,warmupPeakMs,warmupTotalMs,creatureBuildTotalMs;

        internal int CreatureVisualBuildCount { get { return creatureBuildCount; } }
        internal int CreaturePoolReuseCount { get { return creaturePoolReuseCount; } }
        internal int CreatureBattleMigrationCount { get { return creatureMigrationCount; } }
        internal double CreatureBuildMilliseconds { get { return creatureBuildTotalMs; } }
        internal int WarmupModelBuildCount { get { return warmupBuildCount; } }
        internal int WarmupLastFrameJobs { get { return warmupFrameJobs; } }
        internal int WarmupLastFrameModels { get { return warmupFrameModels; } }
        internal int WarmupPeakFrameJobs { get { return warmupPeakJobs; } }
        internal int WarmupPeakFrameModels { get { return warmupPeakModels; } }
        internal int WarmupBudgetOverruns { get { return warmupBudgetOverruns; } }
        internal double WarmupLastFrameMilliseconds { get { return warmupFrameMs; } }
        internal double WarmupPeakFrameMilliseconds { get { return warmupPeakMs; } }
        internal double WarmupTotalMilliseconds { get { return warmupTotalMs; } }
        internal double WarmupSoftBudgetMilliseconds { get { return WarmupBudgetMs; } }
        internal int CachedVoxelMeshBuildCount { get { return VoxelAssets.MeshBuildCount; } }
        internal double CachedVoxelMeshBuildMilliseconds { get { return VoxelAssets.MeshBuildMilliseconds; } }
        internal int WarmupCombatImpactMeshBuildCount { get { return warmupImpactMeshBuilds; } }
        internal string[] CachedVoxelMeshKeysForVerification { get { return VoxelAssets.CachedMeshKeysForVerification; } }
        internal bool WarmupReady { get { return warmupStaticCursor>=1+WarmupShaders.Length+WarmupCombatShapes.Length+100&&combatAllocated==MaxCombatVfx&&effectPool.Count+movingEffects.Count>=FlyingWarmupCapacity&&warmupKindCursor>=warmupKinds.Count; } }
        internal int WarmupActiveBodyCount { get { if(warmupRoot==null)return 0;int active=0;foreach(var body in warmupRoot.GetComponentsInChildren<VoxelCreature>(true))if(body.gameObject.activeInHierarchy)active++;return active; } }
        internal VoxelCreature CreatureVisualForVerification(string key) { VoxelCreature view;return creatures.TryGetValue(key,out view)?view:null; }
        internal int PooledCreatureCountForVerification(string kind) { Stack<VoxelCreature> pool;return pools.TryGetValue(kind,out pool)?pool.Count:0; }

        VoxelCreature GetFighter(FighterState fighter,Vector3 at)
        {
            string key="fighter:"+fighter.id;
            VoxelCreature visual;
            if(!creatures.TryGetValue(key,out visual)&&!fighter.summoned&&!fighter.neutral&&!string.IsNullOrEmpty(fighter.unitId)){
                string before="unit:"+fighter.unitId;
                if(creatures.TryGetValue(before,out visual)&&visual.Kind==fighter.kind&&visual.Slot==fighter.slot){
                    // Change identity in place. ResetVisual would snap the body and erase
                    // state; the normal fresh fighter/visibility snapshot updates it below.
                    creatures.Remove(before);creatures.Add(key,visual);creatureMigrationCount++;
                }
            }
            return Get(key,fighter.kind,fighter.slot,at);
        }

        VoxelCreature BuildCreatureVisual(string kind,bool warming)
        {
            if(warmupRoot==null){warmupRoot=NewRoot("Inactive presentation warmup",creatureRoot);warmupRoot.gameObject.SetActive(false);}
            long started=Stopwatch.GetTimestamp();
            GameObject body=new GameObject(kind);
            // Parenting before adding any components keeps the whole articulated model
            // inactive, including hidden enemies and as-yet-unseen summoned species.
            body.transform.SetParent(warmupRoot,false);
            VoxelCreature visual=body.AddComponent<VoxelCreature>();
            UnityEngine.Random.State random=UnityEngine.Random.state;
            try{
                UnityEngine.Random.InitState(unchecked(kind.GetHashCode()^creatureBuildCount*6971));visual.Build(kind);
                int beforeImpactMeshes=VoxelAssets.MeshBuildCount;
                visual.WarmupCombatPresentation();
                warmupImpactMeshBuilds+=VoxelAssets.MeshBuildCount-beforeImpactMeshes;
            }finally{UnityEngine.Random.state=random;}
            body.SetActive(false);
            creatureBuildCount++;if(warming)warmupBuildCount++;
            creatureBuildTotalMs+=ElapsedMs(started);
            return visual;
        }

        static double ElapsedMs(long started) { return (Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency; }
        static void AddKind(Dictionary<string,int> counts,string kind,int amount)
        { int existing;counts.TryGetValue(kind,out existing);counts[kind]=existing+amount; }

        void PlanPresentationWarmup()
        {
            warmupTargets.Clear();warmupAvailable.Clear();warmupKinds.Clear();warmupKindCursor=0;
            if(state==null)return;
            foreach(var player in state.players){
                if(player.eliminated||player.hp<=0)continue;
                AddKind(warmupTargets,"steve",1);
                if(player.commanderSkill=="decoy")AddKind(warmupTargets,"iron_golem",1);
                foreach(var unit in player.units){
                    AddKind(warmupTargets,unit.kind,1);
                    // Reserve the first legal summon/death wave. Later waves use the
                    // same pool and existing combat limits without limiting the army.
                    foreach(string skill in CreatureCatalog.Get(unit.kind).skills){
                        AbilityDefinition ability=CreatureAbilities.Get(skill);
                        if(ability.effect=="summon_vex")AddKind(warmupTargets,"vex",Mathf.Min(3,Mathf.CeilToInt(ability.value)));
                        else if(ability.effect=="split")AddKind(warmupTargets,unit.kind,Mathf.Min(3,Mathf.CeilToInt(ability.value)));
                    }
                }
            }
            foreach(var pair in pools)warmupAvailable[pair.Key]=pair.Value.Count;
            foreach(var visual in creatures.Values)AddKind(warmupAvailable,visual.Kind,1);
            foreach(var pair in warmupTargets){int available;warmupAvailable.TryGetValue(pair.Key,out available);for(int n=available;n<pair.Value;n++)warmupKinds.Add(pair.Key);}
        }

        void TickPresentationWarmup()
        {
            warmupFrameJobs=warmupFrameModels=0;warmupFrameMs=0;
            if(state==null||(state.phase!="prep"&&state.phase!="recruit"&&state.phase!="reveal"&&state.phase!="tactics"))return;
            if(Time.unscaledTime>=nextWarmupPlan){nextWarmupPlan=Time.unscaledTime+.5f;PlanPresentationWarmup();}
            long started=Stopwatch.GetTimestamp();
            while(warmupFrameJobs<WarmupJobsPerFrame){
                bool model;
                if(!RunPresentationWarmupJob(out model))break;
                warmupFrameJobs++;if(model)warmupFrameModels++;
                if(model||ElapsedMs(started)>=WarmupBudgetMs)break;
            }
            warmupFrameMs=ElapsedMs(started);warmupTotalMs+=warmupFrameMs;
            warmupPeakMs=Math.Max(warmupPeakMs,warmupFrameMs);
            warmupPeakJobs=Math.Max(warmupPeakJobs,warmupFrameJobs);warmupPeakModels=Math.Max(warmupPeakModels,warmupFrameModels);
            if(warmupFrameJobs>0&&warmupFrameMs>WarmupBudgetMs)warmupBudgetOverruns++;
        }

        bool RunPresentationWarmupJob(out bool model)
        {
            model=false;
            // Warming bodies first avoids later reveal/battle model construction.
            if(warmupKindCursor<warmupKinds.Count){
                string kind=warmupKinds[warmupKindCursor++];Stack<VoxelCreature> pool;
                if(!pools.TryGetValue(kind,out pool)){pool=new Stack<VoxelCreature>();pools.Add(kind,pool);}
                pool.Push(BuildCreatureVisual(kind,true));model=true;return true;
            }
            if(warmupStaticCursor==0){EnsureCombatVfx();warmupStaticCursor++;return true;}
            if(warmupStaticCursor<=WarmupShaders.Length){WarmupShader(WarmupShaders[warmupStaticCursor-1]);warmupStaticCursor++;return true;}
            int meshStart=1+WarmupShaders.Length;
            if(warmupStaticCursor<meshStart+WarmupCombatShapes.Length){CombatMesh(WarmupCombatShapes[warmupStaticCursor-meshStart]);warmupStaticCursor++;return true;}
            if(warmupStaticCursor<meshStart+WarmupCombatShapes.Length+100){VoxelAssets.DamageNumber(warmupStaticCursor-meshStart-WarmupCombatShapes.Length);warmupStaticCursor++;return true;}
            if(combatAllocated<MaxCombatVfx){combatVfxPool.Push(CreateCombatAccent());return true;}
            if(effectPool.Count+movingEffects.Count<FlyingWarmupCapacity){effectPool.Push(CreateFlyingEffect());return true;}
            return false;
        }

        void WarmupShader(string resource)
        {
            Shader shader=Resources.Load<Shader>(resource);if(shader==null)return;
            var variants=new ShaderVariantCollection();
            if(resource=="ArenaCombat")variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.Normal,"FOG_LINEAR"));
            else {
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ForwardBase,"DIRECTIONAL","FOG_LINEAR","SHADOWS_SCREEN"));
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ForwardAdd,"POINT","FOG_LINEAR"));
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader,PassType.ShadowCaster,"SHADOWS_DEPTH"));
            }
            // A single small collection per job avoids the synchronous global shader warmup.
            variants.WarmUp();Destroy(variants);
        }

        CombatVfx CreateCombatAccent()
        {
            EnsureCombatVfx();
            GameObject g=new GameObject("Pooled battle accent");g.SetActive(false);g.transform.SetParent(effectRoot,false);
            var accent=new CombatVfx{transform=g.transform,filter=g.AddComponent<MeshFilter>(),renderer=g.AddComponent<MeshRenderer>()};
            accent.renderer.sharedMaterial=combatMaterial;accent.renderer.shadowCastingMode=ShadowCastingMode.Off;accent.renderer.receiveShadows=false;
            combatAllocated++;return accent;
        }

        FlyingEffect CreateFlyingEffect()
        {
            GameObject g=new GameObject("Pooled combat effect");g.SetActive(false);g.transform.SetParent(effectRoot,false);
            var effect=new FlyingEffect{transform=g.transform,filter=g.AddComponent<MeshFilter>()};
            MeshRenderer renderer=g.AddComponent<MeshRenderer>();renderer.sharedMaterial=VoxelAssets.Glow;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            return effect;
        }
    }
}
