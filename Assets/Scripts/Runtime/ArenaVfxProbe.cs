using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlockArena.Core;
using BlockArena.Presentation;
using UnityEngine;

namespace BlockArena.Runtime
{
    // Explicit opt-in native rendering verification. No server, simulation or network connection is created.
    public sealed class ArenaVfxProbe : MonoBehaviour
    {
        [Serializable]
        sealed class CaptureQuality
        {
            public string name;
            public int samples, distinctColors;
            public float minimumLuminance, maximumLuminance, nonBlackFraction;
        }

        [Serializable]
        sealed class Report
        {
            public bool success;
            public string error, unityVersion, graphicsDevice;
            public int width, height, capacity, peakAllocated, peakActive, mixedArmies, mixedFighters, mixedEvents;
            public List<string> checks = new List<string>();
            public List<string> captures = new List<string>();
            public List<CaptureQuality> captureQuality = new List<CaptureQuality>();
        }

        sealed class Scenario
        {
            public string name, sourceKind;
            public string[] effects;
            public Scenario(string name, string sourceKind, params string[] effects)
            { this.name = name; this.sourceKind = sourceKind; this.effects = effects; }
        }

        readonly Report report = new Report();
        ArenaApp app;
        ArenaWorld world;
        string folder;
        int serial;
        bool failed, restored, originalInput, originalEnabled, closeup;
        Vector2? originalPointer;
        GameState originalVerificationState;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void StartIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-vfx-test") >= 0)
                new GameObject("Combat visual effects verification").AddComponent<ArenaVfxProbe>();
        }

        IEnumerator Start()
        {
            // Observe exceptions in a nested iterator too, so CI gets a nonzero process exit and a written report.
            IEnumerator run = Run();
            while (true)
            {
                object next = null;
                bool moved = false;
                try { moved = run.MoveNext(); if (moved) next = run.Current; }
                catch (Exception error) { Fail(error.ToString()); }
                if (failed || !moved) yield break;
                yield return next;
            }
        }

        IEnumerator Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            folder = Path.Combine(Application.dataPath, "..", "Captures", "CombatVfx");
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-capture-dir") folder = args[i + 1];
            Directory.CreateDirectory(folder);
            report.unityVersion = Application.unityVersion;
            report.graphicsDevice = SystemInfo.graphicsDeviceName;
            report.width = Screen.width; report.height = Screen.height;

            yield return new WaitForSecondsRealtime(1);
            app = ArenaApp.Instance;
            Require(app != null && app.World != null, "native ArenaApp and ArenaWorld were initialized");
            Require(!app.HasSession && !app.Connected && !app.Hosting,
                "visual verification runs without a network session (omit -quickplay and -join)");
            originalEnabled = app.enabled; originalInput = app.VerificationInput;
            originalPointer = app.VerificationPointer; originalVerificationState = app.VerificationState;
            app.VerificationInput = true;
            app.VerificationPointer = new Vector2(-1000, -1000);
            app.enabled = false; // Hide app menus and prevent its normal snapshot loop from replacing the fixture.
            world = app.World;
            world.Clear(); world.ResetView();
            GameState state = Fixture("zombie");
            world.SetState(state, "vfx-player-0");
            world.Zoom(10, new Vector2(Screen.width * .5f, Screen.height * .5f));
            yield return new WaitForSecondsRealtime(1);
            report.capacity = world.CombatVfxCapacity;
            Require(report.capacity > 0 && report.capacity <= 256, "combat effect pool has a finite capacity <= 256");

