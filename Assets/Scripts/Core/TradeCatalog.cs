using System;
using System.Collections.Generic;

namespace BlockArena.Core
{
    public static class TradeCatalog
    {
        public const int RefreshPrice = 2, MarketSlots = 4;
        static readonly Dictionary<string, TradeDefinition> items = new Dictionary<string, TradeDefinition>();
        public static readonly string[] Ids;

        static TradeCatalog()
        {
            Skill("undead_legion", "亡灵军势", "全部亡灵生命 +25、攻击 +4；持续影响以后招募的亡灵。", "undead", 12, hp:25, attack:4);
            Skill("aerial_hunt", "高空猎手", "全部飞行生物攻速 +25%、移速 +20%。", "flying", 12, attackSpeed:.25f, speed:.2f);
            Skill("beast_pack", "兽群协同", "全部野兽生命 +18、攻速 +20%。", "beast", 10, hp:18, attackSpeed:.2f);
            Skill("ocean_blessing", "潮汐祝福", "全部水生生物生命 +35、移速 +15%。", "aquatic", 10, hp:35, speed:.15f);
            Skill("nether_forge", "下界熔炉", "全部下界生物攻击 +8、减伤 +10%。", "nether", 14, attack:8, armor:.1f);
            Skill("illager_banner", "灾厄战旗", "全部灾厄村民攻击 +5、攻速 +15%。", "illager", 11, attack:5, attackSpeed:.15f);
            Skill("golem_foundry", "傀儡铸造", "全部傀儡生命 +60、减伤 +12%。", "golem", 14, hp:60, armor:.12f);
            Skill("arthropod_swarm", "节肢狂潮", "全部节肢生物攻速 +30%、移速 +15%。", "arthropod", 10, attackSpeed:.3f, speed:.15f);
            Skill("skeleton_school", "白骨箭术", "骷髅系生物攻击 +7、射程 +40。", "skeleton", 9, attack:7, range:40);
            Skill("zombie_resilience", "不朽躯壳", "僵尸系生物生命 +45、减伤 +10%。", "zombie", 8, hp:45, armor:.1f);
            Skill("creeper_charge", "高能引线", "苦力怕攻击 +24、移速 +15%。", "creeper", 12, attack:24, speed:.15f);
            Skill("slime_brood", "黏液增殖", "史莱姆生命 +40、攻速 +25%，分裂体继承增益。", "slime", 10, hp:40, attackSpeed:.25f);
            Skill("dragon_hoard", "龙王宝库", "末影龙生命 +120、攻击 +20。", "ender_dragon", 25, hp:120, attack:20);
            Grant("undead_legion", "lifesteal", " 额外获得攻击吸血。");
            Grant("aerial_hunt", "dodge", " 额外获得闪避。");
            Grant("ocean_blessing", "regen", " 额外获得持续再生。");
            Grant("arthropod_swarm", "poison", " 攻击额外施加中毒。");
            Grant("golem_foundry", "shield", " 额外获得开场护盾。");
            Grant("skeleton_school", "pierce", " 攻击额外穿透护甲。");
            Grant("zombie_resilience", "thorns", " 额外获得荆棘反伤。");
            Ids = new List<string>(items.Keys).ToArray();
        }
        static void Skill(string id,string name,string description,string target,int price,float hp=0,float attack=0,float attackSpeed=0,float range=0,float speed=0,float armor=0)
        { Add(id,name,description,"skill","book",target,price,hp,attack,attackSpeed,range,speed,armor); }
        static void Add(string id,string name,string description,string type,string icon,string target,int price,float hp,float attack,float attackSpeed,float range,float speed,float armor)
        { items.Add(id,new TradeDefinition{id=id,name=name,description=description,type=type,icon=icon,target=target,price=price,hp=hp,attack=attack,attackSpeed=attackSpeed,range=range,speed=speed,armor=armor}); }
        static void Grant(string id,string skill,string description) { items[id].grantedSkill=skill;items[id].description+=description; }
        public static TradeDefinition Get(string id)
        { TradeDefinition item;if(id==null||!items.TryGetValue(id,out item))throw new InvalidOperationException("未知商店商品");return item; }
        public static string[] AvailableSkills(PlayerState player)
        {
            var available = new List<string>();
            foreach (string id in Ids) if (player == null || !player.upgrades.Contains(id)) available.Add(id);
            return available.ToArray();
        }
        public static bool Applies(TradeDefinition item,CreatureDefinition creature)
        {
            if(item.target=="all"||item.target==creature.id)return true;
            foreach(string tag in creature.tags)if(tag==item.target)return true;
            return false;
        }
    }
}
