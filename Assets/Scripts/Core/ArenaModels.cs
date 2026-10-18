using System;
using System.Collections.Generic;

namespace BlockArena.Core
{
    [Serializable]
    public struct Point2
    {
        public float x, y;
        public Point2(float x, float y) { this.x = x; this.y = y; }
    }

    [Serializable]
    public sealed class GameCommand
    {
        public string type, unitId, placementId, offerId;
        public int index = -1;
        public float x, y, dx, dy;
        public bool bots = true;
    }

    [Serializable]
    public sealed class GameState
    {
        public string hostId, phase = "lobby", winnerId, message;
        public int round;
        public float timeLeft;
        public List<PlayerState> players = new List<PlayerState>();
        public List<FighterState> fighters = new List<FighterState>();
        public List<EffectState> effects = new List<EffectState>();
        public List<PlacementState> placements = new List<PlacementState>();
    }

    [Serializable]
    public sealed class PlayerState
    {
        public string id, name;
        public int slot, hp = 20;
        public int gold, roundKills, totalKills, lastIncome, killIncome, rankIncome, baseIncome, lastRank;
        public float shopRefreshLeft;
        public bool ready, bot, connected = true, eliminated, recruitPending;
        public string[] shop = new string[0];
        public string recruitChoice;
        public List<UnitState> units = new List<UnitState>();
        public List<ShopOffer> market = new List<ShopOffer>();
        public List<string> upgrades = new List<string>();
        public Point2 steve;
        internal float moveX, moveY, moveUntil, botAt;
    }

    [Serializable]
    public sealed class UnitState
    {
        public string id, kind, placementId;
        public float x, y;
        public List<string> equipment = new List<string>();
    }

    [Serializable]
    public sealed class FighterState
    {
        public string id, unitId, ownerId, kind;
        public int slot;
        public float x, y, hp, maxHp, scale = 1, attackTime;
        public float attack, attackSpeed, range, speed;
        public string[] skills = new string[0], equipment = new string[0];
        public float armor, shield, poisonTime, burnTime, slowTime, stunTime;
        public bool summoned;
        public bool dead;
        internal float attackCooldown, radius, think, side;
        internal bool mini;
        internal string targetId;
        internal string lastAttackerId;
        internal float poisonTick, burnTick, poisonDps, burnDps, slowMultiplier = 1;
        internal FighterState poisonSource, burnSource;
        internal int attacks, hits;
        internal bool deathTriggered;
        internal Dictionary<string, float> abilityCooldowns = new Dictionary<string, float>();
    }

    [Serializable]
    public sealed class EffectState
    {
        public string id, type;
        public float x, y, age, targetX, targetY, value;
        public int slot;
    }

    [Serializable]
    public sealed class PlacementState
    {
        public string playerId;
        public int rank, damage, kills, income, baseIncome, killIncome, rankIncome;
    }

    [Serializable]
    public sealed class CreatureDefinition
    {
        public string id, name, role, description, color;
        public string model, category;
        public string[] tags = new string[0], skills = new string[0];
        public int tier, price;
        public bool flying;
        public float visualScale = 1, power;
        public float hp, attack, attackSpeed, range, speed;
    }

    [Serializable]
    public sealed class AbilityDefinition
    {
        public string id, name, description, icon, effect;
        public float value, radius, duration, cooldown;
    }

    [Serializable]
    public sealed class ShopOffer
    {
        public string id, kind, itemId;
        public int price;
        public bool sold;
    }

    [Serializable]
    public sealed class TradeDefinition
    {
        public string id, name, description, type, icon, target, grantedSkill;
        public int price;
        public float hp, attack, attackSpeed, range, speed, armor;
    }
}
