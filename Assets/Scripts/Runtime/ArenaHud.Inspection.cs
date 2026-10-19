using System;
using System.Collections.Generic;
using System.Text;
using BlockArena.Core;
using UnityEngine;

namespace BlockArena.Runtime
{
    public sealed partial class ArenaHud
    {
        const float InspectionDelay = 1;
        sealed class InspectionSkill
        {
            internal string id, name, icon, plain, rich;
            internal float height;
        }
        readonly List<InspectionSkill> inspectionSkills = new List<InspectionSkill>();
        readonly List<KeyValuePair<string,string>> inspectionStates = new List<KeyValuePair<string,string>>();
        readonly Spring inspectionReveal = new Spring();
        string inspectionSubject, inspectionTeam, inspectionKind, inspectionSkillPlain = "";
        bool inspectionIsFighter;
        int inspectionRound;
        float inspectionStarted;
        Rect inspectionTooltip;
        internal bool InspectionExpanded { get { return inspectionDefinition != null && inspectionSubject != null && Time.unscaledTime <= hoverUntil && Time.unscaledTime - inspectionStarted >= InspectionDelay; } }
        internal int InspectionSkillCount { get { return InspectionExpanded ? inspectionSkills.Count : 0; } }
        internal string InspectionSkillText { get { return InspectionExpanded ? inspectionSkillPlain : ""; } }
        internal Rect InspectionTooltipScreenRect { get { return inspectionDefinition != null && Time.unscaledTime <= hoverUntil ? inspectionTooltip : Rect.zero; } }

        void ResetInspection()
        {
            inspectionSubject = inspectionTeam = inspectionKind = null;
            inspectionStarted = 0; inspectionTooltip = Rect.zero;
            inspectionReveal.value = inspectionReveal.velocity = 0;
            inspectionSkills.Clear(); inspectionSkillPlain = "";
        }

        void BeginInspection(CreatureDefinition definition, FighterState fighter, UnitState unit, PlayerState owner)
        {
            string subject = fighter != null ? fighter.id : unit != null ? unit.id : definition.id;
            string team = owner != null ? owner.id : fighter != null ? fighter.ownerId : null;
            int round = app.State == null ? 0 : app.State.round;
            // Snapshots replace objects, so compare stable individual identity, not reference or species name.
            if (inspectionSubject != subject || inspectionTeam != team || inspectionKind != definition.id ||
                inspectionRound != round || inspectionIsFighter != (fighter != null) || Time.unscaledTime > hoverUntil) {
                ResetInspection();
                inspectionSubject = subject; inspectionTeam = team; inspectionKind = definition.id;
                inspectionRound = round; inspectionIsFighter = fighter != null;
                inspectionStarted = Time.unscaledTime;
            }
        }

        void CacheInspectionSkills()
        {
            string[] ids = inspectionFighter != null && inspectionFighter.neutral ? inspectionFighter.skills : inspectionFighter != null && inspectionFighter.skills != null && inspectionFighter.skills.Length > 0 ? inspectionFighter.skills : inspectionDefinition.skills;
            int count = ids == null ? 0 : ids.Length;
            bool changed = count != inspectionSkills.Count;
            if (!changed) for (int i = 0; i < count; i++) if (inspectionSkills[i].id != ids[i]) { changed = true; break; }
            if (!changed) return;
            inspectionSkills.Clear();
            var plain = new StringBuilder();
            for (int i = 0; i < count; i++) {
                AbilityDefinition a = CreatureAbilities.Get(ids[i]);
                string brief = ArenaDescription.Brief(a);
                string icon = AbilityIcon(a);
                inspectionSkills.Add(new InspectionSkill { id = ids[i], name = a.name, icon = icon, plain = brief, rich = ArenaDescription.Format(brief) });
                if (plain.Length > 0) plain.Append('\n');
                plain.Append(a.name).Append(" · ").Append(brief);
            }
            inspectionSkillPlain = plain.ToString();
        }

        static string AbilityIcon(AbilityDefinition ability)
        {
            return ability.effect == "poison" ? "poison" : ability.effect == "burn" ? "fire" : ability.effect == "slow" ? "frost" : ability.effect == "stun" ? "stun" : ability.icon == "armor" ? "shield" : ability.icon == "attack" ? "sword" : ability.icon ?? "book";
        }

