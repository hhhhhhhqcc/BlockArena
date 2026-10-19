using System;
using System.Collections.Generic;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        readonly Dictionary<string, List<CreatureDefinition>> skillSpecies = new Dictionary<string, List<CreatureDefinition>>();
        readonly Dictionary<string, int> armySpeciesCounts = new Dictionary<string, int>();
        readonly List<string> ownedSkills = new List<string>();
        int skillCountsFrame = -1, skillPointerFrame = -1, skillReleasedFrame = -10, availableSkills;
        bool skillPointerOwned, drawingSkills;
        string detailSkillId;
        Vector2 ownedSkillScroll, skillDetailScroll;
        public bool ShowOwnedSkills { get; set; }
        public bool HasSkillDetails { get { return !string.IsNullOrEmpty(detailSkillId); } }
        Vector2 HudPointer { get { return app.VerificationInput && app.VerificationPointer.HasValue ? app.VerificationPointer.Value : (Vector2)Input.mousePosition; } }
        bool SkillUiAvailable { get { return app.State != null && app.Me != null && app.State.phase != "lobby" && !ModalOpen; } }
        Rect OwnedSkillToggleRect { get { return new Rect(18, height - 64, 220, 40); } }
        Rect OwnedSkillPanelRect { get { return new Rect(18, Mathf.Max(80, height - 422), 344, 344); } }
        Rect OwnedSkillViewport { get { Rect r = OwnedSkillPanelRect; return new Rect(r.x + 12, r.y + 55, r.width - 24, r.height - 69); } }
        Rect SkillDetailsRect
        {
            get {
                float w = 388, h = Mathf.Min(560, height - 110);
                float x = ShowOwnedSkills ? OwnedSkillPanelRect.xMax + 12 : 18, y = height - h - 90;
                if (app.ShowShop && MarketVisible) {
                    Rect shop = MarketRect; x = shop.x - w - 12; y = shop.y;
                    if (x < 12) x = shop.xMax + 12;
                }
                return new Rect(Mathf.Clamp(x, 12, Mathf.Max(12, width - w - 12)), Mathf.Clamp(y, 80, Mathf.Max(80, height - h - 18)), w, h);
            }
        }
        Rect MarketSkillCardRect(int index) { Rect r = MarketRect; return new Rect(r.x + 14 + (index % 2) * 229, r.y + 88 + (index / 2) * 147, 215, 137); }
        Rect ToScreenRect(Rect r) { return new Rect(r.x * scale, Screen.height - r.yMax * scale, r.width * scale, r.height * scale); }
        internal Rect FirstSkillCardScreenRect { get { Metrics(); return ToScreenRect(MarketSkillCardRect(0)); } }
        internal Rect SkillDetailsScreenRect { get { Metrics(); return ToScreenRect(SkillDetailsRect); } }
        internal Rect OwnedSkillsScreenRect { get { Metrics(); return ToScreenRect(OwnedSkillPanelRect); } }
        internal int SkillDetailSpeciesCount { get { return HasSkillDetails ? ApplicableSpecies(TradeCatalog.Get(detailSkillId)).Count : 0; } }
        internal int SkillDetailMatchingUnits { get { return HasSkillDetails ? MatchingArmyCount(TradeCatalog.Get(detailSkillId)) : 0; } }
        internal void PreviewSkillForVerification(string skillId) { OpenSkillDetails(skillId); }
        public void CloseSkillDetails() { detailSkillId = null; skillDetailScroll = Vector2.zero; ClearHover(); }
        void OpenSkillDetails(string skillId)
        {
            TradeDefinition item = TradeCatalog.Get(skillId); if (item.type != "skill") return;
            if (detailSkillId != skillId) skillDetailScroll = Vector2.zero;
            detailSkillId = skillId; ClearHover();
        }

        // Queried by the world every frame, so moving off a card clears the highlight immediately.
        public string HighlightedSkillId
        {
            get {
                Metrics();
                if (!SkillUiAvailable || marketDragging || audioPointerOwned) return null;
                Vector2 p = Mouse();
                if (AudioCovers(p)) return null;
                if (HasSkillDetails && SkillDetailsRect.Contains(p)) return detailSkillId;
                if (ShowOwnedSkills && OwnedSkillPanelRect.Contains(p)) {
                    RefreshSkillCounts(); Rect viewport = OwnedSkillViewport;
                    if (!viewport.Contains(p) || p.x > viewport.xMax - 18) return null;
                    float rowY = p.y - viewport.y + ownedSkillScroll.y;
                    int index = Mathf.FloorToInt(rowY / 88);
                    return index >= 0 && index < ownedSkills.Count && rowY - index * 88 <= 82 ? ownedSkills[index] : null;
                }
                if (app.ShowShop && MarketVisible && app.Me.market != null) {
                    for (int i = 0; i < Mathf.Min(4, app.Me.market.Count); i++)
                        if (MarketSkillCardRect(i).Contains(p) && TradeCatalog.Get(app.Me.market[i].itemId).type == "skill") return app.Me.market[i].itemId;
                }
                return null;
            }
        }
        bool SkillUiCovers(Vector2 p)
        {
            return SkillUiAvailable && (OwnedSkillToggleRect.Contains(p) || (ShowOwnedSkills && OwnedSkillPanelRect.Contains(p)) || (HasSkillDetails && SkillDetailsRect.Contains(p)));
        }
        bool SkillBlocksUnderlying { get { return skillPointerOwned || SkillUiCovers(Mouse()); } }
        void UpdateSkillPointer()
        {
            if (skillPointerFrame == Time.frameCount || app.VerificationInput) return;
            ProcessSkillPointer(HudPointer, Input.GetMouseButtonDown(0), Input.GetMouseButton(0), Input.GetMouseButtonUp(0));
        }
        internal void ProcessSkillPointer(Vector2 screen, bool pressed, bool held, bool released)
        {
            Metrics(); skillPointerFrame = Time.frameCount;
            Vector2 p = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
            if (!SkillUiAvailable) { if (skillPointerOwned) skillReleasedFrame = Time.frameCount; skillPointerOwned = false; return; }
            if (pressed && !marketPointerOwned && !audioPointerOwned && !AudioCovers(p) && SkillUiCovers(p)) skillPointerOwned = true;
            if (released || (!held && !pressed)) { if (skillPointerOwned) skillReleasedFrame = Time.frameCount; skillPointerOwned = false; }
        }
        void RefreshSkillCounts()
        {
            if (skillCountsFrame == Time.frameCount) return;
            skillCountsFrame = Time.frameCount; armySpeciesCounts.Clear(); ownedSkills.Clear(); availableSkills = 0;
            if (app.Me == null) return;
            availableSkills = TradeCatalog.AvailableSkills(app.Me).Length;
            if (app.State.phase == "battle" || app.State.phase == "results" || app.State.phase == "finished") {
                foreach (FighterState fighter in app.State.fighters) {
                    if (fighter.ownerId != app.Me.id || fighter.dead) continue;
                    int count; armySpeciesCounts.TryGetValue(fighter.kind, out count); armySpeciesCounts[fighter.kind] = count + 1;
                }
            } else foreach (UnitState unit in app.Me.units) { int count; armySpeciesCounts.TryGetValue(unit.kind, out count); armySpeciesCounts[unit.kind] = count + 1; }
            if (app.Me.upgrades != null) foreach (string id in app.Me.upgrades)
                if (Array.IndexOf(TradeCatalog.Ids, id) >= 0 && TradeCatalog.Get(id).type == "skill" && !ownedSkills.Contains(id)) ownedSkills.Add(id);
        }
        List<CreatureDefinition> ApplicableSpecies(TradeDefinition item)
        {
            List<CreatureDefinition> list;
            if (skillSpecies.TryGetValue(item.id, out list)) return list;
            list = new List<CreatureDefinition>();
            foreach (string kind in CreatureCatalog.Kinds) { CreatureDefinition d = CreatureCatalog.Get(kind); if (TradeCatalog.Applies(item, d)) list.Add(d); }
            skillSpecies.Add(item.id, list); return list;
        }
        int MatchingArmyCount(TradeDefinition item)
        {
            RefreshSkillCounts(); int total = 0;
            foreach (CreatureDefinition d in ApplicableSpecies(item)) { int count; if (armySpeciesCounts.TryGetValue(d.id, out count)) total += count; }
            return total;
        }
        int AvailableSkillCount()
        {
            RefreshSkillCounts(); return availableSkills;
        }
        bool SkillDetailButton(Rect r, TradeDefinition item)
        {
            bool enabled = GUI.enabled && !AudioBlocksUnderlying && !EconomyBlocksUnderlying && (drawingSkills || !SkillBlocksUnderlying);
            bool previous = GUI.enabled; GUI.enabled = enabled; RegisterPointerRect(r);
            bool clicked = GUI.Button(r, GUIContent.none, GUIStyle.none); GUI.enabled = previous;
            if (clicked) { OpenSkillDetails(item.id); if (SoundCue != null) SoundCue("click"); }
            return clicked;
        }
        void SkillHover(TradeDefinition item)
        {
            if (HasSkillDetails || !GUI.enabled || AudioBlocksUnderlying) return;
            SetHover(item.name, item.description + "\n\n适用：" + TargetName(item.target) + " · " + ApplicableSpecies(item).Count + " 种\n当前军团匹配 " + MatchingArmyCount(item) + " 只\n点击技能查看全部适用生物", HudPointer);
        }

        void DrawSkillPanels()
        {
            if (!SkillUiAvailable) return;
            RefreshSkillCounts(); drawingSkills = true;
            bool previous = GUI.enabled, active = !marketDragging && !AudioBlocksUnderlying;
            GUI.enabled = active && !(HasSkillDetails && SkillDetailsRect.Contains(Mouse()));
            if (IconButton(OwnedSkillToggleRect, "book", "已购技能 · " + ownedSkills.Count + (ShowOwnedSkills ? "  −" : "  ＋"), new Color(.13f, .23f, .22f))) ShowOwnedSkills = !ShowOwnedSkills;
            if (ShowOwnedSkills) DrawOwnedSkills();
            GUI.enabled = active;
            if (HasSkillDetails) DrawSkillDetails();
            GUI.enabled = previous; drawingSkills = false;
        }
        void DrawOwnedSkills()
        {
            Rect r = OwnedSkillPanelRect; Panel(r, new Color(.055f, .095f, .105f, .98f));
            Text(new Rect(r.x + 17, r.y + 13, 255, 30), "永久技能 · " + ownedSkills.Count, label, gold);
            if (IconButton(new Rect(r.xMax - 45, r.y + 10, 32, 30), "close", "", new Color(.13f, .2f, .21f))) ShowOwnedSkills = false;
            Rect viewport = OwnedSkillViewport;
            if (ownedSkills.Count == 0) { Text(new Rect(r.x + 21, r.y + 90, r.width - 42, 75), "尚未购买永久技能\n商店购买后，现有与未来的适用生物都会获得强化。", small, muted); return; }
            ownedSkillScroll = GUI.BeginScrollView(viewport, ownedSkillScroll, new Rect(0, 0, viewport.width - 20, Mathf.Max(viewport.height, ownedSkills.Count * 88)));
            for (int i = 0; i < ownedSkills.Count; i++) {
                if (i * 88 + 84 < ownedSkillScroll.y || i * 88 > ownedSkillScroll.y + viewport.height) continue;
                TradeDefinition item = TradeCatalog.Get(ownedSkills[i]); int matches = MatchingArmyCount(item);
                Rect row = new Rect(0, i * 88, viewport.width - 22, 82);
                Fill(row, new Color(.09f, .15f, .16f)); Fill(new Rect(row.x, row.y, 3, row.height), matches > 0 ? mint : copper);
                ArenaIcons.Draw(new Rect(9, row.y + 9, 25, 25), ItemIcon(item)); Text(new Rect(42, row.y + 7, 170, 27), item.name, micro, white);
                Text(new Rect(213, row.y + 7, 80, 27), matches + " 只", micro, matches > 0 ? mint : muted);
                Text(new Rect(11, row.y + 36, row.width - 22, 40), ArenaDescription.Format(Compact(item.description, 26)), micro, muted);
                SkillDetailButton(row, item);
            }
            GUI.EndScrollView();
            string hovered = HighlightedSkillId;
            if (hovered != null && ownedSkills.Contains(hovered)) SkillHover(TradeCatalog.Get(hovered));
        }
        void DrawSkillDetails()
        {
            TradeDefinition item = TradeCatalog.Get(detailSkillId); List<CreatureDefinition> species = ApplicableSpecies(item); int matches = MatchingArmyCount(item);
            Rect r = SkillDetailsRect; Panel(r, new Color(.055f, .095f, .11f, .99f));
            Text(new Rect(r.x + 17, r.y + 13, r.width - 72, 31), item.name, label, gold);
            if (IconButton(new Rect(r.xMax - 46, r.y + 10, 32, 30), "close", "", new Color(.13f, .2f, .22f))) { CloseSkillDetails(); return; }
            bool owned = app.Me.upgrades != null && app.Me.upgrades.Contains(item.id);
            Text(new Rect(r.x + 17, r.y + 48, r.width - 34, 28), (owned ? "已掌握 · " : "永久技能 · ") + (matches > 0 ? "当前 " + matches + " 只可获益" : "当前无匹配，可为后续招募购买"), micro, matches > 0 ? mint : muted);
            if (matches > 0) Fill(new Rect(r.x + 17, r.y + 80, r.width - 34, 2), mint);
            Rect viewport = new Rect(r.x + 17, r.y + 94, r.width - 34, r.height - 111);
            string description = ArenaDescription.Format(item.description);
            float textWidth = viewport.width - 22, descriptionHeight = small.CalcHeight(new GUIContent(description), textWidth);
            float listY = descriptionHeight + 81, contentHeight = listY + species.Count * 33;
            skillDetailScroll = GUI.BeginScrollView(viewport, skillDetailScroll, new Rect(0, 0, textWidth, Mathf.Max(viewport.height, contentHeight)));
            Text(new Rect(0, 0, textWidth, descriptionHeight + 3), description, small, white);
            Text(new Rect(0, descriptionHeight + 17, textWidth, 26), "全部适用生物 · " + species.Count + " 种", label, gold);
            Text(new Rect(0, descriptionHeight + 48, textWidth, 26), "当前拥有的种类以青色标出", micro, muted);
            for (int i = 0; i < species.Count; i++) {
                float yy = listY + i * 33; if (yy + 33 < skillDetailScroll.y || yy > skillDetailScroll.y + viewport.height) continue;
                CreatureDefinition d = species[i]; int count; armySpeciesCounts.TryGetValue(d.id, out count);
                if (count > 0) Fill(new Rect(0, yy, textWidth, 30), new Color(.1f, .27f, .23f));
                ArenaIcons.Creature(new Rect(5, yy + 3, 25, 25), d.model ?? d.id, MobColor(d));
                Text(new Rect(40, yy + 3, textWidth - 110, 27), d.name, micro, count > 0 ? mint : white);
                if (count > 0) Text(new Rect(textWidth - 61, yy + 3, 61, 27), "× " + count, micro, mint);
            }
            GUI.EndScrollView();
        }
    }
}
