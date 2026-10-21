using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class VoxelCreature
    {
        // These are visual reactions only. The server remains the owner of position, HP and damage.
        // Meshes are shared by species; no Renderer.material or per-frame material allocation is used.
        MeshFilter[] combatParts;
        Mesh[] combatNormalMeshes, combatImpactMeshes;
        Vector3[] combatRestPositions, combatRestScales, deathPartPositions, deathPartScales;
        Quaternion[] combatRestRotations, deathPartRotations;
        Vector3[] deathScatter;
        Vector3 combatAttackDirection, combatHitDirection;
        Vector3 deathFigurePosition, deathFigureScale;
        Quaternion deathFigureRotation;
        float combatAttackAge = 10, combatHitAge = 10, combatDeathAge;
        float combatLastAttack, combatLastHealth, combatHitStrength = 1;
        float combatDeathSide = 1;
        bool combatObserved, combatWasDead, combatFlashing, combatRanged;

        /// <summary>Rendered body anchor, including flying hover, interpolation and the current elastic pose.</summary>
        public Vector3 CombatEffectCenter
        {
            get { return figure == null ? transform.position + Vector3.up * (Height * Size * .55f)
                : figure.TransformPoint(Vector3.up * (Height * .55f)); }
        }

        /// <summary>Optional event direction; attacks are also detected from attackTime snapshots.</summary>
        public void PlayCombatAttack(Vector3 targetPosition, bool ranged = false)
        {
            if (Kind == "steve" || Dead || !Battle) return;
            Vector3 direction = targetPosition - transform.position;
            direction.y = 0;
            combatAttackDirection = direction.sqrMagnitude > .0001f ? direction.normalized : facing * Vector3.back;
            combatRanged = ranged || UsesRangedCombatPose();
            combatAttackAge = 0;
        }

        /// <summary>Optional event direction. This never moves the gameplay root or its collider.</summary>
        public void PlayCombatHit(Vector3 sourcePosition, float strength = 1)
        {
            if (Kind == "steve" || Dead || !Battle) return;
            Vector3 away = transform.position - sourcePosition;
            away.y = 0;
            combatHitDirection = away.sqrMagnitude > .0001f ? away.normalized : facing * Vector3.forward;
            combatHitStrength = Mathf.Clamp(strength, .35f, 1.65f);
            combatHitAge = 0;
        }

        bool UsesRangedCombatPose()
        {
            return BlockArena.Core.CreatureCatalog.Get(Kind).range > 70;
        }

        void BuildCombatFeedback()
        {
            if (Kind == "steve") return;
            combatParts = figure.GetComponentsInChildren<MeshFilter>();
            int count = combatParts.Length;
            combatNormalMeshes = new Mesh[count];
            combatImpactMeshes = new Mesh[count];
            combatRestPositions = new Vector3[count];
            combatRestRotations = new Quaternion[count];
            combatRestScales = new Vector3[count];
            deathPartPositions = new Vector3[count];
            deathPartRotations = new Quaternion[count];
            deathPartScales = new Vector3[count];
            deathScatter = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Transform part = combatParts[i].transform;
                combatNormalMeshes[i] = combatParts[i].sharedMesh;
                combatRestPositions[i] = part.localPosition;
                combatRestRotations[i] = part.localRotation;
                combatRestScales[i] = part.localScale;
                // A deterministic burst keeps death silhouettes clear without consuming simulation RNG.
                float angle = i * 2.399963f + Height;
                float spread = (.2f + (i % 4) * .085f) * Mathf.Clamp(modelFootprint, .4f, 1.2f);
                deathScatter[i] = new Vector3(Mathf.Cos(angle) * spread, .12f + (i % 3) * .07f, Mathf.Sin(angle) * spread);
                if (part.parent != figure) deathScatter[i] *= .35f;
            }
        }

        void ResetCombatFeedback()
        {
            combatAttackAge = combatHitAge = 10;
            combatDeathAge = combatLastAttack = 0;
            combatObserved = combatWasDead = combatRanged = false;
            combatAttackDirection = combatHitDirection = Vector3.zero;
            SetCombatFlash(false);
            if (combatParts == null) return;
            for (int i = 0; i < combatParts.Length; i++)
            {
                Transform part = combatParts[i].transform;
                part.localPosition = combatRestPositions[i];
                part.localRotation = combatRestRotations[i];
                part.localScale = combatRestScales[i];
            }
            if (figure != null) figure.gameObject.SetActive(true);
        }

        void ObserveCombatFeedback(float dt)
        {
            if (Kind == "steve") return;
            combatAttackAge = Mathf.Min(10, combatAttackAge + dt);
            combatHitAge = Mathf.Min(10, combatHitAge + dt);
            if (!Battle)
            {
                // Preparation and drag/drop retain their existing spring animation.
                if (combatObserved || combatWasDead) ResetCombatFeedback();
                return;
            }
            if (combatWasDead && !Dead) ResetCombatFeedback();
            if (!combatObserved)
            {
                combatObserved = true;
                combatLastHealth = Health;
            }
            if (!Dead)
            {
                if (Attack > combatLastAttack + .025f && combatAttackAge > .065f)
                    PlayCombatAttack(transform.position + facing * Vector3.back);
                if (Health < combatLastHealth - .001f && combatHitAge > .055f)
                {
                    float fraction = (combatLastHealth - Health) / Mathf.Max(1, MaximumHealth);
                    PlayCombatHit(transform.position + facing * Vector3.back, .65f + fraction * 3);
                }
            }
            else if (!combatWasDead)
            {
                combatDeathAge = 0;
                combatDeathSide = Mathf.Sin(seed) < 0 ? -1 : 1;
                deathFigurePosition = figure.localPosition;
                deathFigureRotation = figure.rotation;
                deathFigureScale = figure.localScale;
                for (int i = 0; i < combatParts.Length; i++)
                {
                    Transform part = combatParts[i].transform;
                    deathPartPositions[i] = part.localPosition;
                    deathPartRotations[i] = part.localRotation;
                    deathPartScales[i] = part.localScale;
                }
            }
            if (Dead && combatWasDead) combatDeathAge += dt;
            combatWasDead = Dead;
            combatLastAttack = Attack;
            combatLastHealth = Health;
        }

        void UpdateCombatFacing(float dt)
        {
            if (Kind == "steve" || !Battle || Dead || combatAttackAge > .24f || combatAttackDirection.sqrMagnitude < .01f) return;
            facing = Quaternion.Slerp(facing, Quaternion.LookRotation(-combatAttackDirection, Vector3.up), 1 - Mathf.Exp(-20 * dt));
        }

        float CombatStrike()
        {
            if (!Battle || Dead) return 0;
            float duration = modelFamily == "golem" || modelFamily == "warden" ? .43f : combatRanged ? .31f : .35f;
            float phase = combatAttackAge / duration;
            if (phase >= 1) return 0;
            if (phase < .16f) return -.23f * Mathf.Sin(phase / .16f * Mathf.PI);
            if (phase < .35f) return Mathf.Sin((phase - .16f) / .19f * Mathf.PI * .5f) * 1.12f;
            float recover = (phase - .35f) / .65f;
            return (1 - recover) * (1 - recover) * 1.12f;
        }

        void StepCombatPose(float strike)
        {
            if (Kind == "steve" || combatParts == null) return;
            if (Dead) { StepCombatDeath(); return; }
            SetCombatFlash(Battle && combatHitAge < .085f);
            if (!Battle) return;

            float attack = Mathf.Max(0, strike);
            float anticipation = Mathf.Min(0, strike);
            float thrust = attack * (combatRanged ? -.055f : .16f) + anticipation * .14f;
            Vector3 direction = combatAttackDirection.sqrMagnitude > .01f ? combatAttackDirection : facing * Vector3.back;
            figure.localPosition += direction * thrust;
            float stretch = attack * (combatRanged ? .045f : .085f) + anticipation * .18f;
            figure.localScale = Vector3.Scale(figure.localScale, new Vector3(1 - stretch * .48f, 1 + stretch, 1 - stretch * .48f));
            // Families with bespoke arm swings already receive the same strike envelope above.
            if (modelFamily != "zombie" && modelFamily != "skeleton" && modelFamily != "golem")
                for (int i = 0; i < arms.Count; i++)
                    arms[i].localRotation *= Quaternion.Euler(attack * (combatRanged ? -12 : 38), (i % 2 == 0 ? -1 : 1) * attack * 7, 0);

            if (combatHitAge < .34f)
            {
                float recoil = Mathf.Sin(Mathf.Clamp01(combatHitAge / .28f) * Mathf.PI) * Mathf.Exp(-combatHitAge * 5) * combatHitStrength;
                float press = Mathf.Max(0, 1 - combatHitAge / .1f) * combatHitStrength;
                figure.localPosition += combatHitDirection * (recoil * .135f) + Vector3.up * (recoil * .035f);
                figure.localScale = Vector3.Scale(figure.localScale, new Vector3(1 + press * .07f, 1 - press * .13f, 1 + press * .07f));
                Vector3 localDirection = Quaternion.Inverse(facing) * combatHitDirection;
                figure.rotation *= Quaternion.Euler(localDirection.z * recoil * 12, 0, -localDirection.x * recoil * 13);
                if (head != null) head.localRotation *= Quaternion.Euler(-recoil * 8, 0, recoil * 3);
            }
        }

        void StepCombatDeath()
        {
            float age = combatDeathAge;
            SetCombatFlash(age < .07f);
            // Hold the last readable pose briefly, tip over, then break apart into the shared death burst.
            float fall = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - .07f) / .29f));
            float vanish = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - .4f) / .4f));
            figure.localPosition = Vector3.Lerp(deathFigurePosition, new Vector3(0, .025f, 0), fall);
            figure.rotation = deathFigureRotation * Quaternion.Euler(fall * 14, fall * combatDeathSide * 8, fall * combatDeathSide * 77);
            float squash = Mathf.Sin(Mathf.Clamp01(age / .16f) * Mathf.PI) * .12f;
            figure.localScale = Vector3.Scale(deathFigureScale, new Vector3(1 + squash, 1 - squash, 1 + squash)) * Mathf.Max(.001f, 1 - vanish * .84f);
            for (int i = 0; i < combatParts.Length; i++)
            {
                Transform part = combatParts[i].transform;
                float scatter = Mathf.SmoothStep(0, 1, Mathf.Clamp01((age - .29f - i % 4 * .018f) / .4f));
                part.localPosition = deathPartPositions[i] + deathScatter[i] * scatter;
                part.localRotation = deathPartRotations[i] * Quaternion.Euler(scatter * (i % 2 == 0 ? 24 : -24), scatter * i * 9, scatter * combatDeathSide * 19);
                part.localScale = deathPartScales[i] * Mathf.Max(.001f, 1 - vanish);
            }
            if (age >= .81f) figure.gameObject.SetActive(false);
        }

        void SetCombatFlash(bool visible)
        {
            if (combatFlashing == visible || combatParts == null) return;
            combatFlashing = visible;
            for (int i = 0; i < combatParts.Length; i++)
            {
                if (visible && combatImpactMeshes[i] == null)
                    combatImpactMeshes[i] = VoxelAssets.ImpactTint(combatNormalMeshes[i]);
                combatParts[i].sharedMesh = visible ? combatImpactMeshes[i] : combatNormalMeshes[i];
            }
        }
    }
}