        void Inspection()
        {
            var d = inspectionDefinition; var f = inspectionFighter;
            const float panelWidth = 430, innerWidth = 390, skillTextWidth = 352;
            float hp = f == null ? d.hp : f.hp, maxHp = f == null ? d.hp : f.maxHp;
            inspectionStates.Clear();
            if (f != null) {
                if (f.shield > 0) inspectionStates.Add(new KeyValuePair<string,string>("shield", "护盾 " + Mathf.CeilToInt(f.shield)));
                if (f.poisonTime > 0) inspectionStates.Add(new KeyValuePair<string,string>("poison", "中毒"));
                if (f.burnTime > 0) inspectionStates.Add(new KeyValuePair<string,string>("fire", "灼烧"));
                if (f.slowTime > 0) inspectionStates.Add(new KeyValuePair<string,string>("frost", "减速"));
                if (f.stunTime > 0) inspectionStates.Add(new KeyValuePair<string,string>("stun", "眩晕"));
            }
            float stateWidth = 0, stateHeight = inspectionStates.Count == 0 ? 0 : 30;
            foreach (var status in inspectionStates) {
                float w = small.CalcSize(new GUIContent(ArenaDescription.Format(status.Value))).x + 34;
                if (stateWidth + w > innerWidth) { stateHeight += 30; stateWidth = 0; }
                stateWidth += w + 6;
            }
            float detailY = 202 + stateHeight, skillHeight = 0;
            foreach (var skill in inspectionSkills) {
                skill.height = small.CalcHeight(new GUIContent(skill.rich), skillTextWidth);
                skillHeight += 34 + skill.height + 10;
            }
            bool expanded = InspectionExpanded;
            inspectionReveal.Step(expanded ? 1 : 0, 22, .76f);
            float reveal = Mathf.Clamp01(inspectionReveal.value);
            float baseHeight = detailY + 43;
            // Current catalog + all applicable purchased skills fit. Keep the panel within short windows too.
            float extra = Mathf.Max(0, skillHeight + 16 - 43);
            float panelHeight = Mathf.Min(height - 36, baseHeight + extra * reveal);
            Rect r = TooltipRect(panelWidth, panelHeight);
            inspectionTooltip = ToScreenRect(r);
            Panel(r, new Color(.07f,.11f,.12f,.99f));
            ArenaIcons.Creature(new Rect(r.x+17,r.y+14,58,58),d.model??d.id,MobColor(d));
            bool neutral=f!=null&&f.neutral;
            bool resource=neutral&&app.State.roundEvent!=null&&app.State.roundEvent.kind=="gold";
            Text(new Rect(r.x+89,r.y+13,321,36),resource?"中央金矿":(neutral?"中立 · ":f!=null&&f.scale<.9f?"小":"")+d.name,heading,neutral?gold:white);
            Text(new Rect(r.x+89,r.y+52,321,26),neutral?(app.State.roundEvent==null?"中立生物":"击破奖励 "+app.State.roundEvent.reward+" 金币"):(inspectionOwner==null?"":Trim(inspectionOwner.name,8)+" · ")+d.role,small,neutral?gold:inspectionOwner==null?muted:Team(inspectionOwner.slot));
            Fill(new Rect(r.x+20,r.y+92,innerWidth,18),new Color(.025f,.045f,.045f));
            Fill(new Rect(r.x+22,r.y+94,(innerWidth-4)*Mathf.Clamp01(hp/Mathf.Max(1,maxHp)),14),new Color(.29f,.75f,.45f));
            Text(new Rect(r.x+20,r.y+88,innerWidth,25),"<b>"+Mathf.CeilToInt(hp)+" / "+Mathf.CeilToInt(maxHp)+"</b>",centered,white);
            Stat(new Rect(r.x+20,r.y+127,183,29),"sword",ArenaDescription.Format((f==null?d.attack:f.attack).ToString("0.#")));
            Stat(new Rect(r.x+225,r.y+127,183,29),"speed",ArenaDescription.Format((f==null?d.attackSpeed:f.attackSpeed).ToString("0.##")+" 次/秒"));
            Stat(new Rect(r.x+20,r.y+164,183,29),"range",ArenaDescription.Format((f==null?d.range:f.range).ToString("0.#")));
            Stat(new Rect(r.x+225,r.y+164,183,29),"shield",ArenaDescription.Format(((f==null?0:f.armor)*100).ToString("0.#")+"% 护甲"));
            float sx = 0, sy = 0;
            foreach (var status in inspectionStates) {
                string text = ArenaDescription.Format(status.Value);
                float w = small.CalcSize(new GUIContent(text)).x + 34;
                if (sx + w > innerWidth) { sy += 30; sx = 0; }
                ArenaIcons.Draw(new Rect(r.x+20+sx,r.y+202+sy,22,22),status.Key);
                Text(new Rect(r.x+47+sx,r.y+201+sy,w-26,27),text,small,gold); sx += w+6;
            }
            Fill(new Rect(r.x+20,r.y+detailY+3,innerWidth,1),new Color(.34f,.42f,.38f,.55f));
            if(resource) {
                Text(new Rect(r.x+20,r.y+detailY+12,innerWidth,25),"额外奖励 · 不计普通击杀收入",micro,gold);
                return;
            }
            if (!expanded) {
                Text(new Rect(r.x+20,r.y+detailY+12,innerWidth-72,25),"停留查看技能",micro,muted);
                float progress = Mathf.Clamp01((Time.unscaledTime-inspectionStarted)/InspectionDelay);
                Rect track = new Rect(r.xMax-82,r.y+detailY+22,60,3);
                Fill(track,new Color(.15f,.24f,.22f)); Fill(new Rect(track.x,track.y,track.width*progress,3),mint);
                return;
            }
            GUI.BeginClip(new Rect(r.x+20,r.y+detailY+10,innerWidth,Mathf.Max(0,r.height-detailY-20)));
            float y = 0;
            foreach (var skill in inspectionSkills) {
                ArenaIcons.Draw(new Rect(0,y+2,26,26),skill.icon);
                Text(new Rect(38,y,skillTextWidth,28),"<b>"+skill.name+"</b>",label,mint);
                Text(new Rect(38,y+31,skillTextWidth,skill.height+3),skill.rich,small,white);
                y += 34 + skill.height + 10;
            }
            if(inspectionSkills.Count==0) Text(new Rect(0,0,innerWidth,28),"暂无技能",small,muted);
            GUI.EndClip();
        }
    }
}
