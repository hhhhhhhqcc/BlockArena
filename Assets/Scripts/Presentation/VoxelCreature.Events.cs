using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class VoxelCreature
    {
        Transform bountyMark, neutralAura, neutralGold, neutralCoin;
        string neutralEventKind;
        bool bountyMarked, neutralEventActive;
        float neutralBornAt = -10;
        CapsuleCollider eventCollider;
        Vector3 eventColliderCenter;
        float eventColliderHeight, eventColliderRadius;

        internal bool BountyMarked { get { return bountyMarked && !Dead && gameObject.activeInHierarchy; } }
        internal bool NeutralEventActive { get { return neutralEventKind != null && neutralEventActive && !Dead; } }
        internal bool GoldResourceVisible { get { return NeutralEventActive && neutralEventKind == "gold" && neutralGold != null && neutralGold.gameObject.activeInHierarchy; } }

        internal void SetBountyMarked(bool value)
        {
            bountyMarked = value && Kind != "steve" && !Dead;
            if (bountyMarked && bountyMark == null) {
                bountyMark = VoxelAssets.Part(transform, "Bounty original-unit amber insignia", VoxelAssets.Cached("bounty/original-unit/" + Kind, b => {
                    Color amber = VoxelAssets.Hex("#EDAC42"), pale = VoxelAssets.Hex("#FFF0AF");
                    float radius = Mathf.Clamp(modelFootprint + .13f, .48f, 1.5f);
                    for (int i = 0; i < 4; i++) {
                        Quaternion q = Quaternion.Euler(0, i * 90 + 45, 0);
                        b.Box(q * new Vector3(0, .035f, radius), new Vector3(.13f, .035f, .065f), amber, q);
                    }
                    b.Box(new Vector3(.55f, Height + modelHover + .25f, 0), new Vector3(.13f, .13f, .13f), amber, Quaternion.Euler(25, 45, 35));
                    b.Box(new Vector3(.55f, Height + modelHover + .31f, 0), new Vector3(.055f, .055f, .055f), pale, Quaternion.Euler(25, 45, 35));
                }), Vector3.zero, VoxelAssets.Glow, false);
            }
            if (bountyMark != null) bountyMark.gameObject.SetActive(bountyMarked);
        }

        internal void SetNeutralEventVisual(string kind, bool active, float bornAt)
        {
            if (kind != "boss" && kind != "gold") {
                if (neutralEventKind != null) ResetArenaEventFeedback();
                return;
            }
            neutralEventKind = kind; neutralEventActive = active && !Dead; neutralBornAt = bornAt;
            if (eventCollider == null) {
                eventCollider = GetComponent<CapsuleCollider>();
                if (eventCollider != null) {
                    eventColliderCenter = eventCollider.center;
                    eventColliderHeight = eventCollider.height; eventColliderRadius = eventCollider.radius;
                }
            }
            if (neutralAura == null) {
                neutralAura = VoxelAssets.Part(transform, "Neutral objective sun rune", VoxelAssets.Cached("event/neutral-sun-rune", b => {
                    Color gold = VoxelAssets.Hex("#F5C75D"), dark = VoxelAssets.Hex("#A87B3C");
                    b.Ring(1.1f, 1.04f, gold); b.Ring(.94f, .91f, dark);
                    for (int i = 0; i < 8; i++) {
                        Quaternion q = Quaternion.Euler(0, i * 45, 0);
                        b.Box(q * new Vector3(0, .025f, 1.19f), new Vector3(.12f, .035f, .22f), gold, q);
                    }
                }), new Vector3(0, .045f, 0), VoxelAssets.Glow, false);
            }
            if (kind == "gold") EnsureGoldResource();
            ApplyNeutralVisibility();
        }

        void EnsureGoldResource()
        {
            if (neutralGold != null) return;
            neutralGold = new GameObject("Central floating gold cache").transform;
            neutralGold.SetParent(transform, false);
            VoxelAssets.Part(neutralGold, "Obsidian and bronze treasure pedestal", VoxelAssets.Cached("event/gold-pedestal", b => {
                Color stone = VoxelAssets.Hex("#403B42"), bronze = VoxelAssets.Hex("#976239"), gold = VoxelAssets.Hex("#E9BB53");
                b.Disc(.71f, .03f, .19f, 8, stone, bronze);
                b.Disc(.54f, .19f, .32f, 8, bronze, stone);
                b.Ring(.56f, .49f, gold);
                for (int i = 0; i < 4; i++) {
                    Quaternion q = Quaternion.Euler(0, i * 90 + 45, 0);
                    b.Box(q * new Vector3(0, .39f, .38f), new Vector3(.16f, .24f, .16f), gold, q);
                }
                b.Box(new Vector3(-.25f, .33f, -.12f), new Vector3(.34f, .13f, .17f), gold, Quaternion.Euler(0, -20, 0));
                b.Box(new Vector3(.2f, .35f, .06f), new Vector3(.35f, .14f, .17f), VoxelAssets.Hex("#FFE89A"), Quaternion.Euler(0, 25, 0));
            }), Vector3.zero, null, true);
            neutralCoin = VoxelAssets.Part(neutralGold, "Spinning eight-sided gold coin", VoxelAssets.Cached("event/floating-coin", b => {
                for (int y = -3; y <= 3; y++) for (int x = -3; x <= 3; x++) {
                    if (Mathf.Abs(x) + Mathf.Abs(y) > 5) continue;
                    bool edge = Mathf.Abs(x) == 3 || Mathf.Abs(y) == 3 || Mathf.Abs(x) + Mathf.Abs(y) == 5;
                    b.Box(new Vector3(x * .14f, y * .14f, 0), new Vector3(.14f, .14f, .15f), VoxelAssets.Hex(edge ? "#D68D37" : "#F8CE61"));
                }
                Color mark = VoxelAssets.Hex("#FFF0AB");
                for (int side = -1; side <= 1; side += 2) {
                    b.Box(new Vector3(0, 0, side * .086f), new Vector3(.075f, .53f, .025f), mark);
                    b.Box(new Vector3(0, .17f, side * .088f), new Vector3(.32f, .07f, .025f), mark);
                    b.Box(new Vector3(0, -.17f, side * .088f), new Vector3(.32f, .07f, .025f), mark);
                    b.Box(new Vector3(-.12f, .08f, side * .088f), new Vector3(.07f, .16f, .025f), mark);
                    b.Box(new Vector3(.12f, -.08f, side * .088f), new Vector3(.07f, .16f, .025f), mark);
                }
            }), Vector3.up * 1.05f, VoxelAssets.Glow, false);
        }

        void ApplyNeutralVisibility()
        {
            if (neutralEventKind == null) return;
            bool alive = neutralEventActive && !Dead;
            figure.gameObject.SetActive(alive && neutralEventKind != "gold");
            if (neutralGold != null) neutralGold.gameObject.SetActive(alive && neutralEventKind == "gold");
            if (neutralAura != null) neutralAura.gameObject.SetActive(alive);
            teamRing.gameObject.SetActive(false);
            if (eventCollider != null) {
                eventCollider.enabled = alive;
                eventCollider.center = neutralEventKind == "gold" ? new Vector3(0, .9f, 0) : eventColliderCenter;
                eventCollider.height = neutralEventKind == "gold" ? 1.85f : eventColliderHeight;
                eventCollider.radius = neutralEventKind == "gold" ? .62f : eventColliderRadius;
            }
            if (!alive) healthRoot.gameObject.SetActive(false);
        }

        void StepArenaEventFeedback(float time)
        {
            if (bountyMark != null) {
                bountyMark.gameObject.SetActive(bountyMarked && !Dead);
                if (bountyMarked) bountyMark.localPosition = Vector3.up * (.015f + Mathf.Sin(time * 3 + seed) * .009f);
            }
            if (neutralEventKind == null) return;
            ApplyNeutralVisibility();
            if (!neutralEventActive || Dead) return;
            float age = Mathf.Max(0, time - neutralBornAt);
            float pop = Mathf.Clamp(1 - Mathf.Exp(-age * 7) * Mathf.Cos(age * 12), .02f, 1.14f);
            neutralAura.localScale = Vector3.one * (1 + Mathf.Sin(time * 2.5f) * .045f);
            neutralAura.localRotation = Quaternion.Euler(0, time * 10, 0);
            if (neutralEventKind == "gold") {
                neutralGold.localScale = new Vector3(.9f + pop * .1f, pop, .9f + pop * .1f);
                neutralCoin.localPosition = Vector3.up * (1.05f + Mathf.Sin(time * 2.7f) * .07f);
                neutralCoin.localRotation = Quaternion.Euler(0, time * 48, 0);
                if (healthRoot.gameObject.activeSelf) healthRoot.position = transform.position + Vector3.up * (1.82f * Size + .23f);
            }
            else {
                figure.localScale = Vector3.Scale(figure.localScale, new Vector3(1 + (1 - pop) * .12f, pop, 1 + (1 - pop) * .12f));
                figure.localPosition += Vector3.down * (1 - Mathf.Clamp01(age / .7f)) * .7f;
            }
        }

        void ResetArenaEventFeedback()
        {
            bountyMarked = false; neutralEventKind = null; neutralEventActive = false; neutralBornAt = -10;
            if (bountyMark != null) bountyMark.gameObject.SetActive(false);
            if (neutralAura != null) neutralAura.gameObject.SetActive(false);
            if (neutralGold != null) neutralGold.gameObject.SetActive(false);
            if (eventCollider != null) {
                eventCollider.enabled = true; eventCollider.center = eventColliderCenter;
                eventCollider.height = eventColliderHeight; eventCollider.radius = eventColliderRadius;
            }
            if (figure != null && Kind != "steve") figure.gameObject.SetActive(true);
        }
    }
}