            Scenario[] scenarios = {
                new Scenario("01-melee", "iron_golem", "strike", "hit", "knockback"),
                new Scenario("02-arrow", "skeleton", "arrow"),
                new Scenario("03-fire", "blaze", "arrow", "burn", "hit"),
                new Scenario("04-frost", "snow_golem", "arrow", "slow", "hit"),
                new Scenario("05-guardian-beam", "guardian", "arrow", "stun"),
                new Scenario("06-poison", "cave_spider", "strike", "poison"),
                new Scenario("07-heal-and-shield", "allay", "heal", "shield"),
                new Scenario("08-teleport", "enderman", "teleport"),
                new Scenario("09-sweep", "evoker", "sweep"),
                new Scenario("10-spawn", "slime", "spawn"),
                new Scenario("11-explosion", "creeper", "explode"),
                new Scenario("12-death", "zombie", "death")
            };
            foreach (Scenario scenario in scenarios)
            {
                closeup = true;
                world.ClearCombatVfxForVerification();
                state = Fixture(scenario.sourceKind);
                if (scenario.name == "07-heal-and-shield")
                {
                    state.fighters[1].ownerId = state.fighters[0].ownerId;
                    state.fighters[1].slot = state.fighters[0].slot;
                }
                world.SetState(state, "vfx-player-0");
                yield return new WaitForSecondsRealtime(.25f);
                foreach (string effect in scenario.effects) state.effects.Add(Event(effect, state, serial++));
                state.fighters[0].attackTime = .12f;
                if (scenario.name == "03-fire") state.fighters[1].burnTime = 3;
                if (scenario.name == "04-frost") state.fighters[1].slowTime = 3;
                if (scenario.name == "06-poison") state.fighters[1].poisonTime = 3;
                if (scenario.name == "07-heal-and-shield") state.fighters[1].shield = 12;
                if (scenario.name == "12-death") { state.fighters[0].dead = true; state.fighters[0].hp = 0; }
                world.SetState(state, "vfx-player-0");
                ObservePool();
                int accepted = world.CombatVfxSeenCount;
                int active = world.CombatVfxActiveCount;
                Require(accepted == scenario.effects.Length, scenario.name + " accepted each event once");
                Require(active > 0, scenario.name + " produced visible effect objects");
                world.SetState(state, "vfx-player-0");
                Require(world.CombatVfxSeenCount == accepted && world.CombatVfxActiveCount == active,
                    scenario.name + " repeat snapshot does not retrigger visual effects");
                yield return new WaitForSecondsRealtime(.10f);
                yield return new WaitForEndOfFrame();
                Capture(scenario.name);
            }

            // Exercise the actual species pool, including a model whose death animation hid its figure.
            world.ClearCombatVfxForVerification();
            state = Fixture("magma_cube");
            world.SetState(state, "vfx-player-0");
            VoxelCreature original = FindCreature(state.fighters[0].unitId);
            Require(original != null, "creature pool fixture has an actual active VoxelCreature");
            Transform figure = original.transform.Find("Articulated model");
            MeshFilter[] parts = figure.GetComponentsInChildren<MeshFilter>();
            var normalMeshes = new Mesh[parts.Length];
            var normalScales = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            { normalMeshes[i] = parts[i].sharedMesh; normalScales[i] = parts[i].transform.localScale; }
            yield return new WaitForSecondsRealtime(.2f);
            state.fighters[0].dead = true; state.fighters[0].hp = 0;
            state.effects.Add(Event("death", state, serial++));
            world.SetState(state, "vfx-player-0");
            yield return new WaitForSecondsRealtime(1);
            Require(!figure.gameObject.activeSelf, "completed creature death hides its articulated figure");
            state.fighters.RemoveAt(0); state.effects.Clear();
            world.SetState(state, "vfx-player-0");
            Require(!original.gameObject.activeSelf, "removed fighter returns its actual creature object to the species pool");
            FighterState fresh = Fighter("vfx-reused-source", "magma_cube", 2, -58, 0);
            state.fighters.Insert(0, fresh);
            world.SetState(state, "vfx-player-0");
            VoxelCreature reused = FindCreature(fresh.unitId);
            Require(reused == original && !reused.Dead && figure.gameObject.activeSelf,
                "new fighter ID reuses the same creature instance and restores its visible alive state");
            bool restoredParts = true;
            for (int i = 0; i < parts.Length; i++)
                restoredParts &= parts[i].sharedMesh == normalMeshes[i] &&
                    Vector3.Distance(parts[i].transform.localScale, normalScales[i]) < .001f;
            Require(restoredParts, "creature reuse restores every shared mesh and body-part scale after death scatter");
            Vector3 gameplayRoot = reused.transform.position;
            reused.PlayCombatAttack(gameplayRoot + Vector3.right * 3);
            reused.PlayCombatHit(gameplayRoot - Vector3.right * 3);
            yield return new WaitForSecondsRealtime(.025f);
            bool flashed = false;
            for (int i = 0; i < parts.Length; i++) flashed |= parts[i].sharedMesh != normalMeshes[i];
            Require(flashed, "reused creature still plays its impact flash");
            yield return new WaitForSecondsRealtime(.4f);
            bool normalAgain = true;
            for (int i = 0; i < parts.Length; i++) normalAgain &= parts[i].sharedMesh == normalMeshes[i];
            Require(normalAgain, "reused creature releases its impact mesh after the hit animation");
            Require(Vector3.Distance(gameplayRoot, reused.transform.position) < .001f,
                "attack and hit poses never move the gameplay root or collider");

