using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlockArena.Core
{
    /// <summary>Named creature skills share executable, server-authoritative effects.</summary>
    public static class CreatureAbilities
    {
        private static readonly Dictionary<string, AbilityDefinition> definitions = new Dictionary<string, AbilityDefinition>(StringComparer.Ordinal);
        private static readonly string[] empty = new string[0];
        [ThreadStatic] private static bool reflecting;
        [ThreadStatic] internal static bool PeriodicDamage;

        static CreatureAbilities()
        {
            Register("lifesteal", "军势汲取", "lifesteal", .12f);
            Register("dodge", "高空闪避", "dodge", .2f);
            Register("haste", "迅捷鼓舞", "haste", .15f);
            Register("regen", "潮汐再生", "regen", 1.5f);
            Register("poison", "节肢毒素", "poison", 2, 0, 3);
            Register("shield", "铸造屏障", "shield", 30);
            Register("pierce", "白骨穿甲", "pierce", .2f);
            Register("thorns", "不朽荆棘", "thorns", .18f);
        }

        public static string[] Ids { get { EnsureCatalog(); return definitions.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(); } }
        private static void EnsureCatalog() { if (CreatureCatalog.Kinds == null) throw new InvalidOperationException("生物目录尚未初始化"); }
        public static AbilityDefinition Get(string id) { EnsureCatalog(); return GetRegistered(id); }
        internal static AbilityDefinition GetRegistered(string id)
        {
            AbilityDefinition ability;
            if (id == null || !definitions.TryGetValue(id, out ability)) throw new InvalidOperationException("未知技能类型：" + id);
            return ability;
        }
        internal static string Register(string id, string name, string effect, float value, float radius = 0, float duration = 0, float cooldown = 0)
        {
            var ability = new AbilityDefinition { id = id, name = name, effect = effect, value = value, radius = radius, duration = duration, cooldown = cooldown };
            ability.icon = effect == "heal" || effect == "regen" || effect == "lifesteal" ? "heart" :
                effect == "armor" || effect == "shield" || effect == "rally" ? "armor" :
                effect == "haste" || effect == "dodge" || effect == "blink" || effect == "charge" ? "speed" :
                effect == "pierce" || effect == "cleave" || effect == "frenzy" || effect == "execute" ? "attack" : "book";
            ability.description = Describe(ability);
            definitions.Add(id, ability);
            return id;
        }
        private static string N(float value) { return value.ToString("0.##", CultureInfo.InvariantCulture); }
        private static string P(float value) { return N(value * 100) + "%"; }
        private static string Describe(AbilityDefinition a)
        {
            switch (a.effect)
            {
                case "armor": return "获得 " + P(a.value) + " 减伤；总减伤上限 80%。";
                case "shield": return "每轮开战获得 " + N(a.value) + " 点护盾。";
                case "regen": return "每秒恢复 " + N(a.value) + " 点生命。";
                case "haste": return "攻速与移动速度提高 " + P(a.value) + "。";
                case "dodge": return "按受击次数累计闪避，平均避开 " + P(a.value) + " 的来袭伤害；总闪避上限 50%。";
                case "lifesteal": return "造成生命伤害后恢复其 " + P(a.value) + " 的生命。";
                case "pierce": return "伤害忽略目标 " + N(a.value * 100) + " 个百分点的护甲减伤。";
                case "thorns": return "受伤后向伤害来源反射 " + P(a.value) + " 的实际生命伤害；反射不再触发反射。";
                case "poison": return "命中施加 " + N(a.duration) + " 秒中毒，每秒 " + N(a.value) + " 点伤害；同类毒取较强效果。";
                case "burn": return "命中施加 " + N(a.duration) + " 秒灼烧，每秒 " + N(a.value) + " 点伤害。";
                case "slow": return "命中使目标移速降低 " + P(a.value) + "，持续 " + N(a.duration) + " 秒。";
                case "stun": return "攻击眩晕目标 " + N(a.duration) + " 秒，独立冷却 " + N(a.cooldown) + " 秒。";
                case "knockback": return "命中把目标击退 " + N(a.value) + " 距离。";
                case "charge": return "本轮首次攻击额外造成 " + P(a.value) + " 攻击伤害。";
                case "frenzy": return "自身生命低于 50% 时，普通攻击伤害提高 " + P(a.value) + "。";
                case "execute": return "攻击生命低于 30% 的目标时，伤害提高 " + P(a.value) + "。";
                case "cleave": return "攻击额外波及目标周围 " + N(a.radius) + " 范围敌人，造成 " + P(a.value) + " 攻击伤害。";
                case "explode": return "攻击时自爆，对自身周围 " + N(a.radius) + " 范围敌人造成 " + P(a.value) + " 攻击伤害。";
                case "split": return "死亡分裂为 " + N(a.value) + " 只小型同类；分裂体不能再次分裂，且不提供击杀金币。";
                case "heal": return "每 " + N(a.cooldown) + " 秒为 " + N(a.radius) + " 范围生命比例最低的友军恢复 " + N(a.value) + " 点生命，可治疗自身。";
                case "rally": return "每 " + N(a.cooldown) + " 秒给 " + N(a.radius) + " 范围友军增加 " + N(a.value) + " 护盾，最多叠至各自生命上限的 40%。";
                case "blink": return "向 " + N(a.radius) + " 范围内最近敌人瞬移接近，冷却 " + N(a.cooldown) + " 秒。";
                case "summon_vex": return "攻击时召唤 " + N(a.value) + " 只恼鬼，独立冷却 " + N(a.cooldown) + " 秒；召唤物不提供击杀金币。";
                default: throw new InvalidOperationException("尚未实现的技能效果：" + a.effect);
            }
        }
        private static string[] SkillIds(FighterState f)
        {
            if (f == null) return empty;
            if (f.skills != null && f.skills.Length > 0) return f.skills;
            return string.IsNullOrEmpty(f.kind) ? empty : CreatureCatalog.Get(f.kind).skills;
        }
        public static bool Has(FighterState f, string id)
        {
            foreach (string skill in SkillIds(f)) if (skill == id || GetRegistered(skill).effect == id) return true;
            return false;
        }
        private static float Sum(FighterState f, string effect)
        {
            float value = 0;
            foreach (string id in SkillIds(f)) { AbilityDefinition a = GetRegistered(id); if (a.effect == effect) value += a.value; }
            return value;
        }
        private static float Distance(FighterState a, FighterState b)
        { float x = a.x - b.x, y = a.y - b.y; return (float)Math.Sqrt(x * x + y * y); }
        private static float Clamp(float value, float min, float max) { return Math.Max(min, Math.Min(max, value)); }
        private static bool Ready(FighterState f, AbilityDefinition a)
        { float remaining; return !f.abilityCooldowns.TryGetValue(a.id, out remaining) || remaining <= 0; }
        private static void Cooldown(FighterState f, AbilityDefinition a) { if (a.cooldown > 0) f.abilityCooldowns[a.id] = a.cooldown; }

        public static void OnSpawn(FighterState f)
        {
            if (f.skills == null || f.skills.Length == 0) f.skills = (string[])SkillIds(f).Clone();
            f.abilityCooldowns.Clear(); f.attacks = f.hits = 0; f.deathTriggered = false; f.slowMultiplier = 1;
            f.armor = Clamp(f.armor + Sum(f, "armor"), 0, .8f);
            f.shield += Sum(f, "shield");
            float haste = Sum(f, "haste"); f.attackSpeed *= 1 + haste; f.speed *= 1 + haste;
        }

        public static void Tick(FighterState f, List<FighterState> fighters, float dt,
            Action<FighterState, float, FighterState> damage, Action<string, FighterState, FighterState, float> effect,
            float battlefieldEdge = 410)
        {
            if (f.dead || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            f.stunTime = Math.Max(0, f.stunTime - dt); f.slowTime = Math.Max(0, f.slowTime - dt);
            if (f.slowTime <= 0) f.slowMultiplier = 1;
            foreach (string id in SkillIds(f))
            {
                float remaining;
                if (f.abilityCooldowns.TryGetValue(id, out remaining)) f.abilityCooldowns[id] = Math.Max(0, remaining - dt);
            }
            TickPoison(f, dt, damage);
            if (f.dead) return;
            TickBurn(f, dt, damage);
            if (f.dead) return;
            f.hp = Math.Min(f.maxHp, f.hp + Sum(f, "regen") * dt * f.healingMultiplier);
            if (f.stunTime > 0) return;
            foreach (string id in SkillIds(f))
            {
                AbilityDefinition a = GetRegistered(id);
                if (!Ready(f, a)) continue;
                if (a.effect == "heal")
                {
                    FighterState patient = null; float lowest = 1;
                    foreach (FighterState ally in fighters)
                    {
                        if (ally.dead || ally.ownerId != f.ownerId || ally.maxHp <= 0 || Distance(f, ally) > a.radius) continue;
                        float ratio = ally.hp / ally.maxHp;
                        if (ratio < lowest) { lowest = ratio; patient = ally; }
                    }
                    if (patient != null) { Heal(f, patient, a.value, effect); Cooldown(f, a); }
                }
                else if (a.effect == "rally")
                {
                    bool granted = false;
                    foreach (FighterState ally in fighters)
                    {
                        if (ally.dead || ally.ownerId != f.ownerId || Distance(f, ally) > a.radius) continue;
                        float added = Math.Max(0, Math.Min(a.value, ally.maxHp * .4f - ally.shield));
                        if (added <= 0) continue;
                        ally.shield += added; effect?.Invoke("shield", f, ally, added); granted = true;
                    }
                    if (granted) Cooldown(f, a);
                }
                else if (a.effect == "blink")
                {
                    FighterState enemy = null; float nearest = a.radius;
                    foreach (FighterState other in fighters)
                    {
                        if (other.dead || other.ownerId == f.ownerId) continue;
                        float distance = Distance(f, other);
                        if (distance < nearest) { nearest = distance; enemy = other; }
                    }
                    if (enemy != null && nearest > f.range + 20)
                    {
                        float dx = (f.x - enemy.x) / Math.Max(1, nearest), dy = (f.y - enemy.y) / Math.Max(1, nearest);
                        effect?.Invoke("teleport", f, enemy, nearest);
                        f.x = Clamp(enemy.x + dx * Math.Max(20, f.range * .8f), -battlefieldEdge, battlefieldEdge);
                        f.y = Clamp(enemy.y + dy * Math.Max(20, f.range * .8f), -battlefieldEdge, battlefieldEdge);
                        f.targetId = enemy.id; f.think = .5f; Cooldown(f, a);
                    }
                }
            }
        }

        private static void TickPoison(FighterState f, float dt, Action<FighterState, float, FighterState> damage)
        {
            if (f.poisonTime <= 0) return;
            float elapsed = Math.Min(dt, f.poisonTime); f.poisonTime = Math.Max(0, f.poisonTime - dt); f.poisonTick += elapsed;
            while (!f.dead && (f.poisonTick >= .5f || f.poisonTime <= 0 && f.poisonTick > .00001f))
            {
                float step = Math.Min(.5f, f.poisonTick); f.poisonTick -= step;
                bool previous=PeriodicDamage;PeriodicDamage=true;
                try { damage(f, f.poisonDps * step, f.poisonSource); }
                finally { PeriodicDamage=previous; }
            }
            if (f.poisonTime <= 0) { f.poisonDps = 0; f.poisonSource = null; f.poisonTick = 0; }
        }
        private static void TickBurn(FighterState f, float dt, Action<FighterState, float, FighterState> damage)
        {
            if (f.burnTime <= 0) return;
            float elapsed = Math.Min(dt, f.burnTime); f.burnTime = Math.Max(0, f.burnTime - dt); f.burnTick += elapsed;
            while (!f.dead && (f.burnTick >= .5f || f.burnTime <= 0 && f.burnTick > .00001f))
            {
                float step = Math.Min(.5f, f.burnTick); f.burnTick -= step;
                bool previous=PeriodicDamage;PeriodicDamage=true;
                try { damage(f, f.burnDps * step * f.environmentDamage, f.burnSource); }
                finally { PeriodicDamage=previous; }
            }
            if (f.burnTime <= 0) { f.burnDps = 0; f.burnSource = null; f.burnTick = 0; }
        }
        private static void Heal(FighterState source, FighterState patient, float amount, Action<string, FighterState, FighterState, float> effect)
        {
            if (patient == null || patient.dead || amount <= 0) return;
            float actual = Math.Max(0, Math.Min(amount * patient.healingMultiplier, patient.maxHp - patient.hp));
            if (actual <= 0) return;
            patient.hp += actual; effect?.Invoke("heal", source, patient, actual);
        }

        public static void Attack(FighterState f, FighterState target, List<FighterState> fighters,
            Action<FighterState, float, FighterState> damage, Action<string, FighterState, FighterState, float> effect,
            Action<FighterState, string, int> summon, float battlefieldEdge = 410)
        {
            if (f.dead || target == null || target.dead || f.stunTime > 0) return;
            f.attacks++;
            foreach (string id in SkillIds(f))
            {
                AbilityDefinition a = GetRegistered(id);
                if (a.effect != "explode") continue;
                effect?.Invoke("explode", f, target, a.radius);
                foreach (FighterState enemy in fighters.ToArray())
                    if (!enemy.dead && enemy.ownerId != f.ownerId && Distance(f, enemy) <= a.radius) damage(enemy, f.attack * a.value, f);
                damage(f, 999999, f);
                return;
            }
            float bonus = (f.attacks == 1 ? Sum(f, "charge") : 0) + (f.hp < f.maxHp * .5f ? Sum(f, "frenzy") : 0)
                + (target.hp < target.maxHp * .3f ? Sum(f, "execute") : 0);
            if (f.range > 70) effect?.Invoke("arrow", f, target, 0);
            else effect?.Invoke("strike", f, target, 0);
            float before = target.hp + target.shield;
            damage(target, f.attack * (1 + bonus), f);
            bool hit = target.hp + target.shield < before;
            if (f.dead) return;
            foreach (string id in SkillIds(f))
            {
                AbilityDefinition a = GetRegistered(id);
                if (!Ready(f, a)) continue;
                if (a.effect == "summon_vex" && !f.summoned)
                { summon?.Invoke(f, "vex", (int)a.value); Cooldown(f, a); continue; }
                if (a.effect == "cleave")
                {
                    foreach (FighterState enemy in fighters.ToArray())
                        if (!enemy.dead && enemy != target && enemy.ownerId != f.ownerId && Distance(target, enemy) <= a.radius)
                            damage(enemy, f.attack * a.value, f);
                    effect?.Invoke("sweep", f, target, a.radius); Cooldown(f, a);
                }
                if (!hit || target.dead) continue;
                switch (a.effect)
                {
                    case "poison":
                        if(target.resistanceTime>0)break;
                        if (target.poisonTime <= 0 || a.value >= target.poisonDps)
                        { target.poisonDps = a.value; target.poisonSource = f; target.poisonTime = Math.Max(target.poisonTime, a.duration); }
                        effect?.Invoke("poison", f, target, a.value); Cooldown(f, a); break;
                    case "burn":
                        if(target.resistanceTime>0)break;
                        if (target.burnTime <= 0 || a.value >= target.burnDps)
                        { target.burnDps = a.value; target.burnSource = f; target.burnTime = Math.Max(target.burnTime, a.duration); }
                        effect?.Invoke("burn", f, target, a.value); Cooldown(f, a); break;
                    case "slow":
                        if(target.resistanceTime>0)break;
                        target.slowTime = Math.Max(target.slowTime, a.duration * target.statusDurationMultiplier);
                        target.slowMultiplier = Math.Min(target.slowMultiplier, Clamp(1 - a.value, .1f, 1));
                        effect?.Invoke("slow", f, target, a.duration); Cooldown(f, a); break;
                    case "stun":
                        if(target.resistanceTime>0)break;
                        target.stunTime = Math.Max(target.stunTime, a.duration * target.statusDurationMultiplier);
                        effect?.Invoke("stun", f, target, a.duration); Cooldown(f, a); break;
                    case "knockback":
                        float distance = Distance(f, target), dx = target.x - f.x, dy = target.y - f.y;
                        if (distance < .01f) { dx = f.side == 0 ? 1 : f.side; dy = 0; distance = 1; }
                        target.x = Clamp(target.x + dx / distance * a.value, -battlefieldEdge, battlefieldEdge);
                        target.y = Clamp(target.y + dy / distance * a.value, -battlefieldEdge, battlefieldEdge);
                        effect?.Invoke("knockback", f, target, a.value); Cooldown(f, a); break;
                }
            }
        }

        public static float BeforeDamage(FighterState target, float amount, FighterState source)
        {
            if (target.dead || amount <= 0) return 0;
            // Self-destruction cannot be evaded or blocked by the attacker's own shields.
            if (ReferenceEquals(target, source)) return amount;
            target.hits++;
            float dodge = Clamp(Sum(target, "dodge"), 0, .5f);
            if (dodge > 0 && Math.Floor(target.hits * (double)dodge) > Math.Floor((target.hits - 1) * (double)dodge)) return 0;
            float armor = Clamp(target.armor - (source == null ? 0 : Sum(source, "pierce")), 0, .8f);
            float remaining = amount * (1 - armor);
            float absorbed = Math.Min(target.shield, remaining); target.shield -= absorbed;
            return Math.Max(0, remaining - absorbed);
        }
        public static void AfterDamage(FighterState target, float amount, FighterState source,
            Action<FighterState, float, FighterState> damage, Action<string, FighterState, FighterState, float> effect)
        {
            if (source == null || source.ownerId == target.ownerId || amount <= 0) return;
            if (!source.dead) Heal(source, source, amount * Clamp(Sum(source, "lifesteal"), 0, .8f), effect);
            float reflected = amount * Clamp(Sum(target, "thorns"), 0, .8f);
            if (reflecting || source.dead || reflected <= 0) return;
            reflecting = true;
            try { effect?.Invoke("thorns", target, source, reflected); damage(source, reflected, target); }
            finally { reflecting = false; }
        }
        public static void OnDeath(FighterState f, List<FighterState> fighters,
            Action<FighterState, float, FighterState> damage, Action<string, FighterState, FighterState, float> effect,
            Action<FighterState, string, int> summon)
        {
            if (f.deathTriggered) return;
            f.deathTriggered = true;
            if (f.mini || f.summoned) return;
            foreach (string id in SkillIds(f))
            {
                AbilityDefinition a = GetRegistered(id);
                if (a.effect == "split") summon?.Invoke(f, f.kind, (int)a.value);
            }
        }
    }
}
