using System.Collections.Generic;

/// <summary>
/// Per-tier stack inventory: each (SkillId, tier) pair has an independent stack count.
/// Tier 1 must be picked at least once before Tier 2 cards offer; Tier 2 ≥ 1 before Tier 3.
/// </summary>
public class SkillInventory
{
    readonly Dictionary<SkillId, int> _t1 = new Dictionary<SkillId, int>(12);
    readonly Dictionary<SkillId, int> _t2 = new Dictionary<SkillId, int>(12);
    readonly Dictionary<SkillId, int> _t3 = new Dictionary<SkillId, int>(12);

    // Sum of all tier stacks per skill — used as the "Lv" label and HUD index.
    readonly Dictionary<SkillId, int> _total = new Dictionary<SkillId, int>(12);

    public int GetTierStacks(SkillId id, int tier)
    {
        var bag = BagFor(tier);
        if (bag == null) return 0;
        return bag.TryGetValue(id, out int v) ? v : 0;
    }

    public int AddTierStack(SkillId id, int tier)
    {
        var bag = BagFor(tier);
        if (bag == null) return 0;
        bag.TryGetValue(id, out int cur);
        cur++;
        bag[id] = cur;

        _total.TryGetValue(id, out int total);
        _total[id] = total + 1;
        return cur;
    }

    public int GetTotalStacks(SkillId id)
        => _total.TryGetValue(id, out int v) ? v : 0;

    public int GetLevel(SkillId id) => GetTotalStacks(id);

    public bool HasSkill(SkillId id) => GetTierStacks(id, 1) > 0;

    public IReadOnlyDictionary<SkillId, int> All => _total;

    Dictionary<SkillId, int> BagFor(int tier) => tier switch
    {
        1 => _t1,
        2 => _t2,
        3 => _t3,
        _ => null,
    };
}
