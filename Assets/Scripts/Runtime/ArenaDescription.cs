using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlockArena.Core;

namespace BlockArena.Runtime
{
    // Accept catalog/plain text only. Every span is emitted together with its closing tags;
    // compact/truncate the plain text before calling Format, never the rich text afterward.
    public static class ArenaDescription
    {
        const string Gold = "#F3CF82", Damage = "#FFB47B", Health = "#91E2B9";
        const string Defense = "#8DDAEE", Control = "#CAB0F5", Speed = "#F1D489";
        struct Term
        {
            internal readonly string text, color;
            internal Term(string text, string color) { this.text = text; this.color = color; }
        }
        static readonly Term[] terms = MakeTerms();
        static readonly Dictionary<string, string> formatted = new Dictionary<string, string>(StringComparer.Ordinal);

        static Term[] MakeTerms()
        {
            var result = new[] {
                new Term("实际生命伤害", Damage), new Term("普通攻击伤害", Damage), new Term("攻击伤害", Damage),
                new Term("伤害", Damage), new Term("攻击", Damage), new Term("荆棘反伤", Damage),
                new Term("生命上限", Health), new Term("生命", Health), new Term("治疗", Health),
                new Term("恢复", Health), new Term("吸血", Health), new Term("再生", Health),
                new Term("护甲减伤", Defense), new Term("减伤", Defense), new Term("护甲", Defense),
                new Term("护盾", Defense), new Term("闪避", Defense),
                new Term("移速降低", Control), new Term("眩晕", Control), new Term("击退", Control),
                new Term("中毒", Control), new Term("灼烧", Control), new Term("减速", Control),
                new Term("攻击速度", Speed), new Term("移动速度", Speed), new Term("攻速", Speed),
                new Term("移速", Speed), new Term("射程", Speed), new Term("冷却", Speed),
                new Term("死亡分裂", Control), new Term("分裂", Control), new Term("召唤", Control),
                new Term("瞬移", Control), new Term("自爆", Damage), new Term("反射", Damage)
            };
            Array.Sort(result, (a, b) => b.text.Length.CompareTo(a.text.Length));
            return result;
        }

        public static string Format(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            string cached;
            if (formatted.TryGetValue(plain, out cached)) return cached;
            var output = new StringBuilder(plain.Length + 128);
            string numberColor = Gold;
            for (int i = 0; i < plain.Length; ) {
                char c = plain[i];
                // Unity IMGUI does not decode HTML entities. These visible angle characters
                // retain the text while preventing user/network text from creating rich tags.
                if (c == '<' || c == '>') { output.Append(c == '<' ? '\u2039' : '\u203a'); i++; continue; }
                if (IsNumberStart(plain, i)) {
                    int end = NumberEnd(plain, i);
                    Emphasize(output, plain, i, end - i, numberColor);
                    i = end; continue;
                }
                bool matched = false;
                foreach (Term term in terms) {
                    if (i + term.text.Length > plain.Length || string.CompareOrdinal(plain, i, term.text, 0, term.text.Length) != 0) continue;
                    Emphasize(output, plain, i, term.text.Length, term.color);
                    numberColor = term.color; i += term.text.Length; matched = true; break;
                }
                if (matched) continue;
                output.Append(c); i++;
                if (c == '\n' || c == '。' || c == '；' || c == ';' || c == '，' || c == ',' || c == '、' || c == '·') numberColor = Gold;
            }
            cached = output.ToString();
            if (formatted.Count >= 256) formatted.Clear();
            formatted[plain] = cached;
            return cached;
        }
        static bool Digit(char c) { return c >= '0' && c <= '9'; }
        static bool IsNumberStart(string value, int i)
        {
            return Digit(value[i]) || ((value[i] == '+' || value[i] == '-' || value[i] == '\u2212') && i + 1 < value.Length && Digit(value[i + 1]));
        }
        static int NumberEnd(string value, int i)
        {
            int end = i;
            if (!Digit(value[end])) end++;
            while (end < value.Length && Digit(value[end])) end++;
            if (end + 1 < value.Length && value[end] == '.' && Digit(value[end + 1])) {
                end++; while (end < value.Length && Digit(value[end])) end++;
            }
            if (end < value.Length && (value[end] == '%' || value[end] == '％')) end++;
            return end;
        }
        static void Emphasize(StringBuilder output, string value, int start, int count, string color)
        {
            output.Append("<b><color=").Append(color).Append('>');
            output.Append(value, start, count);
            output.Append("</color></b>");
        }

        static string N(float value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }
        static string P(float value) { return N(value * 100) + "%"; }

        // A plain, factual summary. Call Format on the result when drawing it.
        public static string Brief(AbilityDefinition a)
        {
            if (a == null) return string.Empty;
            string summary;
            switch (a.effect) {
                case "armor": return "减伤 " + P(a.value) + " · 总上限 80%";
                case "shield": return "开战获得 " + N(a.value) + " 护盾";
                case "regen": return "每秒恢复 " + N(a.value) + " 生命";
                case "haste": return "攻速、移速 +" + P(a.value);
                case "dodge": return "累计闪避 " + P(a.value) + " 的受击次数 · 总上限 50%";
                case "lifesteal": return "生命伤害的 " + P(a.value) + " 转为自身治疗";
                case "pierce": return "忽略 " + N(a.value * 100) + " 个百分点的护甲减伤";
                case "thorns": return "受伤反射 " + P(a.value) + " 实际生命伤害 · 不连锁";
                case "poison": summary = "命中中毒 " + N(a.duration) + " 秒，每秒 " + N(a.value) + " 伤害 · 同类取强"; break;
                case "burn": summary = "命中灼烧 " + N(a.duration) + " 秒，每秒 " + N(a.value) + " 伤害"; break;
                case "slow": summary = "命中移速降低 " + P(a.value) + "，持续 " + N(a.duration) + " 秒"; break;
                case "stun": summary = "命中眩晕 " + N(a.duration) + " 秒"; break;
                case "knockback": summary = "命中击退 " + N(a.value) + " 距离"; break;
                case "charge": return "本轮首次攻击额外造成 " + P(a.value) + " 攻击伤害";
                case "frenzy": return "自身生命低于 50% 时，普攻伤害 +" + P(a.value);
                case "execute": return "目标生命低于 30% 时，普攻伤害 +" + P(a.value);
                case "cleave": summary = "攻击波及目标周围 " + N(a.radius) + " 范围，造成 " + P(a.value) + " 攻击伤害"; break;
                case "explode": return "攻击时自爆，周围 " + N(a.radius) + " 范围造成 " + P(a.value) + " 攻击伤害";
                case "split": return "死亡分裂为 " + N(a.value) + " 只小型同类 · 不再分裂，无击杀金币";
                case "heal": return "每 " + N(a.cooldown) + " 秒，治疗 " + N(a.radius) + " 范围生命比例最低友军 " + N(a.value) + " 生命 · 含自身";
                case "rally": return "每 " + N(a.cooldown) + " 秒，" + N(a.radius) + " 范围友军 +" + N(a.value) + " 护盾 · 上限各自生命上限的 40%";
                case "blink": return "瞬移接近 " + N(a.radius) + " 范围内最近敌人 · 冷却 " + N(a.cooldown) + " 秒";
                case "summon_vex": summary = "攻击召唤 " + N(a.value) + " 只恼鬼 · 无击杀金币"; break;
                default: return a.description ?? string.Empty;
            }
            return summary + (a.cooldown > 0 ? " · 冷却 " + N(a.cooldown) + " 秒" : string.Empty);
        }
    }
}
