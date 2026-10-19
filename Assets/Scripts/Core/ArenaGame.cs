using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BlockArena.Core
{
    /// <summary>Dependency-free authoritative rules, shared by the host and rule checks.</summary>
    public sealed partial class ArenaGame
    {
        public const float RecruitSeconds = 25f, PrepSeconds = 45f, BattleSeconds = 55f, ResultsSeconds = 6f;
        public GameState State { get; private set; }
        public int Seed { get; private set; }
        private readonly Random random;
        private int nextId;
        private float clock, readyElapsed;
        private int damageDepth;
        private readonly List<DefeatGroup> defeats = new List<DefeatGroup>();
        private readonly List<string> participants = new List<string>();
        private sealed class DefeatGroup { public List<string> ids = new List<string>(); public float score; }

        public ArenaGame(string hostId, string hostName, int? seed = null)
        {
            if (String.IsNullOrWhiteSpace(hostId)) Fail("玩家身份无效");
            Seed = seed ?? NewSeed();
            random = new Random(Seed);
            State = new GameState { hostId = hostId, message = "等待冒险家加入" };
            State.players.Add(MakePlayer(hostId, hostName, 0, false));
        }

        private static int NewSeed()
        {
            byte[] bytes = new byte[4];
            using (RandomNumberGenerator generator = RandomNumberGenerator.Create()) generator.GetBytes(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }

        public void AddPlayer(string id, string name, bool bot = false)
        {
            if (State.phase != "lobby") Fail("对战已经开始");
            if (String.IsNullOrWhiteSpace(id) || State.players.Any(p => p.id == id)) Fail("玩家身份无效或重复");
            if (State.players.Count >= 4) Fail("房间已满");
            int slot = Enumerable.Range(0, 4).First(s => State.players.All(p => p.slot != s));
            State.players.Add(MakePlayer(id, name, slot, bot));
        }

        public void RemovePlayer(string id)
        {
            PlayerState p = State.players.Find(player => player.id == id);
            if (p == null) return;
            if (State.phase == "lobby") State.players.Remove(p);
            else { p.connected = false; p.bot = true; p.moveUntil = 0; }
            if (State.hostId == id)
            {
                PlayerState successor = State.players.Find(player => player.connected && !player.bot && player.id != id)
                    ?? State.players.Find(player => player.id != id);
                State.hostId = successor == null ? null : successor.id;
            }
        }

        public void SetConnected(string id, bool connected)
        {
            PlayerState p = Player(id);
            p.connected = connected;
            if (!connected) { p.moveUntil = 0; p.botAt = Math.Min(p.botAt, clock + 1f); }
        }

        public void Apply(string playerId, GameCommand command)
        {
            PlayerState p = Player(playerId);
            if (command == null) Fail("操作无效");
            string type = command.type;
            if (type == "start")
            {
                if (playerId != State.hostId) Fail("只有房主可以开始");
                if (State.phase != "lobby") Fail("对战已经开始");
                if (!command.bots && State.players.Count < 2) Fail("至少需要两位玩家，或添加机器人");
                Start(command.bots);
                return;
            }
            if (type == "restart")
            {
                if (playerId != State.hostId) Fail("只有房主可以重新开始");
                if (State.phase != "finished") Fail("对战尚未结束");
                Start(State.players.Count < 2);
                return;
            }
            if (p.eliminated) Fail("你已被淘汰，可以继续观战");
            if (type == "move")
            {
                if (!Finite(command.dx) || !Finite(command.dy)) Fail("移动方向无效");
                double length = Math.Sqrt((double)command.dx * command.dx + (double)command.dy * command.dy);
                double factor = Math.Max(1d, length);
                p.moveX = (float)(command.dx / factor); p.moveY = (float)(command.dy / factor); p.moveUntil = clock + .28f;
                return;
            }
            if (type == "choose")
            {
                if (State.phase != "recruit") Fail("请在招募阶段选择生物");
                if (!p.recruitPending) Fail("本轮已经完成招募");
                ValidateRecruitOffer(p, command.offerId);
                if (command.index < 0 || command.index > 2 || p.shop == null || p.shop.Length != 3) Fail("招募位置无效");
                CreatureCatalog.Get(p.shop[command.index]);
                Choose(p, command.index);
                return;
            }
            if (type == "recruit_refresh") { RefreshRecruits(p, command.offerId); return; }
            if (type == "dismiss") { Dismiss(p, command.unitId); return; }
            if (type == "skip")
            {
                if (State.phase != "recruit" || !p.recruitPending) Fail("当前没有待完成的招募");
                FinishRecruit(p); return;
            }
            if (type == "purchase" || type == "refresh")
            {
                if ((State.phase != "prep" && State.phase != "recruit") || p.recruitPending) Fail("请在准备阶段购买");
                if (type == "refresh")
                {
                    if (TradeCatalog.AvailableSkills(p).Length == 0) Fail("所有永久技能均已掌握，无需刷新");
                    if (p.gold < TradeCatalog.RefreshPrice) Fail("金币不足，刷新需要 2 金币");
                    p.gold -= TradeCatalog.RefreshPrice; RefreshMarket(p); p.ready = false;
                }
                else Purchase(p, command);
                return;
            }
            if (type != "place" && type != "ready") Fail("未知操作");
            if (State.phase != "recruit" && State.phase != "prep") Fail("请在准备阶段操作");
            if (p.recruitPending) Fail("请先完成本轮招募");
            if (type == "ready") { p.ready = !p.ready; return; }
            if (command.placementId != null && command.placementId.Length > 64) Fail("布阵请求标识过长");
            UnitState unit = p.units.Find(u => u.id == command.unitId);
            if (unit == null) Fail("只能操作自己拥有的生物");
            if (!Finite(command.x) || !Finite(command.y)) Fail("放置坐标无效");
            Point2 local = WorldToLocal(p.slot, command.x, command.y);
            if (local.x < -185.001f || local.x > 185.001f || local.y < 174.999f || local.y > 355.001f) Fail("请将生物放在自己的布阵区域内");
            unit.x = command.x; unit.y = command.y; unit.placementId = command.placementId; p.ready = false;
        }

        public void Tick(float dt)
        {
            if (!Finite(dt) || dt <= 0) return;
            dt = Math.Min(dt, .1f);
            clock += dt;
            foreach (EffectState e in State.effects) e.age += dt;
            State.effects.RemoveAll(e => e.age > (e.type == "explode" ? 1f : .75f));
            foreach (PlayerState p in State.players)
            {
                if (p.eliminated || p.moveUntil < clock) continue;
                Point2 local = WorldToLocal(p.slot, p.steve.x + p.moveX * dt * 130f, p.steve.y + p.moveY * dt * 130f);
                p.steve = LocalToWorld(p.slot, Clamp(local.x, -220, 220), Clamp(local.y, 165, 400));
            }
            if (State.phase == "lobby" || State.phase == "finished") return;
            State.timeLeft = Math.Max(0, State.timeLeft - dt);
            if (State.phase == "recruit")
            {
                foreach (PlayerState p in Living())
                    if (p.recruitPending && Automated(p) && clock >= p.botAt) AutoRecruit(p);
                if (State.phase == "recruit" && State.timeLeft <= 0)
                    foreach (PlayerState p in Living()) if (p.recruitPending) AutoRecruit(p);
            }
            else if (State.phase == "prep")
            {
                foreach (PlayerState p in Living()) if (Automated(p)) BotPrep(p);
                readyElapsed = Living().All(p => p.ready) ? readyElapsed + dt : 0;
                if (State.timeLeft <= 0 || readyElapsed >= 1f) BeginBattle();
            }
            else if (State.phase == "battle") CombatTick(dt);
            else if (State.phase == "results" && State.timeLeft <= 0) BeginRecruit(false);
        }

        public static Point2 LocalToWorld(int slot, float x, float y)
        {
            int s = ((slot % 4) + 4) % 4;
            if (s == 1) return new Point2(-y, x);
            if (s == 2) return new Point2(-x, -y);
            if (s == 3) return new Point2(y, -x);
            return new Point2(x, y);
        }

        public static Point2 WorldToLocal(int slot, float x, float y) { return LocalToWorld(-slot, x, y); }
        private static bool Finite(float n) { return !Single.IsNaN(n) && !Single.IsInfinity(n); }
        private static float Clamp(float n, float lo, float hi) { return Math.Max(lo, Math.Min(hi, n)); }
        private static float Distance(float ax, float ay, float bx, float by) { float dx = ax - bx, dy = ay - by; return (float)Math.Sqrt(dx * dx + dy * dy); }
        private static float Distance(FighterState a, FighterState b) { return Distance(a.x, a.y, b.x, b.y); }
        private static void Fail(string message) { throw new InvalidOperationException(message); }
        private bool Automated(PlayerState p) { return p.bot || !p.connected; }
        private List<PlayerState> Living() { return State.players.FindAll(p => !p.eliminated); }
        private float Rng() { return (float)random.NextDouble(); }
        private string Id(string prefix) { return prefix + (++nextId); }
        private PlayerState Player(string id)
        {
            PlayerState p = State.players.Find(player => player.id == id);
            if (p == null) Fail("玩家不在房间中");
            return p;
        }
        private static string CleanName(string name)
        {
            string input = (name ?? "").Trim();
            StringBuilder result = new StringBuilder();
            int scalars = 0;
            for (int i = 0; i < input.Length && scalars < 16; i++)
            {
                char c = input[i];
                if (Char.IsControl(c) || c == '<' || c == '>') continue;
                if (Char.IsHighSurrogate(c))
                {
                    if (i + 1 >= input.Length || !Char.IsLowSurrogate(input[i + 1])) continue;
                    result.Append(c); result.Append(input[++i]); scalars++;
                }
                else if (!Char.IsLowSurrogate(c)) { result.Append(c); scalars++; }
            }
            string value = result.ToString().Trim();
            return value.Length == 0 ? "冒险家" : value;
        }
        private static PlayerState MakePlayer(string id, string name, int slot, bool bot)
        {
            return new PlayerState { id = id, name = CleanName(name), slot = slot, bot = bot, connected = !bot, steve = LocalToWorld(slot, 0, 390) };
        }

        private string[] RollShop()
        {
            int tier = Math.Min(6, 2 + (State.round - 1) / 2);
            List<string> available = CreatureCatalog.Kinds.Where(k => CreatureCatalog.Get(k).tier <= tier &&
                (CreatureCatalog.Get(k).tier < 6 || State.round >= 9)).ToList();
            string[] choices = new string[3];
            for (int c = 0; c < 3; c++)
            {
                // Late rounds retain common recruits while progressively opening stronger species.
                int pick = random.Next(available.Count);
                choices[c] = available[pick]; available.RemoveAt(pick);
            }
            return choices;
        }
        private void RefreshMarket(PlayerState p)
        {
            p.market.Clear(); p.shopRefreshLeft = 0;
            var skills = TradeCatalog.AvailableSkills(p).ToList();
            for (int i = 0; i < TradeCatalog.MarketSlots && skills.Count > 0; i++)
            {
                var candidates = i == 0 ? skills.Where(id => TradeCatalog.Get(id).price <= p.gold).ToList() : skills;
                if (candidates.Count == 0) candidates = skills;
                string id = candidates[random.Next(candidates.Count)]; skills.Remove(id);
                TradeDefinition item = TradeCatalog.Get(id);
                p.market.Add(new ShopOffer { id = Id("offer"), kind = item.type, itemId = id, price = item.price });
            }
        }
        private void Purchase(PlayerState p, GameCommand command)
        {
            ShopOffer offer = p.market.Find(o => o.id == command.offerId);
            if (offer == null || offer.sold) Fail("该商品已经售出或商店已刷新");
            TradeDefinition item = TradeCatalog.Get(offer.itemId);
            if (offer.kind != "skill" || item.type != "skill") Fail("商店仅提供永久技能");
            if (p.upgrades.Contains(item.id)) Fail("你已经学习了此技能");
            if (p.gold < item.price) Fail("金币不足");
            p.upgrades.Add(item.id);
            p.gold -= item.price; p.market.Remove(offer); p.ready = false;
        }
        private UnitState AddUnit(PlayerState p, string kind, Point2? position = null)
        {
            Point2 chosen = new Point2();
            if (position.HasValue) chosen = position.Value;
            else
            {
                float bestGap = -1;
                for (int i = 0; i < 12; i++)
                {
                    float y = CreatureCatalog.Get(kind).range > 70 ? 280 + Rng() * 65 : 190 + Rng() * 85;
                    Point2 candidate = LocalToWorld(p.slot, (Rng() - .5f) * 320, y);
                    float gap = Single.PositiveInfinity;
                    foreach (UnitState u in p.units) gap = Math.Min(gap, Distance(u.x, u.y, candidate.x, candidate.y));
                    if (gap > bestGap) { bestGap = gap; chosen = candidate; }
                }
            }
            UnitState unit = new UnitState { id = Id("u"), kind = kind, x = chosen.x, y = chosen.y };
            p.units.Add(unit); return unit;
        }
        private void Start(bool bots)
        {
            while (bots && State.players.Count < 4)
            {
                int slot = Enumerable.Range(0, 4).First(s => State.players.All(p => p.slot != s));
                string id = "bot-" + slot; while (State.players.Any(p => p.id == id)) id += "b";
                State.players.Add(MakePlayer(id, CreatureCatalog.TeamNames[slot] + "守卫", slot, true));
            }
            State.round = 0; State.winnerId = null;
            State.leaderId = null; State.roundEvent = null;
            foreach (PlayerState p in State.players)
            { p.hp = 20; p.eliminated = false; p.gold = 12; p.roundKills = p.totalKills = p.lastIncome = p.killIncome = p.rankIncome = p.baseIncome = p.lastRank = 0; p.bountyKills = p.bountyIncome = p.eventIncome = 0; p.recruitOfferId = null; p.upgrades.Clear(); }
            BeginRecruit(true);
        }
        private void BeginRecruit(bool first)
        {
            State.round++; State.phase = "recruit"; State.timeLeft = RecruitSeconds;
            State.fighters.Clear(); State.effects.Clear(); State.placements.Clear(); defeats.Clear(); readyElapsed = 0;
            State.leaderId = null; AnnounceRoundEvent();
            State.message = "金币招募 · 每轮最多购买一只，也可以保留金币跳过";
            foreach (PlayerState p in State.players)
            {
                p.ready = false; p.moveUntil = 0; p.recruitChoice = null; p.recruitPending = !p.eliminated;
                p.steve = LocalToWorld(p.slot, 0, 390);
                if (p.eliminated) { p.shop = new string[0]; p.recruitOfferId = null; p.market.Clear(); continue; }
                if (first)
                {
                    p.hp = 20; p.units.Clear();
                    AddUnit(p, "zombie", LocalToWorld(p.slot, -24, 210));
                    AddUnit(p, "skeleton", LocalToWorld(p.slot, 24, 305));
                }
                p.shop = RollShop(); p.recruitOfferId = Id("recruit"); RefreshMarket(p); p.botAt = clock + 1 + Rng() * 1.2f;
            }
        }
        private void BeginPrep()
        {
            State.phase = "prep"; State.timeLeft = PrepSeconds; readyElapsed = 0;
            State.message = "招募完成，自由布阵后准备开战";
            foreach (PlayerState p in Living()) p.botAt = clock + .6f + Rng() * .6f;
        }
        private void Choose(PlayerState p, int index)
        {
            string kind = p.shop[index]; int price = CreatureCatalog.Get(kind).price;
            if (p.gold < price) Fail("金币不足，可以跳过招募并保留金币");
            p.gold -= price; AddUnit(p, kind).paidPrice = price; p.recruitChoice = kind;
            FinishRecruit(p);
        }
        private void FinishRecruit(PlayerState p)
        {
            p.recruitPending = false; p.ready = false;
            if (Living().All(player => !player.recruitPending)) BeginPrep();
        }
        private void AutoRecruit(PlayerState p)
        {
            var affordable = Enumerable.Range(0, p.shop.Length).Where(i => CreatureCatalog.Get(p.shop[i]).price <= p.gold).ToList();
            if (affordable.Count == 0) FinishRecruit(p);
            else Choose(p, affordable[random.Next(affordable.Count)]);
        }
        private void BotPrep(PlayerState p)
        {
            if (p.ready || clock < p.botAt) return;
            foreach (ShopOffer offer in p.market.ToArray())
            {
                if (offer.sold || p.gold < offer.price || p.gold < 4) continue;
                if (offer.kind != "skill" || p.upgrades.Contains(offer.itemId)) continue;
                if (!p.units.Any(u => TradeCatalog.Applies(TradeCatalog.Get(offer.itemId), CreatureCatalog.Get(u.kind)))) continue;
                Purchase(p, new GameCommand { offerId = offer.id });
            }
            for (int i = 0; i < p.units.Count; i++)
            {
                UnitState u = p.units[i];
                float x = (float)Math.Sin(i * 2.39996323) * 155;
                float y = (CreatureCatalog.Get(u.kind).range > 70 ? 310 : 215) + (float)Math.Cos(i * 1.61803399) * 25;
                if (u.kind == "spider") { x = (i % 2 == 1 ? 1 : -1) * (125 + (float)Math.Sin(i) * 25); y += 15; }
                Point2 at = LocalToWorld(p.slot, x, y); u.x = at.x; u.y = at.y;
            }
            p.ready = true;
        }

        private FighterState NewFighter(PlayerState p, UnitState u, bool mini = false)
        {
            FighterState f = BuildStats(p, u, mini);
            f.id = Id("f"); f.attackCooldown = Rng() * .45f; f.side = Rng() < .5f ? -1 : 1;
            return f;
        }
        public static FighterState PreviewFighter(PlayerState p, UnitState u) { return BuildStats(p, u, false); }
        private static FighterState BuildStats(PlayerState p, UnitState u, bool mini)
        {
            CreatureDefinition c = CreatureCatalog.Get(u.kind);
            FighterState f = new FighterState { id = u.id, unitId = u.id, ownerId = p.id, slot = p.slot, kind = u.kind,
                x = u.x, y = u.y, hp = mini ? 20 : c.hp, maxHp = mini ? 20 : c.hp, attack = mini ? 4 : c.attack,
                attackSpeed = mini ? 1.1f : c.attackSpeed, range = mini ? 22 : c.range, speed = mini ? 48 : c.speed,
                scale = mini ? .62f : 1, mini = mini, summoned = mini,
                radius = (c.tier >= 6 ? 28 : c.model == "golem" || u.kind == "golem" ? 20 : c.model == "spider" ? 13 : 15) * (mini ? .62f : 1) };
            List<string> skills = new List<string>(c.skills);
            foreach (string id in p.upgrades)
            {
                TradeDefinition item = TradeCatalog.Get(id);
                if (!TradeCatalog.Applies(item, c)) continue;
                f.maxHp += item.hp; f.attack += item.attack;
                f.attackSpeed *= 1 + item.attackSpeed; f.speed *= 1 + item.speed; f.range += item.range; f.armor += item.armor;
                if (!string.IsNullOrEmpty(item.grantedSkill) && !skills.Contains(item.grantedSkill)) skills.Add(item.grantedSkill);
            }
            f.armor = Math.Min(.7f, f.armor); f.hp = f.maxHp; f.skills = skills.ToArray();
            CreatureAbilities.OnSpawn(f);
            return f;
        }
        private void BeginBattle()
        {
            State.phase = "battle"; State.timeLeft = BattleSeconds; State.fighters.Clear(); defeats.Clear(); participants.Clear();
            State.message = "四方混战，最后存活的军团获胜";
            foreach (PlayerState p in Living())
            {
                participants.Add(p.id);
                p.roundKills = p.killIncome = p.bountyKills = p.bountyIncome = p.eventIncome = 0;
                foreach (UnitState u in p.units) State.fighters.Add(NewFighter(p, u));
            }
            State.leaderId = SelectBattleLeader();
            SpawnRoundEvent();
        }
        private void Effect(string type, float x, float y, int slot, float value = 0, float tx = 0, float ty = 0)
        {
            State.effects.Add(new EffectState { id = Id("e"), type = type, x = x, y = y, slot = slot, value = value, targetX = tx, targetY = ty });
            if (State.effects.Count > 600) State.effects.RemoveRange(0, State.effects.Count - 600);
        }
        private void Kill(FighterState f)
        {
            if (f.dead) return;
            f.dead = true; f.hp = 0; Effect("death", f.x, f.y, f.slot);
            if (!f.neutral) CreatureAbilities.OnDeath(f, State.fighters, Damage, AbilityEffect, Summon);
        }
        private void Summon(FighterState source, string kind, int count)
        {
            if (source.summoned || source.neutral) return;
            PlayerState p = Player(source.ownerId);
            for (int i = 0; i < Math.Min(3, count); i++)
            {
                bool mini = kind == source.kind && (kind == "slime" || kind == "magma_cube");
                var unit = new UnitState { id = source.unitId, kind = kind, x = Clamp(source.x + (i - (count - 1) * .5f) * 24, -410, 410), y = source.y };
                FighterState child = NewFighter(p, unit, mini); child.summoned = true;
                if (!mini) { child.scale *= .8f; child.hp = child.maxHp *= .65f; }
                State.fighters.Add(child); Effect("spawn", child.x, child.y, child.slot);
            }
        }
        private void AbilityEffect(string type, FighterState source, FighterState target, float value)
        { Effect(type, source.x, source.y, source.slot, value, target == null ? source.x : target.x, target == null ? source.y : target.y); }
        private void Damage(FighterState target, float amount, FighterState attacker)
        {
            if (target.dead || damageDepth >= 8 || !Finite(amount) || amount <= 0) return;
            damageDepth++;
            try
            {
                amount = CreatureAbilities.BeforeDamage(target, amount, attacker);
                if (amount <= 0) return;
                float actual = Math.Min(target.hp, amount);
                target.hp = Math.Max(0, target.hp - amount);
                if (attacker != null && attacker.ownerId != target.ownerId) target.lastAttackerId = attacker.ownerId;
                Effect("hit", target.x, target.y, attacker == null ? target.slot : attacker.slot, (float)Math.Floor(actual + .5f));
                if (target.hp <= 0)
                {
                    RewardDefeat(target, attacker);
                    Kill(target);
                }
                if (damageDepth == 1) CreatureAbilities.AfterDamage(target, actual, attacker, Damage, AbilityEffect);
            }
            finally { damageDepth--; }
        }
        private FighterState ChooseTarget(FighterState f)
        {
            FighterState chosen = null; float best = Single.PositiveInfinity;
            string bossTarget = f.neutral ? BossTargetOwner() : null;
            foreach (FighterState other in State.fighters)
            {
                if (other.dead || other.ownerId == f.ownerId) continue;
                if (f.neutral && (other.neutral || other.ownerId != bossTarget)) continue;
                float score = Distance(f, other);
                if (f.kind == "spider" && other.kind == "skeleton") score *= .64f;
                if (score < best) { best = score; chosen = other; }
            }
            f.targetId = chosen == null ? null : chosen.id; f.think = .35f + Rng() * .2f; return chosen;
        }
        private static long GridKey(int x, int y) { return ((long)x << 32) ^ (uint)y; }
        private static Dictionary<long, List<FighterState>> SpatialGrid(List<FighterState> fighters)
        {
            Dictionary<long, List<FighterState>> grid = new Dictionary<long, List<FighterState>>();
            foreach (FighterState f in fighters)
            {
                long key = GridKey((int)Math.Floor(f.x / 42), (int)Math.Floor(f.y / 42));
                List<FighterState> cell;
                if (!grid.TryGetValue(key, out cell)) { cell = new List<FighterState>(); grid.Add(key, cell); }
                cell.Add(f);
            }
            return grid;
        }
        private static Point2 Separation(FighterState f, Dictionary<long, List<FighterState>> grid)
        {
            int cx = (int)Math.Floor(f.x / 42), cy = (int)Math.Floor(f.y / 42); float x = 0, y = 0;
            for (int ix = cx - 1; ix <= cx + 1; ix++) for (int iy = cy - 1; iy <= cy + 1; iy++)
            {
                List<FighterState> cell;
                if (!grid.TryGetValue(GridKey(ix, iy), out cell)) continue;
                foreach (FighterState other in cell)
                {
                    if (other == f || other.dead) continue;
                    float dx = f.x - other.x, dy = f.y - other.y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy), minimum = f.radius + other.radius;
                    if (dist >= minimum) continue;
                    if (dist < .01f) { dx = f.side; dy = f.slot % 2 == 1 ? .7f : -.7f; dist = (float)Math.Sqrt(dx * dx + dy * dy); }
                    float push = (1 - dist / minimum) * (f.ownerId == other.ownerId ? 48 : 95);
                    x += dx / dist * push; y += dy / dist * push;
                }
            }
            float magnitude = (float)Math.Sqrt(x * x + y * y);
            if (magnitude > 65) { x *= 65 / magnitude; y *= 65 / magnitude; }
            return new Point2(x, y);
        }
        private void Attack(FighterState f, FighterState target)
        {
            f.attackCooldown = 1 / f.attackSpeed; f.attackTime = .24f;
            CreatureAbilities.Attack(f, target, State.fighters, Damage, AbilityEffect, Summon);
        }
        private void CombatTick(float dt)
        {
            List<FighterState> current = State.fighters.FindAll(f => !f.dead);
            Dictionary<string, FighterState> byId = current.ToDictionary(f => f.id);
            Dictionary<long, List<FighterState>> grid = SpatialGrid(current);
            if (((int)Math.Floor(clock / .05f)) % 2 == 1) current.Reverse();
            foreach (FighterState f in current)
            {
                if (f.dead) continue;
                f.attackTime = Math.Max(0, f.attackTime - dt); f.attackCooldown = Math.Max(0, f.attackCooldown - dt); f.think -= dt;
                // The gold resource has no spawn bonuses or attacks, but existing poison/burn still expire and deal damage.
                CreatureAbilities.Tick(f, State.fighters, dt, Damage, AbilityEffect);
                if (f.dead || f.stunTime > 0) continue;
                if (f.neutral && f.attack <= 0) continue;
                FighterState target = null;
                if (f.targetId != null) byId.TryGetValue(f.targetId, out target);
                if (target == null || target.dead || f.think <= 0 || f.neutral) target = ChooseTarget(f);
                if (target == null) continue;
                float dist = Distance(f, target), range = f.range + (f.range > 70 ? 0 : target.radius * .55f);
                if (dist <= range && f.attackCooldown <= 0) Attack(f, target);
                if (f.dead) continue;
                Point2 repel = Separation(f, grid); float vx = repel.x, vy = repel.y;
                if (dist > range * .95f)
                {
                    float dx = (target.x - f.x) / Math.Max(1, dist), dy = (target.y - f.y) / Math.Max(1, dist);
                    float flank = f.kind == "spider" && dist > 75 ? f.side * .55f : 0;
                    vx += (dx - dy * flank) * f.speed * f.slowMultiplier; vy += (dy + dx * flank) * f.speed * f.slowMultiplier;
                }
                float velocity = (float)Math.Sqrt(vx * vx + vy * vy), limit = f.speed * 1.3f * f.slowMultiplier;
                if (velocity > limit) { vx *= limit / velocity; vy *= limit / velocity; }
                f.x = Clamp(f.x + vx * dt, -410, 410); f.y = Clamp(f.y + vy * dt, -410, 410);
            }
            UpdateRoundEvent();
            HashSet<string> alive = new HashSet<string>(State.fighters.Where(f => !f.dead && !f.neutral && participants.Contains(f.ownerId)).Select(f => f.ownerId));
            HashSet<string> already = new HashSet<string>(defeats.SelectMany(g => g.ids));
            List<string> newlyDefeated = participants.Where(id => !alive.Contains(id) && !already.Contains(id)).ToList();
            if (newlyDefeated.Count > 0) defeats.Add(new DefeatGroup { ids = newlyDefeated });
            if (alive.Count <= 1 || State.timeLeft <= 0) ResolveBattle();
        }
        private void ResolveBattle()
        {
            Dictionary<string, float> totals = new Dictionary<string, float>();
            foreach (FighterState f in State.fighters)
            {
                if (f.dead || f.neutral || !participants.Contains(f.ownerId)) continue;
                if (!totals.ContainsKey(f.ownerId)) totals.Add(f.ownerId, 0);
                totals[f.ownerId] += f.hp;
            }
            List<DefeatGroup> groups = new List<DefeatGroup>();
            foreach (KeyValuePair<string, float> entry in totals.OrderByDescending(pair => pair.Value))
            {
                DefeatGroup previous = groups.Count > 0 ? groups[groups.Count - 1] : null;
                if (previous != null && Math.Abs(previous.score - entry.Value) < .001f) previous.ids.Add(entry.Key);
                else groups.Add(new DefeatGroup { ids = new List<string> { entry.Key }, score = entry.Value });
            }
            for (int i = defeats.Count - 1; i >= 0; i--) groups.Add(new DefeatGroup { ids = new List<string>(defeats[i].ids), score = -1 });
            HashSet<string> known = new HashSet<string>(groups.SelectMany(g => g.ids));
            List<string> missing = participants.FindAll(id => !known.Contains(id));
            if (missing.Count > 0) groups.Add(new DefeatGroup { ids = missing, score = -1 });
            State.placements.Clear(); int rank = 1;
            int lateDamage = State.round > 8 ? 1 + (State.round - 9) / 3 : 0;
            foreach (DefeatGroup group in groups)
            {
                int baseDamage = rank == 1 ? (group.ids.Count > 1 ? 1 : 0) : rank - 1;
                foreach (string id in group.ids)
                {
                    PlayerState p = Player(id); int amount = baseDamage == 0 ? 0 : baseDamage + lateDamage;
                    p.hp = Math.Max(0, p.hp - amount); p.eliminated = p.hp == 0; p.ready = false;
                    p.lastRank = rank; p.baseIncome = 6; p.killIncome = Math.Min(KillIncomeCap, p.roundKills); p.rankIncome = RankReward(rank);
                    // Kill gold was credited at the lethal hit; the summary still describes the whole round.
                    p.lastIncome = p.baseIncome + p.killIncome + p.rankIncome + p.bountyIncome + p.eventIncome; p.gold += p.baseIncome + p.rankIncome;
                    State.placements.Add(new PlacementState { playerId = id, rank = rank, damage = amount, kills = p.roundKills,
                        income = p.lastIncome, baseIncome = p.baseIncome, killIncome = p.killIncome, rankIncome = p.rankIncome,
                        bountyKills = p.bountyKills, bountyIncome = p.bountyIncome, eventIncome = p.eventIncome });
                }
                rank += group.ids.Count;
            }
            State.phase = "results";
            if (State.roundEvent != null) State.roundEvent.active = false;
            State.timeLeft = Math.Max(ResultsSeconds, CommanderRitualTimeline.DurationFor(State) + CommanderRitualTimeline.NetworkMarginSeconds);
            if (groups.Count > 0)
                State.message = groups[0].ids.Count > 1 ? "本轮并列，各扣 " + State.placements[0].damage + " 点生命" : Player(groups[0].ids[0]).name + "赢下本轮";
            List<PlayerState> remaining = Living();
            if (remaining.Count <= 1)
            {
                State.phase = "finished"; State.winnerId = remaining.Count == 1 ? remaining[0].id : null;
                State.message = remaining.Count == 1 ? remaining[0].name + "成为斗兽场冠军" : "所有军团同时倒下，本场平局";
            }
        }

        public GameState SnapshotFor(string playerId)
        {
            GameState copy = new GameState { hostId = State.hostId, phase = State.phase, winnerId = State.winnerId, message = State.message, round = State.round, leaderId = State.leaderId,
                roundEvent = CopyRoundEvent(State.roundEvent),
                timeLeft = State.phase == "prep" && readyElapsed > 0 ? Math.Min(State.timeLeft, Math.Max(0, 1 - readyElapsed)) : State.timeLeft };
            foreach (PlayerState p in State.players)
            {
                PlayerState player = new PlayerState { id = p.id, name = p.name, slot = p.slot, hp = p.hp, ready = p.ready, bot = p.bot, connected = p.connected,
                    eliminated = p.eliminated, recruitPending = p.recruitPending, recruitChoice = p.recruitChoice, steve = p.steve,
                    gold = p.gold, roundKills = p.roundKills, totalKills = p.totalKills, lastIncome = p.lastIncome, killIncome = p.killIncome,
                    rankIncome = p.rankIncome, baseIncome = p.baseIncome, lastRank = p.lastRank, shopRefreshLeft = p.id == playerId ? p.shopRefreshLeft : 0,
                    bountyKills = p.bountyKills, bountyIncome = p.bountyIncome, eventIncome = p.eventIncome,
                    recruitOfferId = p.id == playerId ? p.recruitOfferId : null,
                    shop = p.id == playerId ? (string[])p.shop.Clone() : null };
                player.upgrades.AddRange(p.upgrades);
                if (p.id == playerId) foreach (ShopOffer offer in p.market) player.market.Add(new ShopOffer { id = offer.id, kind = offer.kind, itemId = offer.itemId, price = offer.price, sold = offer.sold });
                foreach (UnitState u in p.units) player.units.Add(new UnitState { id = u.id, kind = u.kind, x = u.x, y = u.y, placementId = u.placementId, paidPrice = u.paidPrice });
                copy.players.Add(player);
            }
            foreach (FighterState f in State.fighters) copy.fighters.Add(new FighterState { id = f.id, unitId = f.unitId, ownerId = f.ownerId, kind = f.kind, slot = f.slot,
                x = f.x, y = f.y, hp = f.hp, maxHp = f.maxHp, scale = f.scale, attackTime = f.attackTime, dead = f.dead, neutral = f.neutral,
                attack = f.attack, attackSpeed = f.attackSpeed, range = f.range, speed = f.speed,
                skills = (string[])f.skills.Clone(), armor = f.armor, shield = f.shield,
                poisonTime = f.poisonTime, burnTime = f.burnTime, slowTime = f.slowTime, stunTime = f.stunTime, summoned = f.summoned });
            foreach (EffectState e in State.effects) copy.effects.Add(new EffectState { id = e.id, type = e.type, x = e.x, y = e.y, age = e.age,
                targetX = e.targetX, targetY = e.targetY, slot = e.slot, value = e.value });
            foreach (PlacementState p in State.placements) copy.placements.Add(new PlacementState { playerId = p.playerId, rank = p.rank, damage = p.damage,
                kills = p.kills, income = p.income, baseIncome = p.baseIncome, killIncome = p.killIncome, rankIncome = p.rankIncome,
                bountyKills = p.bountyKills, bountyIncome = p.bountyIncome, eventIncome = p.eventIncome });
            return copy;
        }
    }
}
