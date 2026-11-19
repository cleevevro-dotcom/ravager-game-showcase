using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per level-up: rolls N distinct (SkillId, Tier) cards.
/// - Same skill across different tiers in one screen = OK.
/// - Same (skill, tier) twice in one screen = NOT OK.
/// - Tier gating: T1 always; T2 if T1 stacks ≥ 1; T3 if T2 stacks ≥ 1.
/// - T1 weight stays roughly flat over time so unlocks keep appearing randomly.
/// - T2/T3 weight ramps up with WaveManager.ElapsedTime so deeper runs naturally
///   surface evolution cards instead of letting the player drown in T1 stacks.
/// - Stack bias (+15% per stack capped at +200%) only applies to T2/T3 cards so
///   already-invested skills get more upgrade offers — T1 stays uniform across skills.
/// </summary>
public static class MutationLibrary
{
    static readonly SkillId[] AllSkills =
    {
        SkillId.Fire, SkillId.Freeze, SkillId.Lightning, SkillId.Plague,
        SkillId.Tentacle, SkillId.Axe, SkillId.Sword,
        SkillId.Physical, SkillId.Agility, SkillId.Health, SkillId.Fear,
        SkillId.Vision,
        // New beast skills (Bloodlust, Earthshatter, Berserk, SpiritOrb, WindCutter,
        // IronSkin, ChaosSurge, SoulHarvest) — same tier-gating + time-scaling rules apply.
        SkillId.Bloodlust, SkillId.Earthshatter, SkillId.Berserk, SkillId.SpiritOrb,
        SkillId.WindCutter, SkillId.IronSkin, SkillId.ChaosSurge, SkillId.SoulHarvest,
    };

    const float StackBiasPerStack = 0.15f;
    const float MaxStackBias      = 2.0f;

    static readonly System.Random Rng = new System.Random();

    // Reused scratch lists — avoid GC during gameplay.
    static readonly List<SkillTierCard> _candidateScratch = new List<SkillTierCard>(AllSkills.Length * 3);
    static readonly List<float>         _weightScratch    = new List<float>(AllSkills.Length * 3);

    /// <summary>Return up to 'count' distinct cards. Distinctness key = (SkillId, Tier).</summary>
    public static List<IPlayerMutation> GetRandom(int count, SkillInventory inv)
    {
        var result = new List<IPlayerMutation>(count);
        var mgr = SkillManager.Instance;
        if (inv == null || mgr == null) return result;

        TierTimeWeights w = ComputeTierWeightsForElapsed(
            WaveManager.Instance != null ? WaveManager.Instance.ElapsedTime : 0f);

        BuildCandidates(mgr, inv, w);

        for (int pick = 0; pick < count && _candidateScratch.Count > 0; pick++)
        {
            int idx = WeightedPickIndex(_weightScratch);
            if (idx < 0) break;
            result.Add(_candidateScratch[idx]);
            _candidateScratch.RemoveAt(idx);
            _weightScratch.RemoveAt(idx);
        }

        _candidateScratch.Clear();
        _weightScratch.Clear();
        return result;
    }

    struct TierTimeWeights
    {
        public float t1;
        public float t2;
        public float t3;
    }

    /// <summary>
    /// Maps elapsed seconds → per-tier multiplier. Curve targets:
    ///   t = 0:00  →  T1=1.00  T2=0.20  T3=0.05   (T1 dominates the early game)
    ///   t = 2:00  →  T1=0.94  T2=1.00  T3=0.10   (T2 starts mixing in)
    ///   t = 5:00  →  T1=0.85  T2=2.20  T3=1.00   (T2 leads at boss time)
    ///   t = 10:00 →  T1=0.70  T2=2.50  T3=2.00   (T3 viable late, T1 still random-pickable)
    /// </summary>
    static TierTimeWeights ComputeTierWeightsForElapsed(float elapsedSeconds)
    {
        float minutes = Mathf.Max(0f, elapsedSeconds) / 60f;
        TierTimeWeights w;
        // T1 tapers slightly so high tiers can dominate eventually, but never disappears.
        w.t1 = Mathf.Lerp(1.0f, 0.7f, Mathf.Clamp01(minutes / 10f));
        // T2 ramps quickly between minute 0 and minute 5, keeps climbing modestly to a cap.
        w.t2 = Mathf.Clamp(0.20f + 0.40f * minutes, 0.20f, 2.5f);
        // T3 has a 2-minute warmup so the first level-ups are pure unlock-fests, then ramps.
        w.t3 = minutes < 2f
            ? 0.05f
            : Mathf.Clamp(0.10f + 0.30f * (minutes - 2f), 0.10f, 2.0f);
        return w;
    }

    static void BuildCandidates(SkillManager mgr, SkillInventory inv, TierTimeWeights w)
    {
        _candidateScratch.Clear();
        _weightScratch.Clear();

        for (int i = 0; i < AllSkills.Length; i++)
        {
            SkillId id = AllSkills[i];
            int t1 = inv.GetTierStacks(id, 1);
            int t2 = inv.GetTierStacks(id, 2);
            int t3 = inv.GetTierStacks(id, 3);

            // No rarity weighting — skill selection is purely tier + elapsed-time + stack bias.
            // Stack bias rewards investment on T2/T3 only so T1 unlocks stay uniform across skills.
            int totalStacks = t1 + t2 + t3;
            float upgradeBias = 1f + Mathf.Min(MaxStackBias, totalStacks * StackBiasPerStack);

            // T1 — always available, uniform weight (no stack bias) so unlocks stay "random".
            AddCard(id, 1, t1, w.t1);
            // T2 — gated by T1 ≥ 1, time-scaled, biased toward stacked skills.
            if (t1 >= 1) AddCard(id, 2, t2, w.t2 * upgradeBias);
            // T3 — gated by T2 ≥ 1, time-scaled, biased toward stacked skills.
            if (t2 >= 1) AddCard(id, 3, t3, w.t3 * upgradeBias);
        }
        // mgr is intentionally unused now that rarity weighting is gone; keep param for future hooks.
        _ = mgr;
    }

    static void AddCard(SkillId id, int tier, int currentStacks, float weight)
    {
        _candidateScratch.Add(new SkillTierCard(id, tier, currentStacks));
        _weightScratch.Add(weight);
    }

    static int WeightedPickIndex(List<float> weights)
    {
        float total = 0f;
        for (int i = 0; i < weights.Count; i++) total += weights[i];
        if (total <= 0f) return weights.Count > 0 ? Rng.Next(weights.Count) : -1;

        double roll = Rng.NextDouble() * total;
        double acc = 0d;
        for (int i = 0; i < weights.Count; i++)
        {
            acc += weights[i];
            if (roll <= acc) return i;
        }
        return weights.Count - 1;
    }
}