            // Invalid IDs, old packets and repeated packets must stay cheap and cannot resurrect presentation.
            closeup = false;
            world.ClearCombatVfxForVerification();
            state = Fixture("skeleton");
            state.effects.Add(new EffectState { type = "arrow", age = 0 });
            EffectState old = Event("explode", state, serial++); old.age = 2; state.effects.Add(old);
            world.SetState(state, "vfx-player-0");
            Require(world.CombatVfxSeenCount == 0 && world.CombatVfxActiveCount == 0,
                "invalid IDs and stale events are ignored");

            // Overload multiple event families in one frame and a second time after a clear.
            // Repeating the identical burst is intentional: it exercises both the fixed pool and ID reset.
            world.ClearCombatVfxForVerification();
            state = Fixture("blaze");
            string[] stressTypes = { "strike", "arrow", "burn", "poison", "slow", "stun", "shield", "heal", "teleport", "sweep", "knockback", "spawn", "explode", "death", "hit" };
            int burst = report.capacity * 3;
            for (int i = 0; i < burst; i++) state.effects.Add(Event(stressTypes[i % stressTypes.Length], state, serial++));
            world.SetState(state, "vfx-player-0"); ObservePool();
            int allocated = world.CombatVfxAllocatedCount;
            Require(world.CombatVfxActiveCount <= report.capacity && allocated <= report.capacity,
                "over-capacity mixed burst respects active and allocated pool bounds");
            int seen = world.CombatVfxSeenCount;
            world.SetState(state, "vfx-player-0"); ObservePool();
            Require(world.CombatVfxSeenCount == seen && world.CombatVfxAllocatedCount == allocated,
                "over-capacity repeated snapshot does not allocate or replay");
            yield return new WaitForSecondsRealtime(.12f);
            yield return new WaitForEndOfFrame();
            Capture("13-pool-stress");

            world.ClearCombatVfxForVerification();
            Require(world.CombatVfxActiveCount == 0 && world.CombatVfxSeenCount == 0,
                "explicit clear releases visual effects and event deduplication history");
            world.SetState(state, "vfx-player-0"); ObservePool();
            Require(world.CombatVfxActiveCount > 0 && world.CombatVfxAllocatedCount <= allocated,
                "replayed fixture after clear reuses the existing bounded pool");
            world.ClearCombatVfxForVerification();

            state = Fixture("guardian");
            state.effects.Add(Event("arrow", state, serial++));
            state.effects.Add(Event("heal", state, serial++));
            world.SetState(state, "vfx-player-0");
            yield return new WaitForSecondsRealtime(5);
            Require(world.CombatVfxActiveCount == 0, "effect objects return to the pool after their animation completes");
            world.Clear();
            Require(world.CombatVfxActiveCount == 0 && world.CombatVfxSeenCount == 0,
                "normal world clear releases all combat effects");

            // A normal full-arena view: four armies, six creatures each, and a plausible number of simultaneous events.
            closeup = false; world.ResetView();
            state = MixedArmiesFixture();
            world.SetState(state, "vfx-player-0");
            yield return new WaitForSecondsRealtime(1.2f);
            for (int i = 0; i < state.fighters.Count; i += 2)
            {
                FighterState source = state.fighters[i], target = NearestEnemy(source, state.fighters);
                state.effects.Add(Event(source.range > 70 ? "arrow" : "strike", source, target, serial++));
                source.attackTime = .12f;
                if (i % 6 == 0) state.effects.Add(Event("hit", source, target, serial++));
            }
            foreach (FighterState source in state.fighters)
            {
                FighterState target = NearestEnemy(source, state.fighters);
                if (source.kind == "blaze")
                { state.effects.Add(Event("burn", source, target, serial++)); target.burnTime = 3; }
                if (source.kind == "snow_golem")
                { state.effects.Add(Event("slow", source, target, serial++)); target.slowTime = 3; }
                if (source.kind == "cave_spider")
                { state.effects.Add(Event("poison", source, target, serial++)); target.poisonTime = 3; }
                if (source.kind == "allay")
                    state.effects.Add(Event("heal", source, state.fighters[0], serial++));
            }
            FighterState fallen = state.fighters[10]; fallen.dead = true; fallen.hp = 0;
            state.effects.Add(Event("death", fallen, fallen, serial++));
            FighterState creeper = state.fighters[18]; creeper.dead = true; creeper.hp = 0;
            state.effects.Add(Event("explode", creeper, creeper, serial++));
            state.effects.Add(Event("death", creeper, creeper, serial++));
            world.SetState(state, "vfx-player-0"); ObservePool();
            report.mixedArmies = 4; report.mixedFighters = state.fighters.Count; report.mixedEvents = state.effects.Count;
            Require(report.mixedFighters == 24 && report.mixedEvents < 32,
                "mixed-army scene uses 24 creatures and fewer than 32 spatially distributed combat events");
            Require(world.CombatVfxActiveCount > 0 && world.CombatVfxSeenCount == state.effects.Count,
                "all four armies present their mixed combat events through the normal snapshot path");
            yield return new WaitForSecondsRealtime(.1f);
            yield return new WaitForEndOfFrame();
            Capture("14-mixed-armies");
            Require(world.ViewCamera.orthographicSize > 12 && world.ZoomFactor == 1,
                "mixed-army capture uses the normal full-arena camera without a close-up override");
            Require(report.captures.Count == 14, "all fourteen requested visual verification captures were written");
            world.Clear();

