/// <summary>
/// A level-up choice that adds one stack to a specific (Skill, Tier) slot.
/// Pulls metadata from SkillManager so card text always reflects current state.
/// </summary>
public class SkillTierCard : IPlayerMutation
{
    public SkillId Id   { get; }
    public int     Tier { get; }
    public int     StacksBefore { get; }

    public SkillTierCard(SkillId id, int tier, int stacksBefore)
    {
        Id           = id;
        Tier         = tier;
        StacksBefore = stacksBefore;
    }

    public int StacksAfter => StacksBefore + 1;

    public string Name
    {
        get
        {
            var mgr = SkillManager.Instance;
            string name = mgr != null ? mgr.GetDisplayName(Id) : Id.ToString();
            string tierTag = Tier switch { 1 => "T1", 2 => "T2", 3 => "T3", _ => "T?" };
            return StacksBefore == 0 ? $"{name} — {tierTag}" : $"{name} — {tierTag} ×{StacksAfter}";
        }
    }

    public string Description
    {
        get
        {
            var mgr = SkillManager.Instance;
            return mgr != null ? mgr.GetTierDescription(Id, Tier) : "";
        }
    }

    public SkillCategory Category
        => SkillManager.Instance != null ? SkillManager.Instance.GetCategory(Id) : SkillCategory.Stat;

    public SkillRarity Rarity
        => SkillManager.Instance != null ? SkillManager.Instance.GetRarity(Id) : SkillRarity.Common;

    public void Apply(PlayerStats stats)
    {
        SkillManager.Instance?.AddStack(Id, Tier);
    }
}
