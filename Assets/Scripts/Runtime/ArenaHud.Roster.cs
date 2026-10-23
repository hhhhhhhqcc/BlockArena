using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        // Keep the saved world view clear outside combat. All rectangles below use
        // the same logical GUI space as Health(), including during the slide.
        float rosterVisibility, rosterVelocity, rosterHoverUntil = -1;
        int rosterFrame = -1;

        bool RosterAvailable { get { return app.State != null && app.State.players != null && app.State.players.Count > 0; } }
        internal bool RosterPinned { get { return RosterAvailable && app.State.phase == "battle"; } }
        internal float RosterVisibility { get { return rosterVisibility; } }
        Rect RosterRect
        {
            get { return new Rect(Mathf.Lerp(width + 8, width - 344, rosterVisibility), 83, 326, 240); }
        }
        Rect RosterEdgeRect { get { return new Rect(width - 24, 83, 24, 240); } }
        Rect VisibleRosterRect
        {
            get {
                if (!RosterAvailable) return Rect.zero;
                Rect r = RosterRect;
                float left = Mathf.Clamp(r.x, 0, width), right = Mathf.Clamp(r.xMax, 0, width);
                return new Rect(left, r.y, Mathf.Max(0, right - left), Mathf.Min(r.height, Mathf.Max(0, height - r.y)));
            }
        }
        internal Rect RosterScreenRect { get { Metrics(); return ToScreenRect(VisibleRosterRect); } }
        internal Rect RosterEdgeScreenRect { get { Metrics(); return ToScreenRect(RosterAvailable ? RosterEdgeRect : Rect.zero); } }

        void ResetRoster()
        {
            rosterVisibility = rosterVelocity = 0;
            rosterHoverUntil = -1;
            rosterFrame = -1;
        }

        bool RosterPointerCovered(Vector2 p)
        {
            if (app.PlacementDragging) return true;
            if (EconomyCovers(p)) return true;
            // Full-screen overlays own their shade as well as their central panel.
            if (app.ShowGameMenu || app.ShowRoom || app.ShowBestiary) return true;
            if (RecruitVisible || PinnedInspectionCovers(p)) return true;
            if (marketDragging || marketPointerOwned || audioPointerOwned || skillPointerOwned) return true;
            if (AudioCovers(p) || SkillUiCovers(p)) return true;
            return MarketVisible && MarketRect.Contains(p);
        }

        void UpdateRoster()
        {
            if (rosterFrame == Time.frameCount) return;
            rosterFrame = Time.frameCount;
            Metrics();
            if (!RosterAvailable) {
                rosterVisibility = rosterVelocity = 0; rosterHoverUntil = -1;
                return;
            }
            Vector2 pointer = HudPointer;
            Vector2 p = new Vector2(pointer.x / scale, (Screen.height - pointer.y) / scale);
            bool covered = RosterPointerCovered(p);
            bool hover = !covered && (RosterEdgeRect.Contains(p) || VisibleRosterRect.Contains(p));
            if (hover) rosterHoverUntil = Time.unscaledTime + .5f;
            float target = RosterPinned || (!covered && Time.unscaledTime < rosterHoverUntil) ? 1 : 0;
            rosterVisibility = Mathf.SmoothDamp(rosterVisibility, target, ref rosterVelocity,
                target > rosterVisibility ? .16f : .24f, Mathf.Infinity, Mathf.Clamp(Time.unscaledDeltaTime, 0, .1f));
            rosterVisibility = Mathf.Clamp01(rosterVisibility);
            if (Mathf.Abs(rosterVisibility - target) < .001f && Mathf.Abs(rosterVelocity) < .01f) {
                rosterVisibility = target; rosterVelocity = 0;
            }
        }

        bool RosterCovers(Vector2 guiPoint)
        {
            if (!RosterAvailable) return false;
            // A collapsed roster must not reserve its old 326 px of world input.
            return VisibleRosterRect.Contains(guiPoint) || RosterEdgeRect.Contains(guiPoint);
        }

        void DrawRosterTab()
        {
            if (!RosterAvailable) return;
            Rect rail = RosterEdgeRect;
            float alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, 1, rosterVisibility));
            if (alpha < .01f) return;
            Fill(rail, new Color(.035f, .075f, .085f, .88f * alpha));
            Fill(new Rect(rail.x, rail.y + 3, 2, rail.height - 6), new Color(.38f, .87f, .73f, .45f * alpha));
            foreach (var player in app.State.players) {
                PlayerVisual motion; playerVisuals.TryGetValue(player.id, out motion);
                float shownHp = motion == null ? player.hp : motion.displayHp;
                bool shownEliminated = player.eliminated && shownHp <= 0;
                Color team = Team(player.slot); team.a = alpha * (shownEliminated ? .35f : 1);
                float y = rail.y + player.slot * 60 + 12;
                Fill(new Rect(rail.x + 8, y, 8, 5), team);
                Rect hp = new Rect(rail.x + 8, y + 9, 8, 23);
                Fill(hp, new Color(.015f, .03f, .035f, .9f * alpha));
                float hpHeight = hp.height * Mathf.Clamp01(shownHp / 20f);
                Fill(new Rect(hp.x, hp.yMax - hpHeight, hp.width, hpHeight), team);
            }
            // Small pixel chevron makes the hover strip discoverable without text.
            Color hint = new Color(.94f, .96f, .91f, .72f * alpha);
            Fill(new Rect(rail.x + 3, rail.center.y - 2, 2, 4), hint);
            Fill(new Rect(rail.x + 5, rail.center.y - 4, 2, 2), hint);
            Fill(new Rect(rail.x + 5, rail.center.y + 2, 2, 2), hint);
            if (!RosterPinned && !RosterPointerCovered(Mouse())) RegisterPointerRect(rail);
        }
    }
}