            report.success = true;
            WriteReport();
            Debug.Log("ARENA_VFX_OK checks=" + report.checks.Count + " captures=" + report.captures.Count +
                " pool=" + report.peakAllocated + "/" + report.capacity + " output=" + folder);
            Restore();
            if (Array.IndexOf(args, "-vfx-exit") >= 0 || Array.IndexOf(args, "-smoke-exit") >= 0) Application.Quit(0);
        }

        GameState Fixture(string kind)
        {
            var state = new GameState { phase = "battle", round = 8, timeLeft = 30 };
            for (int i = 0; i < 4; i++) state.players.Add(new PlayerState {
                id = "vfx-player-" + i, name = "Visual fixture " + i, slot = i, hp = 20,
                steve = ArenaGame.LocalToWorld(i, 0, 385)
            });
            // ArenaWorld keys visuals by fighter ID; changing species requires a new ID just as in a real roster.
            state.fighters.Add(Fighter("vfx-source-" + kind, kind, 0, -58, 0));
            state.fighters.Add(Fighter("vfx-target", "zombie", 1, 45, 0));
            return state;
        }

        GameState MixedArmiesFixture()
        {
            GameState state = Fixture("zombie"); state.fighters.Clear();
            string[][] kinds = {
                new[] { "iron_golem", "wolf", "snow_golem", "allay", "zombie", "skeleton" },
                new[] { "blaze", "magma_cube", "piglin", "ghast", "strider", "wither_skeleton" },
                new[] { "enderman", "guardian", "shulker", "evoker", "vex", "cave_spider" },
                new[] { "creeper", "slime", "bee", "ravager", "witch", "skeleton" }
            };
            for (int slot = 0; slot < 4; slot++) for (int unit = 0; unit < 6; unit++)
            {
                float lateral = (unit % 3 - 1) * 78;
                float forward = unit < 3 ? 95 : 175;
                Vector3 position = Quaternion.Euler(0, -slot * 90, 0) * new Vector3(lateral, 0, forward);
                state.fighters.Add(Fighter("vfx-mixed-" + slot + "-" + unit, kinds[slot][unit], slot, position.x, position.z));
            }
            return state;
        }

        static FighterState NearestEnemy(FighterState source, List<FighterState> fighters)
        {
            FighterState nearest = null; float distance = float.MaxValue;
            foreach (FighterState candidate in fighters)
            {
                if (candidate.slot == source.slot || candidate.dead) continue;
                float dx = candidate.x - source.x, dy = candidate.y - source.y, squared = dx * dx + dy * dy;
                if (squared < distance) { distance = squared; nearest = candidate; }
            }
            return nearest;
        }

        VoxelCreature FindCreature(string unitId)
        {
            foreach (VoxelCreature creature in world.GetComponentsInChildren<VoxelCreature>())
                if (creature.UnitId == unitId) return creature;
            return null;
        }

        static FighterState Fighter(string id, string kind, int slot, float x, float y)
        {
            CreatureDefinition definition = CreatureCatalog.Get(kind);
            return new FighterState { id = id, unitId = id, ownerId = "vfx-player-" + slot,
                kind = kind, slot = slot, x = x, y = y, hp = definition.hp * .7f, maxHp = definition.hp,
                attack = definition.attack, attackSpeed = definition.attackSpeed, range = definition.range,
                speed = definition.speed, skills = definition.skills, scale = 1 };
        }

        static EffectState Event(string type, GameState state, int id)
        {
            return Event(type, state.fighters[0], state.fighters[1], id);
        }

        static EffectState Event(string type, FighterState source, FighterState target, int id)
        {
            bool atSource = type == "death" || type == "spawn" || type == "explode";
            return new EffectState { id = "vfx-fixture-" + id, type = type, sourceId = source.id,
                targetId = atSource ? source.id : target.id, kind = source.kind, slot = source.slot,
                x = type == "hit" ? target.x : source.x, y = type == "hit" ? target.y : source.y,
                targetX = atSource ? source.x : target.x, targetY = atSource ? source.y : target.y,
                value = type == "explode" ? 82 : type == "sweep" ? 65 : type == "slow" || type == "stun" ? 2 : 12 };
        }

        void ObservePool()
        {
            report.peakActive = Math.Max(report.peakActive, world.CombatVfxActiveCount);
            report.peakAllocated = Math.Max(report.peakAllocated, world.CombatVfxAllocatedCount);
            if (world.CombatVfxActiveCount > report.capacity || world.CombatVfxAllocatedCount > report.capacity)
                throw new InvalidOperationException("combat visual effect pool exceeded its configured capacity");
        }

        void Capture(string name)
        {
            string path = Path.Combine(folder, name + ".png");
            // Capture after WaitForEndOfFrame and write synchronously: the following fixture may clear the world.
            Texture2D pixels = ScreenCapture.CaptureScreenshotAsTexture();
            if (pixels == null) throw new InvalidOperationException("native frame readback returned no pixels");
            try
            {
                Color32[] colors = pixels.GetPixels32();
                var quality = new CaptureQuality { name = name, minimumLuminance = 1 };
                var distinct = new HashSet<int>();
                int nonBlack = 0;
                // A grid covers the arena and its edges without a costly full-image scan.
                for (int row = 0; row < 48; row++) for (int column = 0; column < 64; column++)
                {
                    int x = Mathf.Min(pixels.width - 1, (int)((column + .5f) * pixels.width / 64));
                    int y = Mathf.Min(pixels.height - 1, (int)((row + .5f) * pixels.height / 48));
                    Color32 color = colors[y * pixels.width + x];
                    float luminance = (.2126f * color.r + .7152f * color.g + .0722f * color.b) / 255;
                    quality.minimumLuminance = Mathf.Min(quality.minimumLuminance, luminance);
                    quality.maximumLuminance = Mathf.Max(quality.maximumLuminance, luminance);
                    if (color.a > 16 && luminance > .035f) nonBlack++;
                    distinct.Add((color.r >> 3) << 10 | (color.g >> 3) << 5 | (color.b >> 3));
                    quality.samples++;
                }
                quality.nonBlackFraction = (float)nonBlack / quality.samples;
                quality.distinctColors = distinct.Count;
                report.captureQuality.Add(quality);
                // Preserve a failed image as diagnostic evidence, but never count it as a successful capture.
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                Require(quality.nonBlackFraction > .2f &&
                    quality.maximumLuminance - quality.minimumLuminance > .15f && quality.distinctColors >= 16,
                    name + " frame has visible nonblack pixels and rendered detail" +
                    " (nonBlack=" + quality.nonBlackFraction.ToString("F3") +
                    ", luminanceRange=" + (quality.maximumLuminance - quality.minimumLuminance).ToString("F3") +
                    ", colors=" + quality.distinctColors + ")");
            }
            finally { Destroy(pixels); }
            report.captures.Add(path);
            Debug.Log("ARENA_VFX_CAPTURE " + path);
        }

        void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.checks.Add(message);
            Debug.Log("ARENA_VFX_CHECK " + message);
        }

        void WriteReport()
        {
            if (!string.IsNullOrEmpty(folder)) File.WriteAllText(Path.Combine(folder, "vfx-report.json"), JsonUtility.ToJson(report, true));
        }

        void Fail(string message)
        {
            failed = true; report.success = false; report.error = message;
            try { WriteReport(); } catch (Exception error) { Debug.LogError(error); }
            Restore();
            Debug.LogError("ARENA_VFX_FAILED " + message);
            Application.Quit(2);
        }

        void Restore()
        {
            if (restored || app == null) return;
            restored = true; closeup = false;
            app.VerificationInput = originalInput; app.VerificationPointer = originalPointer;
            app.VerificationState = originalVerificationState; app.enabled = originalEnabled;
        }

        void LateUpdate()
        {
            // Verification-only close-ups expose small pixels without changing the player's legal zoom range.
            if (closeup && world != null && world.ViewCamera != null) world.ViewCamera.orthographicSize = 5.8f;
        }

        void OnDestroy() { Restore(); }
    }
}
