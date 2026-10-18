using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockArena.Core
{
    // Independent mob entities in Java 1.21.11; colours, ages and jockey combinations share entries.
    public static class CreatureCatalog
    {
        public const string ContentVersion = "Java 1.21.11";
        private static readonly Dictionary<string, CreatureDefinition> Definitions = Build();
        public static readonly string[] Kinds = Definitions.Keys.OrderBy(id => Definitions[id].tier).ThenBy(id => Definitions[id].price).ThenBy(id => id, StringComparer.Ordinal).ToArray();
        public static readonly string[] TeamColors = { "#61dfbb", "#f4bb65", "#af9aff", "#ff8193" };
        public static readonly string[] TeamNames = { "青藤", "琥珀", "紫晶", "绯红" };

        public static CreatureDefinition Get(string kind)
        {
            if (kind == "golem") kind = "iron_golem";
            CreatureDefinition definition;
            if (kind == null || !Definitions.TryGetValue(kind, out definition)) throw new InvalidOperationException("未知生物类型");
            return definition;
        }

        private static Dictionary<string, CreatureDefinition> Build()
        {
            var d = new Dictionary<string, CreatureDefinition>(StringComparer.Ordinal);
            Add(d, "allay", "悦灵", "fairy", "passive", 2, 9, 38, 3, 1, 95, 65, "#73e2ef", 0.7f, true, "flying,support", A("allay_heal", "悦灵之歌", "heal", 7, 110, 0, 4), A("allay_dodge", "轻盈之翼", "dodge", 0.2f, 0, 0, 0));
            Add(d, "armadillo", "犰狳", "armadillo", "passive", 1, 6, 62, 5, 0.7f, 23, 34, "#b88870", 0.8f, false, "beast", A("armadillo_armor", "卷甲防御", "armor", 0.25f, 0, 0, 0));
            Add(d, "axolotl", "美西螈", "fish", "passive", 2, 8, 54, 6, 1.1f, 24, 48, "#edb5ce", 0.65f, false, "aquatic,beast", A("axolotl_regen", "水栖复原", "regen", 1.8f, 0, 0, 0), A("axolotl_shield", "假死护盾", "shield", 16, 0, 0, 0));
            Add(d, "bat", "蝙蝠", "bat", "passive", 1, 3, 24, 3, 1.4f, 20, 85, "#68534b", 0.65f, true, "flying,beast", A("bat_dodge", "回声闪避", "dodge", 0.25f, 0, 0, 0));
            Add(d, "bee", "蜜蜂", "bee", "neutral", 1, 6, 30, 5, 1.2f, 23, 75, "#e6b842", 0.65f, true, "flying,arthropod", A("bee_poison", "蜂刺毒素", "poison", 2, 0, 3, 0));
            Add(d, "blaze", "烈焰人", "blaze", "hostile", 4, 24, 76, 12, 0.85f, 150, 42, "#f2bd45", 1, true, "flying,nether", A("blaze_burn", "烈焰火球", "burn", 4, 0, 4, 0), A("blaze_thorns", "火焰护体", "thorns", 0.2f, 0, 0, 0));
            Add(d, "bogged", "沼骸", "skeleton", "hostile", 3, 16, 42, 8, 0.8f, 145, 34, "#75875a", 1, false, "undead,skeleton", A("bogged_poison", "沼泽毒箭", "poison", 3, 0, 4, 0));
            Add(d, "breeze", "旋风人", "breeze", "hostile", 4, 25, 82, 10, 1.05f, 130, 64, "#9baabf", 1, true, "flying", A("breeze_knockback", "风弹击退", "knockback", 30, 0, 0, 0), A("breeze_dodge", "旋风身法", "dodge", 0.25f, 0, 0, 0));
            Add(d, "camel", "骆驼", "camel", "passive", 2, 11, 105, 7, 0.65f, 34, 52, "#d1a66d", 1.05f, false, "beast,mount", A("camel_charge", "沙丘冲刺", "charge", 0.85f, 0, 0, 0), A("camel_armor", "耐旱体魄", "armor", 0.1f, 0, 0, 0));
            Add(d, "camel_husk", "尸壳骆驼", "camel", "hostile", 3, 17, 118, 9, 0.65f, 34, 47, "#a09469", 1.05f, false, "undead,beast,mount", A("camel_husk_charge", "荒漠冲锋", "charge", 1.1f, 0, 0, 0), A("camel_husk_regen", "枯朽韧性", "regen", 1.2f, 0, 0, 0));
            Add(d, "cat", "猫", "quadruped", "passive", 1, 5, 32, 5, 1.4f, 22, 77, "#d9b27a", 0.6f, false, "beast", A("cat_dodge", "灵猫步", "dodge", 0.25f, 0, 0, 0));
            Add(d, "cave_spider", "洞穴蜘蛛", "spider", "hostile", 2, 10, 34, 5, 1.6f, 23, 79, "#3e6872", 0.8f, false, "arthropod", A("cave_spider_poison", "洞穴剧毒", "poison", 2.5f, 0, 4, 0));
            Add(d, "chicken", "鸡", "bird", "passive", 1, 3, 28, 3, 1.5f, 20, 59, "#eee8d5", 0.65f, false, "beast", A("chicken_haste", "啄击连发", "haste", 0.12f, 0, 0, 0));
            Add(d, "cod", "鳕鱼", "fish", "passive", 1, 3, 25, 4, 1.15f, 21, 58, "#b9aa84", 0.55f, false, "aquatic,beast", A("cod_dodge", "鱼群游弋", "dodge", 0.2f, 0, 0, 0));
            Add(d, "copper_golem", "铜傀儡", "golem", "passive", 2, 12, 94, 8, 0.8f, 27, 39, "#c67d50", 0.8f, false, "golem", A("copper_golem_armor", "氧化甲壳", "armor", 0.18f, 0, 0, 0), A("copper_golem_shield", "铜质屏障", "shield", 18, 0, 0, 0));
            Add(d, "cow", "牛", "quadruped", "passive", 1, 6, 86, 6, 0.65f, 27, 34, "#916b52", 0.95f, false, "beast", A("cow_armor", "坚实体格", "armor", 0.12f, 0, 0, 0));
            Add(d, "creaking", "嘎枝", "creaking", "hostile", 5, 37, 150, 17, 0.8f, 33, 42, "#756d52", 1.15f, false, "construct", A("creaking_armor", "树心护甲", "armor", 0.28f, 0, 0, 0), A("creaking_slow", "树脂缠绕", "slow", 0.35f, 0, 3, 0), A("creaking_regen", "林心愈合", "regen", 2, 0, 0, 0));
            Add(d, "creeper", "苦力怕", "creeper", "hostile", 2, 11, 34, 38, 0.7f, 29, 49, "#90cc50", 1, false, "monster", A("creeper_explode", "引信自爆", "explode", 1, 82, 0, 0));
            Add(d, "dolphin", "海豚", "fish", "neutral", 2, 11, 58, 7, 1.2f, 26, 83, "#9abac2", 1.1f, false, "aquatic,beast", A("dolphin_heal", "海豚恩惠", "heal", 6, 100, 0, 4), A("dolphin_charge", "跃浪突击", "charge", 0.7f, 0, 0, 0));
            Add(d, "donkey", "驴", "quadruped", "passive", 1, 7, 82, 6, 0.8f, 27, 47, "#8a8279", 0.95f, false, "beast,mount", A("donkey_shield", "坚韧驮行", "shield", 20, 0, 0, 0));
            Add(d, "drowned", "溺尸", "zombie", "hostile", 3, 15, 70, 10, 0.75f, 125, 37, "#638e84", 1, false, "undead,zombie,aquatic", A("drowned_pierce", "三叉戟贯穿", "pierce", 0.2f, 0, 0, 0), A("drowned_regen", "深水恢复", "regen", 1, 0, 0, 0));
            Add(d, "elder_guardian", "远古守卫者", "guardian", "hostile", 5, 42, 185, 19, 0.7f, 165, 26, "#b8b69d", 1.25f, false, "aquatic", A("elder_guardian_slow", "远古疲劳", "slow", 0.45f, 0, 4, 0), A("elder_guardian_thorns", "棘刺铠甲", "thorns", 0.3f, 0, 0, 0), A("elder_guardian_shield", "深海屏障", "shield", 38, 0, 0, 0));
            Add(d, "ender_dragon", "末影龙", "dragon", "boss", 6, 80, 430, 30, 0.8f, 155, 60, "#625077", 1.35f, true, "boss,flying,end", A("ender_dragon_cleave", "末影龙息", "cleave", 0.7f, 100, 0, 0), A("ender_dragon_burn", "龙息灼蚀", "burn", 6, 0, 5, 0), A("ender_dragon_armor", "龙鳞护体", "armor", 0.28f, 0, 0, 0));
            Add(d, "enderman", "末影人", "enderman", "neutral", 4, 25, 88, 17, 1.05f, 29, 72, "#3f324d", 1.1f, false, "end", A("enderman_blink", "末影跃迁", "blink", 0, 230, 0, 5), A("enderman_lifesteal", "虚空汲取", "lifesteal", 0.2f, 0, 0, 0));
            Add(d, "endermite", "末影螨", "arthropod", "hostile", 1, 4, 27, 4, 1.6f, 20, 66, "#827295", 0.55f, false, "arthropod,end", A("endermite_lifesteal", "虚空噬咬", "lifesteal", 0.15f, 0, 0, 0));
            Add(d, "evoker", "唤魔者", "illager", "hostile", 5, 38, 72, 13, 0.8f, 145, 36, "#57525b", 1, false, "illager", A("evoker_cleave", "唤魔尖牙", "cleave", 0.65f, 65, 0, 0), A("evoker_summon_vex", "召唤恼鬼", "summon_vex", 2, 0, 0, 9));
            Add(d, "fox", "狐狸", "quadruped", "passive", 2, 8, 42, 7, 1.4f, 24, 78, "#d9803a", 0.7f, false, "beast", A("fox_charge", "狐影奔袭", "charge", 0.9f, 0, 0, 0), A("fox_dodge", "敏捷躲闪", "dodge", 0.2f, 0, 0, 0));
            Add(d, "frog", "青蛙", "frog", "passive", 1, 5, 38, 5, 1.1f, 50, 47, "#a2b065", 0.7f, false, "aquatic,beast", A("frog_slow", "长舌拖拽", "slow", 0.3f, 0, 2, 0));
            Add(d, "ghast", "恶魂", "ghast", "hostile", 4, 27, 75, 20, 0.5f, 180, 31, "#e9e3dd", 1.1f, true, "flying,nether", A("ghast_cleave", "爆裂火球", "cleave", 0.6f, 65, 0, 0), A("ghast_burn", "地狱余烬", "burn", 3, 0, 4, 0));
            Add(d, "giant", "巨人", "zombie", "hostile", 5, 46, 290, 29, 0.48f, 45, 29, "#658e50", 1.8f, false, "undead,zombie", A("giant_cleave", "巨人践踏", "cleave", 0.5f, 70, 0, 0), A("giant_armor", "庞然身躯", "armor", 0.16f, 0, 0, 0));
            Add(d, "glow_squid", "发光鱿鱼", "squid", "passive", 2, 9, 48, 4, 1.05f, 90, 43, "#69c7ba", 0.85f, false, "aquatic,beast", A("glow_squid_rally", "荧光庇护", "rally", 9, 100, 0, 5), A("glow_squid_slow", "光墨减速", "slow", 0.2f, 0, 2, 0));
            Add(d, "goat", "山羊", "quadruped", "neutral", 2, 10, 65, 9, 0.8f, 27, 62, "#e3decf", 0.85f, false, "beast", A("goat_charge", "山羊冲撞", "charge", 1.2f, 0, 0, 0), A("goat_knockback", "角击退敌", "knockback", 22, 0, 0, 0));
            Add(d, "guardian", "守卫者", "guardian", "hostile", 3, 19, 92, 12, 0.8f, 145, 35, "#6c9690", 0.9f, false, "aquatic", A("guardian_pierce", "激光穿甲", "pierce", 0.2f, 0, 0, 0), A("guardian_thorns", "守卫尖刺", "thorns", 0.22f, 0, 0, 0));
            Add(d, "happy_ghast", "快乐恶魂", "ghast", "passive", 4, 28, 140, 6, 0.7f, 115, 38, "#f2e9dc", 1.15f, true, "flying,nether,support", A("happy_ghast_rally", "云团庇护", "rally", 14, 135, 0, 5), A("happy_ghast_heal", "快乐之息", "heal", 11, 135, 0, 4));
            Add(d, "hoglin", "疣猪兽", "quadruped", "hostile", 3, 18, 125, 13, 0.75f, 32, 46, "#b7826e", 1.15f, false, "beast,nether", A("hoglin_knockback", "獠牙顶撞", "knockback", 24, 0, 0, 0), A("hoglin_charge", "蛮力冲锋", "charge", 0.8f, 0, 0, 0));
            Add(d, "horse", "马", "quadruped", "passive", 2, 9, 78, 7, 0.95f, 27, 78, "#a57550", 1.05f, false, "beast,mount", A("horse_charge", "疾驰践踏", "charge", 1, 0, 0, 0), A("horse_haste", "快马奔腾", "haste", 0.12f, 0, 0, 0));
            Add(d, "husk", "尸壳", "zombie", "hostile", 2, 9, 74, 8, 0.75f, 25, 38, "#b5a176", 1, false, "undead,zombie", A("husk_slow", "饥饿撕咬", "slow", 0.25f, 0, 3, 0), A("husk_armor", "沙漠皮肤", "armor", 0.1f, 0, 0, 0));
            Add(d, "illusioner", "幻术师", "illager", "hostile", 4, 29, 62, 10, 1, 155, 43, "#667ea2", 1, false, "illager", A("illusioner_dodge", "镜像步法", "dodge", 0.33f, 0, 0, 0), A("illusioner_shield", "幻术屏障", "shield", 30, 0, 0, 0), A("illusioner_pierce", "幻影箭", "pierce", 0.15f, 0, 0, 0));
            Add(d, "iron_golem", "铁傀儡", "golem", "neutral", 4, 30, 160, 17, 0.65f, 34, 27, "#cbd4bf", 1, false, "golem", A("iron_golem_cleave", "铁臂横扫", "cleave", 0.6f, 46, 0, 0), A("iron_golem_armor", "铁铸身躯", "armor", 0.16f, 0, 0, 0));
            Add(d, "llama", "羊驼", "quadruped", "neutral", 2, 9, 68, 7, 0.9f, 120, 43, "#c7b28d", 1, false, "beast,mount", A("llama_slow", "羊驼唾沫", "slow", 0.25f, 0, 2, 0));
            Add(d, "magma_cube", "岩浆怪", "slime", "hostile", 3, 18, 82, 10, 0.8f, 27, 38, "#8d3b2e", 1, false, "nether", A("magma_cube_split", "熔岩分裂", "split", 2, 0, 0, 0), A("magma_cube_burn", "炽热接触", "burn", 2, 0, 3, 0));
            Add(d, "mooshroom", "哞菇", "quadruped", "passive", 2, 11, 88, 6, 0.7f, 27, 33, "#bc4d46", 0.95f, false, "beast,support", A("mooshroom_heal", "蘑菇浓汤", "heal", 9, 95, 0, 5), A("mooshroom_regen", "菌丝再生", "regen", 1, 0, 0, 0));
            Add(d, "mule", "骡", "quadruped", "passive", 2, 9, 91, 7, 0.85f, 27, 53, "#897966", 1, false, "beast,mount", A("mule_armor", "骡子韧性", "armor", 0.12f, 0, 0, 0), A("mule_shield", "行军护盾", "shield", 20, 0, 0, 0));
            Add(d, "nautilus", "鹦鹉螺", "nautilus", "passive", 3, 14, 98, 8, 0.85f, 27, 51, "#d8c2a2", 0.95f, false, "aquatic,beast,mount", A("nautilus_armor", "螺壳装甲", "armor", 0.24f, 0, 0, 0), A("nautilus_charge", "水下冲刺", "charge", 0.65f, 0, 0, 0));
            Add(d, "ocelot", "豹猫", "quadruped", "passive", 2, 8, 39, 7, 1.45f, 23, 83, "#d9bc65", 0.7f, false, "beast", A("ocelot_charge", "丛林伏击", "charge", 1.1f, 0, 0, 0), A("ocelot_dodge", "斑影闪避", "dodge", 0.2f, 0, 0, 0));
            Add(d, "panda", "熊猫", "quadruped", "neutral", 3, 16, 132, 11, 0.65f, 29, 30, "#e0ddd5", 1.1f, false, "beast", A("panda_regen", "竹食恢复", "regen", 2.2f, 0, 0, 0), A("panda_armor", "厚实皮毛", "armor", 0.12f, 0, 0, 0));
            Add(d, "parched", "焦骸", "skeleton", "hostile", 3, 16, 46, 9, 0.85f, 150, 36, "#b7a77b", 1, false, "undead,skeleton", A("parched_slow", "虚弱之箭", "slow", 0.35f, 0, 3, 0), A("parched_pierce", "沙骨穿甲", "pierce", 0.1f, 0, 0, 0));
            Add(d, "parrot", "鹦鹉", "bird", "passive", 1, 5, 27, 4, 1.2f, 75, 75, "#e35b54", 0.6f, true, "flying,beast,support", A("parrot_rally", "鼓舞鸣唱", "rally", 6, 90, 0, 5));
            Add(d, "phantom", "幻翼", "phantom", "hostile", 3, 18, 56, 11, 1.15f, 28, 88, "#5f7195", 1, true, "flying,undead", A("phantom_charge", "夜空俯冲", "charge", 1.3f, 0, 0, 0), A("phantom_lifesteal", "噩梦吞噬", "lifesteal", 0.2f, 0, 0, 0));
            Add(d, "pig", "猪", "quadruped", "passive", 1, 4, 57, 5, 0.85f, 24, 43, "#dda19e", 0.8f, false, "beast", A("pig_armor", "滚泥护甲", "armor", 0.1f, 0, 0, 0));
            Add(d, "piglin", "猪灵", "piglin", "neutral", 3, 15, 76, 11, 1, 110, 46, "#d09b84", 1, false, "nether", A("piglin_pierce", "金刃破甲", "pierce", 0.18f, 0, 0, 0), A("piglin_lifesteal", "贪战汲取", "lifesteal", 0.1f, 0, 0, 0));
            Add(d, "piglin_brute", "猪灵蛮兵", "piglin", "hostile", 4, 28, 145, 20, 0.8f, 30, 45, "#a77c62", 1.1f, false, "nether", A("piglin_brute_frenzy", "金斧狂怒", "frenzy", 0.4f, 0, 0, 0), A("piglin_brute_armor", "蛮兵护甲", "armor", 0.18f, 0, 0, 0));
            Add(d, "pillager", "掠夺者", "illager", "hostile", 3, 17, 62, 12, 0.8f, 155, 38, "#878680", 1, false, "illager", A("pillager_pierce", "穿透弩箭", "pierce", 0.25f, 0, 0, 0));
            Add(d, "polar_bear", "北极熊", "quadruped", "neutral", 3, 17, 128, 13, 0.7f, 31, 41, "#ece8d9", 1.15f, false, "beast", A("polar_bear_slow", "冰原掌击", "slow", 0.3f, 0, 2, 0), A("polar_bear_frenzy", "护崽怒意", "frenzy", 0.3f, 0, 0, 0));
            Add(d, "pufferfish", "河豚", "fish", "neutral", 2, 8, 42, 4, 1, 23, 36, "#e4c55a", 0.6f, false, "aquatic,beast", A("pufferfish_thorns", "膨胀毒刺", "thorns", 0.3f, 0, 0, 0), A("pufferfish_poison", "河豚毒素", "poison", 2.5f, 0, 3, 0));
            Add(d, "rabbit", "兔子", "rabbit", "passive", 1, 3, 24, 4, 1.5f, 20, 86, "#b6a08a", 0.55f, false, "beast", A("rabbit_dodge", "蹦跳闪避", "dodge", 0.25f, 0, 0, 0));
            Add(d, "ravager", "劫掠兽", "quadruped", "hostile", 5, 43, 230, 23, 0.65f, 37, 38, "#8c8980", 1.25f, false, "illager,beast", A("ravager_cleave", "劫掠践踏", "cleave", 0.55f, 60, 0, 0), A("ravager_stun", "震地怒吼", "stun", 0, 0, 0.55f, 6), A("ravager_armor", "兽皮铁壁", "armor", 0.15f, 0, 0, 0));
            Add(d, "salmon", "鲑鱼", "fish", "passive", 1, 4, 33, 5, 1.15f, 23, 63, "#bb7774", 0.65f, false, "aquatic,beast", A("salmon_haste", "逆流疾游", "haste", 0.15f, 0, 0, 0));
            Add(d, "sheep", "绵羊", "quadruped", "passive", 1, 5, 62, 4, 0.8f, 24, 38, "#e5e0d4", 0.85f, false, "beast", A("sheep_shield", "厚羊毛", "shield", 24, 0, 0, 0));
            Add(d, "shulker", "潜影贝", "shulker", "hostile", 4, 24, 104, 11, 0.8f, 155, 20, "#a487af", 0.9f, false, "end", A("shulker_armor", "闭壳护甲", "armor", 0.32f, 0, 0, 0), A("shulker_stun", "潜影浮弹", "stun", 0, 0, 0.5f, 4));
            Add(d, "silverfish", "蠹虫", "arthropod", "hostile", 1, 3, 25, 4, 1.6f, 19, 63, "#98978e", 0.5f, false, "arthropod", A("silverfish_pierce", "钻隙噬甲", "pierce", 0.2f, 0, 0, 0));
            Add(d, "skeleton", "骷髅", "skeleton", "hostile", 1, 7, 32, 8, 0.9f, 145, 34, "#e3dfc8", 1, false, "undead,skeleton", A("skeleton_pierce", "骨箭穿甲", "pierce", 0.12f, 0, 0, 0));
            Add(d, "skeleton_horse", "骷髅马", "quadruped", "neutral", 3, 14, 77, 9, 1, 29, 85, "#d9d5c4", 1.05f, false, "undead,beast,mount", A("skeleton_horse_charge", "亡灵冲锋", "charge", 1.2f, 0, 0, 0), A("skeleton_horse_armor", "骨架韧性", "armor", 0.1f, 0, 0, 0));
            Add(d, "slime", "史莱姆", "slime", "hostile", 2, 10, 68, 8, 0.75f, 26, 35, "#8be59b", 1, false, "monster", A("slime_split", "黏液分裂", "split", 2, 0, 0, 0));
            Add(d, "sniffer", "嗅探兽", "sniffer", "passive", 3, 19, 155, 9, 0.65f, 32, 29, "#aa7055", 1.15f, false, "beast,support", A("sniffer_heal", "远古草药", "heal", 13, 110, 0, 5), A("sniffer_armor", "苔背护甲", "armor", 0.15f, 0, 0, 0));
            Add(d, "snow_golem", "雪傀儡", "snow_golem", "passive", 2, 8, 42, 5, 1.2f, 130, 35, "#ecefeb", 0.9f, false, "golem", A("snow_golem_slow", "冰冷雪球", "slow", 0.3f, 0, 2, 0));
            Add(d, "spider", "蜘蛛", "spider", "neutral", 1, 7, 40, 6, 1.5f, 25, 76, "#6e526a", 1, false, "arthropod", A("spider_slow", "蛛网牵制", "slow", 0.15f, 0, 1.5f, 0));
            Add(d, "squid", "鱿鱼", "squid", "passive", 1, 6, 49, 5, 0.9f, 85, 40, "#647f91", 0.85f, false, "aquatic,beast", A("squid_slow", "墨汁干扰", "slow", 0.3f, 0, 3, 0));
            Add(d, "stray", "流浪者", "skeleton", "hostile", 3, 15, 44, 9, 0.85f, 150, 33, "#a0b4bc", 1, false, "undead,skeleton", A("stray_slow", "冰霜箭", "slow", 0.4f, 0, 3, 0));
            Add(d, "strider", "炽足兽", "strider", "passive", 2, 8, 65, 6, 0.85f, 25, 53, "#b16161", 0.9f, false, "beast,nether,mount", A("strider_burn", "炽足踏击", "burn", 2, 0, 3, 0));
            Add(d, "tadpole", "蝌蚪", "fish", "passive", 1, 3, 23, 3, 1.25f, 20, 57, "#796551", 0.45f, false, "aquatic,beast", A("tadpole_regen", "水生新芽", "regen", 1.4f, 0, 0, 0));
            Add(d, "trader_llama", "行商羊驼", "quadruped", "neutral", 2, 12, 76, 8, 0.9f, 125, 43, "#cbb897", 1, false, "beast,mount,support", A("trader_llama_rally", "商队守护", "rally", 8, 100, 0, 5), A("trader_llama_slow", "护主唾沫", "slow", 0.25f, 0, 2, 0));
            Add(d, "tropical_fish", "热带鱼", "fish", "passive", 1, 4, 28, 4, 1.3f, 22, 64, "#ec9e57", 0.55f, false, "aquatic,beast", A("tropical_fish_heal", "珊瑚微光", "heal", 4, 70, 0, 4));
            Add(d, "turtle", "海龟", "turtle", "passive", 2, 10, 94, 5, 0.6f, 24, 25, "#6a9b65", 0.9f, false, "aquatic,beast", A("turtle_armor", "龟甲屏障", "armor", 0.32f, 0, 0, 0), A("turtle_regen", "缓慢复苏", "regen", 1, 0, 0, 0));
            Add(d, "vex", "恼鬼", "fairy", "hostile", 3, 15, 38, 9, 1.6f, 24, 95, "#a1b4cf", 0.7f, true, "flying,illager", A("vex_pierce", "穿墙突袭", "pierce", 0.25f, 0, 0, 0), A("vex_dodge", "幽灵闪避", "dodge", 0.2f, 0, 0, 0));
            Add(d, "villager", "村民", "humanoid", "passive", 2, 10, 58, 3, 0.7f, 95, 37, "#ae8b69", 1, false, "villager,support", A("villager_heal", "农夫药膳", "heal", 10, 110, 0, 4));
            Add(d, "vindicator", "卫道士", "illager", "hostile", 3, 19, 85, 17, 0.95f, 28, 49, "#9b8b7e", 1, false, "illager", A("vindicator_pierce", "利斧破甲", "pierce", 0.3f, 0, 0, 0), A("vindicator_frenzy", "卫道狂热", "frenzy", 0.25f, 0, 0, 0));
            Add(d, "wandering_trader", "流浪商人", "humanoid", "passive", 3, 15, 62, 4, 0.8f, 105, 42, "#5d78ab", 1, false, "villager,support", A("wandering_trader_heal", "旅行补给", "heal", 9, 130, 0, 4), A("wandering_trader_dodge", "隐身脱险", "dodge", 0.25f, 0, 0, 0));
            Add(d, "warden", "监守者", "warden", "hostile", 6, 58, 350, 29, 0.6f, 145, 34, "#315b61", 1.15f, false, "monster", A("warden_pierce", "音波穿透", "pierce", 0.5f, 0, 0, 0), A("warden_cleave", "幽匿震荡", "cleave", 0.5f, 75, 0, 0), A("warden_armor", "深暗甲壳", "armor", 0.28f, 0, 0, 0));
            Add(d, "witch", "女巫", "humanoid", "hostile", 4, 24, 65, 7, 0.85f, 145, 36, "#786492", 1, false, "monster", A("witch_poison", "剧毒药水", "poison", 4, 0, 5, 0), A("witch_heal", "治疗药剂", "heal", 15, 100, 0, 5), A("witch_slow", "迟缓药水", "slow", 0.25f, 0, 3, 0));
            Add(d, "wither", "凋灵", "wither", "boss", 6, 75, 360, 25, 0.95f, 175, 49, "#595966", 1.3f, true, "boss,flying,undead,nether", A("wither_cleave", "凋灵之首", "cleave", 0.55f, 80, 0, 0), A("wither_poison", "凋零侵蚀", "poison", 7, 0, 5, 0), A("wither_regen", "不灭凋灵", "regen", 4, 0, 0, 0));
            Add(d, "wither_skeleton", "凋灵骷髅", "skeleton", "hostile", 4, 23, 93, 15, 0.9f, 31, 48, "#55545a", 1.12f, false, "undead,skeleton,nether", A("wither_skeleton_poison", "凋零之刃", "poison", 4, 0, 4, 0), A("wither_skeleton_execute", "黑骨斩杀", "execute", 0.45f, 0, 0, 0));
            Add(d, "wolf", "狼", "quadruped", "neutral", 2, 10, 57, 8, 1.35f, 25, 70, "#b8b9b2", 0.8f, false, "beast", A("wolf_charge", "狼群猛袭", "charge", 0.8f, 0, 0, 0), A("wolf_lifesteal", "嗜血猎食", "lifesteal", 0.15f, 0, 0, 0));
            Add(d, "zoglin", "僵尸疣猪兽", "quadruped", "hostile", 3, 20, 140, 14, 0.75f, 33, 45, "#ad8a78", 1.15f, false, "undead,beast,nether", A("zoglin_poison", "腐化獠牙", "poison", 2, 0, 3, 0), A("zoglin_knockback", "失控顶撞", "knockback", 25, 0, 0, 0));
            Add(d, "zombie", "僵尸", "zombie", "hostile", 1, 6, 58, 9, 0.8f, 24, 42, "#76b45d", 1, false, "undead,zombie", A("zombie_regen", "腐肉复苏", "regen", 0.8f, 0, 0, 0));
            Add(d, "zombie_horse", "僵尸马", "quadruped", "neutral", 3, 13, 96, 8, 0.9f, 28, 71, "#7c9460", 1.05f, false, "undead,beast,mount", A("zombie_horse_charge", "腐蹄冲击", "charge", 1, 0, 0, 0), A("zombie_horse_regen", "亡躯恢复", "regen", 1.2f, 0, 0, 0));
            Add(d, "zombie_nautilus", "僵尸鹦鹉螺", "nautilus", "hostile", 3, 18, 112, 10, 0.8f, 28, 47, "#8eac81", 0.95f, false, "undead,aquatic,beast,mount", A("zombie_nautilus_armor", "腐蚀螺壳", "armor", 0.22f, 0, 0, 0), A("zombie_nautilus_poison", "深渊毒触", "poison", 2, 0, 4, 0));
            Add(d, "zombie_villager", "僵尸村民", "zombie", "hostile", 2, 9, 68, 8, 0.8f, 25, 39, "#84956a", 1, false, "undead,zombie,villager", A("zombie_villager_poison", "感染撕咬", "poison", 1.5f, 0, 3, 0), A("zombie_villager_regen", "残存生机", "regen", 1, 0, 0, 0));
            Add(d, "zombified_piglin", "僵尸猪灵", "zombie", "neutral", 3, 15, 87, 12, 1, 27, 46, "#b79177", 1, false, "undead,nether", A("zombified_piglin_lifesteal", "亡金之刃", "lifesteal", 0.15f, 0, 0, 0), A("zombified_piglin_frenzy", "群怒狂击", "frenzy", 0.3f, 0, 0, 0));
            return d;
        }

        private static string A(string id, string name, string effect, float value, float radius, float duration, float cooldown)
        {
            return CreatureAbilities.Register(id, name, effect, value, radius, duration, cooldown);
        }
        private static void Add(Dictionary<string, CreatureDefinition> d, string id, string name, string model, string category, int tier, int price,
            float hp, float attack, float attackSpeed, float range, float speed, string color, float visualScale, bool flying, string tags, params string[] skills)
        {
            string role = category == "boss" ? "终局首领" : tags.Contains("support") ? "军团支援" : range > 70 ? "远程输出" : hp >= 100 ? "近战前排" : speed >= 65 ? "迅捷突袭" : "近战战士";
            d.Add(id, new CreatureDefinition { id = id, name = name, role = role, model = model, category = category, tier = tier, price = price,
                hp = hp, attack = attack, attackSpeed = attackSpeed, range = range, speed = speed, color = color, visualScale = visualScale, flying = flying,
                tags = tags.Split(','), skills = skills, power = hp / 20 + attack * attackSpeed + range * .02f + skills.Length * 3,
                description = string.Join("；", skills.Select(skill => CreatureAbilities.GetRegistered(skill).name).ToArray()) + "。" });
        }
    }
}
