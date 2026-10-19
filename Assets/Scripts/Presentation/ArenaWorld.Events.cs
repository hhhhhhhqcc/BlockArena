using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Presentation
{
    public sealed partial class ArenaWorld
    {
        Transform leaderCrown, leaderRunes, eventArrival, eventArrivalRing;
        readonly Transform[] eventArrivalPillars = new Transform[8];
        string eventObservedId, eventObservedViewer, eventClaimedBy;
        int eventObservedRound = -1, eventArrivalCount, eventClaimCount, bountyMarkedCount;
        bool eventWasActive;
        float eventBornAt = -10, eventArrivalAt = -10;
        Vector3 eventLocation;

        internal bool LeaderBountyVisible { get { return leaderCrown != null && leaderCrown.gameObject.activeInHierarchy; } }
        internal int BountyMarkedCreatureCount { get { return bountyMarkedCount; } }
        internal int ArenaEventArrivalCount { get { return eventArrivalCount; } }
        internal int ArenaEventClaimCount { get { return eventClaimCount; } }
        internal bool ArenaEventArrivalVisible { get { return eventArrival != null && eventArrival.gameObject.activeInHierarchy; } }
        internal int ActiveNeutralEventCount
        {
            get { int count = 0; foreach (VoxelCreature creature in creatures.Values) if (creature.NeutralEventActive) count++; return count; }
        }
        internal bool GoldResourceVisible
        {
            get { foreach (VoxelCreature creature in creatures.Values) if (creature.GoldResourceVisible) return true; return false; }
        }

        void SyncArenaEventPresentation(GameState next, string viewerId)
        {
            bool baseline = eventObservedRound < 0 || eventObservedViewer != viewerId || next.round < eventObservedRound || next.phase == "lobby";
            ArenaEventState roundEvent = next.roundEvent;
            string eventId = roundEvent == null ? null : roundEvent.id;
            bool active = roundEvent != null && roundEvent.active && next.phase == "battle" &&
                (roundEvent.kind == "boss" || roundEvent.kind == "gold");
            if (roundEvent != null) eventLocation = Position(roundEvent.x, roundEvent.y);
            if (active && (!eventWasActive || eventId != eventObservedId)) {
                eventBornAt = baseline ? Time.unscaledTime - 2 : Time.unscaledTime;
                if (!baseline) StartArenaEventArrival(eventLocation);
            }
            bool battle = next.phase == "battle" || next.phase == "results" || next.phase == "finished";
            PlayerState leader = next.players.Find(p => p.id == next.leaderId && !p.eliminated);
            bountyMarkedCount = 0;
            foreach (VoxelCreature creature in creatures.Values) {
                FighterState fighter;
                bool marked = leader != null && !creature.Dead && creature.Kind != "steve" &&
                    (battle ? fighterViews.TryGetValue(creature, out fighter) && fighter.ownerId == leader.id && !fighter.neutral && !fighter.summoned :
                    !creature.Battle && creature.Slot == leader.slot);
                creature.SetBountyMarked(marked);
                if (marked) bountyMarkedCount++;
            }
            if (battle) {
                foreach (var pair in fighterViews) {
                    FighterState fighter = pair.Value;
                    bool eventFighter = fighter.neutral;
                    bool eventMatches = eventFighter && roundEvent != null && fighter.id == roundEvent.fighterId;
                    if (eventMatches) eventLocation = pair.Key.Target;
                    pair.Key.SetNeutralEventVisual(eventFighter ? eventMatches ? roundEvent.kind : "boss" : null,
                        active && eventMatches && !fighter.dead, eventBornAt);
                }
            }
            if (!baseline && roundEvent != null && !string.IsNullOrEmpty(roundEvent.claimedBy) &&
                (eventId != eventObservedId || eventClaimedBy != roundEvent.claimedBy)) {
                EmitArenaEventClaim(eventLocation, roundEvent.claimedBy); eventClaimCount++;
            }
            if (!active && eventArrival != null)
                eventArrival.gameObject.SetActive(false);
            eventObservedRound = next.round; eventObservedViewer = viewerId; eventObservedId = eventId;
            eventWasActive = active; eventClaimedBy = roundEvent == null ? null : roundEvent.claimedBy;
            UpdateLeaderBounty(Time.unscaledTime);
        }

        void EnsureLeaderBounty()
        {
            if (leaderCrown != null) return;
            leaderCrown = NewRoot("Leader bounty floating crown", effectRoot);
            VoxelAssets.Part(leaderCrown, "Amber crown and suspended reward gem", VoxelAssets.Cached("bounty/commander-crown", b => {
                Color gold = VoxelAssets.Hex("#F0B947"), edge = VoxelAssets.Hex("#A16A32"), pale = VoxelAssets.Hex("#FFF0AD");
                b.Box(Vector3.zero, new Vector3(1.03f, .16f, .72f), edge);
                for (int side = -1; side <= 1; side += 2) {
                    b.Box(new Vector3(0, .08f, side * .35f), new Vector3(1.06f, .22f, .1f), gold);
                    b.Box(new Vector3(side * .48f, .08f, 0), new Vector3(.1f, .22f, .72f), gold);
                    for (int spike = -1; spike <= 1; spike++) {
                        float h = spike == 0 ? .42f : .28f;
                        b.Box(new Vector3(spike * .43f, .16f + h * .5f, side * .34f), new Vector3(.14f, h, .13f), gold);
                        b.Box(new Vector3(spike * .43f, .19f + h, side * .34f), Vector3.one * .13f, pale, Quaternion.Euler(0, 45, 0));
                    }
                }
                b.Box(new Vector3(0, .73f, 0), new Vector3(.25f, .3f, .25f), pale, Quaternion.Euler(0, 45, 25));
            }), Vector3.zero, VoxelAssets.Glow, false);
            leaderRunes = VoxelAssets.Part(effectRoot, "Leader bounty moving ground runes", VoxelAssets.Cached("bounty/commander-runes", b => {
                Color gold = VoxelAssets.Hex("#E5A441"), pale = VoxelAssets.Hex("#FFDC7A");
                b.Ring(1.23f, 1.18f, gold);
                for (int i = 0; i < 8; i++) {
                    Quaternion q = Quaternion.Euler(0, i * 45, 0);
                    b.Box(q * new Vector3(0, .02f, 1.37f), new Vector3(.11f, .04f, .23f), pale, q);
                    b.Box(q * new Vector3(0, .025f, 1.54f), new Vector3(.23f, .04f, .07f), gold, q);
                }
            }), Vector3.zero, VoxelAssets.Glow, false);
        }

        void UpdateLeaderBounty(float time)
        {
            VoxelCreature commander;
            bool visible = state != null && state.phase != "lobby" && !string.IsNullOrEmpty(state.leaderId) &&
                creatures.TryGetValue("steve:" + state.leaderId, out commander) && commander.CommanderFigureVisible;
            // Definite assignment is kept separate from the boolean chain for older Unity compilers.
            if (!visible || !creatures.TryGetValue("steve:" + state.leaderId, out commander)) {
                if (leaderCrown != null) leaderCrown.gameObject.SetActive(false);
                if (leaderRunes != null) leaderRunes.gameObject.SetActive(false);
                return;
            }
            EnsureLeaderBounty(); leaderCrown.gameObject.SetActive(true); leaderRunes.gameObject.SetActive(true);
            leaderCrown.position = commander.transform.position + Vector3.up * (commander.Height * commander.Size + .36f + Mathf.Sin(time * 2.8f) * .1f);
            leaderCrown.rotation = Quaternion.Euler(0, time * 17, Mathf.Sin(time * 1.9f) * 3);
            leaderCrown.localScale = Vector3.one * (1 + Mathf.Sin(time * 2.8f) * .025f);
            leaderRunes.position = commander.transform.position + Vector3.up * .08f;
            leaderRunes.rotation = Quaternion.Euler(0, -time * 13, 0);
            leaderRunes.localScale = Vector3.one * (1 + Mathf.Sin(time * 3.3f) * .035f);
        }

        void StartArenaEventArrival(Vector3 at)
        {
            if (eventArrival == null) {
                eventArrival = NewRoot("Central objective arrival seal", effectRoot);
                eventArrivalRing = VoxelAssets.Part(eventArrival, "Expanding amber arrival shockwave", VoxelAssets.Cached("event/arrival-shockwave", b => {
                    b.Ring(1, .95f, VoxelAssets.Hex("#FFE49C"));
                    b.Ring(.79f, .765f, VoxelAssets.Hex("#E5A347"));
                }), Vector3.up * .1f, VoxelAssets.Glow, false);
                for (int i = 0; i < eventArrivalPillars.Length; i++)
                    eventArrivalPillars[i] = VoxelAssets.Part(eventArrival, "Ascending gold rune " + i,
                        VoxelAssets.Cached("event/arrival-rune", b => {
                            Color gold = VoxelAssets.Hex("#FFE4A0");
                            b.Box(Vector3.zero, new Vector3(.11f, 1, .11f), gold);
                            b.Box(Vector3.up * .25f, new Vector3(.3f, .07f, .1f), gold);
                            b.Box(Vector3.down * .25f, new Vector3(.3f, .07f, .1f), gold);
                        }), Vector3.zero, VoxelAssets.Glow, false);
            }
            eventArrivalAt = Time.unscaledTime; eventArrivalCount++;
            eventArrival.position = at; eventArrival.gameObject.SetActive(true);
            for (int i = 0; i < 36; i++) {
                float angle = i * Mathf.PI * 2 / 36;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                EmitRitualParticle(at + direction * 1.3f + Vector3.up * .2f,
                    direction * 1.3f + Vector3.up * (1.5f + i % 3), VoxelAssets.Hex(i % 3 == 0 ? "#FFF0B2" : "#F1B447"), .12f, .85f);
            }
        }

        void EmitArenaEventClaim(Vector3 at, string claimant)
        {
            for (int i = 0; i < 28; i++) {
                float angle = i * Mathf.PI * 2 / 28;
                EmitRitualParticle(at + Vector3.up * .8f, new Vector3(Mathf.Cos(angle) * 2, 2.4f + i % 3 * .35f, Mathf.Sin(angle) * 2),
                    VoxelAssets.Hex(i % 2 == 0 ? "#FFEAA0" : "#E3A346"), .14f, .65f);
            }
            VoxelCreature commander;
            if (!creatures.TryGetValue("steve:" + claimant, out commander)) return;
            Mesh coin = VoxelAssets.Cached("event/reward-coin-flight", b => {
                b.Box(Vector3.zero, new Vector3(.27f, .27f, .09f), VoxelAssets.Hex("#F8CF61"), Quaternion.Euler(0, 0, 45));
                b.Box(new Vector3(0, 0, -.06f), new Vector3(.055f, .22f, .025f), VoxelAssets.Hex("#FFF1AD"));
            });
            for (int i = 0; i < 5; i++) {
                FlyingEffect token = EffectObject();
                token.start = at + Vector3.up * (1 + i * .15f); token.end = commander.Target + Vector3.up * 1.4f;
                token.duration = .65f + i * .08f; token.filter.sharedMesh = coin; token.size = 1.6f;
            }
        }

        void UpdateArenaEventPresentation(float time)
        {
            UpdateLeaderBounty(time);
            if (eventArrival == null || !eventArrival.gameObject.activeSelf) return;
            float age = time - eventArrivalAt;
            if (age >= 1.15f) { eventArrival.gameObject.SetActive(false); return; }
            float life = Mathf.Clamp01(age / 1.15f);
            eventArrivalRing.localScale = Vector3.one * (.6f + life * 4.4f);
            eventArrivalRing.localPosition = Vector3.up * (.08f + life * .05f);
            float width = Mathf.Clamp01((1 - life) * 3);
            eventArrivalRing.localScale = new Vector3(eventArrivalRing.localScale.x, width, eventArrivalRing.localScale.z);
            for (int i = 0; i < eventArrivalPillars.Length; i++) {
                float angle = (i * 45 + age * 24) * Mathf.Deg2Rad;
                Transform pillar = eventArrivalPillars[i];
                pillar.localPosition = new Vector3(Mathf.Cos(angle) * 2, .3f + life * 1.7f, Mathf.Sin(angle) * 2);
                pillar.localRotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
                pillar.localScale = new Vector3(width, Mathf.Sin(life * Mathf.PI) * 1.3f, width);
            }
        }

        void ResetArenaEventPresentation()
        {
            if (leaderCrown != null) leaderCrown.gameObject.SetActive(false);
            if (leaderRunes != null) leaderRunes.gameObject.SetActive(false);
            if (eventArrival != null) eventArrival.gameObject.SetActive(false);
            eventObservedId = eventObservedViewer = eventClaimedBy = null;
            eventObservedRound = -1; eventWasActive = false; eventBornAt = eventArrivalAt = -10;
            eventArrivalCount = eventClaimCount = bountyMarkedCount = 0;
        }
    }
}
