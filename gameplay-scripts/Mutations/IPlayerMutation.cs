public interface IPlayerMutation
{
    string        Name        { get; }
    string        Description { get; }
    SkillCategory Category    { get; }
    SkillRarity   Rarity      { get; }
    int           Tier        { get; }
    int           StacksAfter { get; }
    void Apply(PlayerStats stats);
}
