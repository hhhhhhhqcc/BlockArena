using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockArena.Core
{
    public sealed partial class ArenaGame
    {
        public const int RecruitRefreshPrice = 2, KillIncomeCap = 5, BountyIncomeCap = 3;

        public static int DismissRefund(UnitState unit)
        {
            return unit == null ? 0 : Math.Max(0, unit.paidPrice) / 2;
        }

        public static float ArmyPower(PlayerState player)
        {
            if (player == null) return 0;
            double power = 0;
            foreach (UnitState unit in player.units)
            {
                FighterState fighter = BuildStats(player, unit, false);
                power += fighter.maxHp + fighter.shield + fighter.attack * fighter.attackSpeed * 10;
            }
            return (float)power;
        }

        public static int RankReward(int rank) { return rank <= 1 ? 4 : rank == 2 ? 3 : 2; }

        void ValidateRecruitOffer(PlayerState player, string offerId)
        {
            if (!string.IsNullOrEmpty(offerId) && offerId != player.recruitOfferId)
                Fail("招募候选已经更新，请使用当前候选");
        }

        void RefreshRecruits(PlayerState player, string offerId)
        {
            if (State.phase != "recruit" || !player.recruitPending) Fail("当前没有待完成的招募");
            ValidateRecruitOffer(player, offerId);
            if (player.gold < RecruitRefreshPrice) Fail("金币不足，重招需要 2 金币");
            string[] replacement = RollShop();
            // A successful refresh must change the set, not merely shuffle its order.
            if (new HashSet<string>(replacement).SetEquals(player.shop))
            {
                int tier = Math.Min(6, 2 + (State.round - 1) / 2);
                var alternatives = CreatureCatalog.Kinds.Where(kind => !replacement.Contains(kind) &&
                    CreatureCatalog.Get(kind).tier <= tier &&
                    (CreatureCatalog.Get(kind).tier < 6 || State.round >= 9)).ToList();
                if (alternatives.Count == 0) Fail("当前没有其他可用的招募候选");
                replacement[random.Next(replacement.Length)] = alternatives[random.Next(alternatives.Count)];
            }
            player.gold -= RecruitRefreshPrice;
            player.shop = replacement;
            player.recruitOfferId = Id("recruit");
            player.ready = false;
        }

        void Dismiss(PlayerState player, string unitId)
        {
            if (State.phase != "recruit" && State.phase != "prep" && State.phase!="tactics") Fail("只能在准备或战术阶段遣散");
            if(State.phase=="tactics"&&player.ready)Fail("请先取消准备");
            UnitState unit = player.units.Find(candidate => candidate.id == unitId);
            if (unit == null) Fail("只能遣散自己拥有的生物");
            if (player.units.Count <= 1) Fail("至少保留一只生物");
            int refund = DismissRefund(unit);
            player.units.Remove(unit);
            foreach(var plan in player.tactics)plan.unitIds=plan.unitIds.Where(id=>id!=unitId).ToArray();
            player.tactics.RemoveAll(plan=>plan.unitIds.Length==0);
            player.gold += refund;
            player.ready = false;
            // Pending recruitment stays pending: a dismissal can fund its purchase.
        }

        string SelectBattleLeader()
        {
            PlayerState best = null;
            float bestPower = 0;
            bool tied = false;
            foreach (PlayerState player in Living())
            {
                float power = ArmyPower(player);
                int comparison = best == null ? 1 : player.hp.CompareTo(best.hp);
                if (comparison == 0) comparison = best.lastRank.CompareTo(player.lastRank);
                if (comparison == 0) comparison = power.CompareTo(bestPower);
                if (comparison > 0) { best = player; bestPower = power; tied = false; }
                else if (comparison == 0) tied = true;
            }
            return best == null || tied ? null : best.id;
        }

        void RewardDefeat(FighterState defeated, FighterState attacker)
        {
            if (attacker == null || attacker.neutral || defeated.ownerId == attacker.ownerId) return;
            PlayerState killer = State.players.Find(player => player.id == attacker.ownerId);
            if (killer == null || killer.eliminated || !participants.Contains(killer.id)) return;
            if (defeated.neutral)
            {
                // Resources are fixed map objectives; retired central event actors grant no income.
                return;
            }
            if (defeated.summoned || !participants.Contains(defeated.ownerId)) return;
            killer.roundKills++; killer.totalKills++;
            if (killer.killIncome < KillIncomeCap) { killer.killIncome++; killer.gold++; }
            if (!string.IsNullOrEmpty(State.leaderId) && defeated.ownerId == State.leaderId && killer.id != State.leaderId)
            {
                killer.bountyKills++;
                int bounty = Math.Min(BountyIncomeCap, killer.bountyKills / 3);
                int earned = bounty - killer.bountyIncome;
                if (earned > 0) { killer.bountyIncome += earned; killer.gold += earned; }
            }
        }
    }
}
