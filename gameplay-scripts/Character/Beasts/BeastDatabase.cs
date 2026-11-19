/// <summary>
/// Static registry of beast presets. Single source of truth for tuning per-beast modifiers
/// and the display copy the Mutation panel reads for locked-beast browsing.
/// </summary>
public static class BeastDatabase
{
    static readonly BeastConfig[] _configs =
    {
        // Goblin — baseline. Normal claw attack, no stat modifiers.
        new BeastConfig(BeastType.Goblin,   "Goblin",   1.00f, 1.00f, 1.00f, 1.00f, 0f,
            "The balanced default. Quick claw swings and average stats across the board.",
            "Claw — short range melee"),
        // Skeleton archer — long-range attack, fragile (decreased HP).
        new BeastConfig(BeastType.Skeleton, "Skeleton", 0.70f, 0.95f, 1.00f, 1.00f, 0f,
            "Lightweight archer. Fires arrows from a distance but goes down faster than the others.",
            "Bow — long range projectile"),
        // Slime — mid-range AoE spit, slightly less damage but moves faster.
        new BeastConfig(BeastType.Slime,    "Slime",    1.00f, 0.85f, 1.15f, 1.00f, 0f,
            "Quick AoE bruiser. Lower damage but faster movement and area splashes.",
            "Spit — mid range AoE"),
        // Golem — slow heavy hitter, high damage, slightly larger.
        new BeastConfig(BeastType.Golem,    "Golem",    1.30f, 1.50f, 0.85f, 1.20f, 0f,
            "Massive front-line bruiser. Slow swings hit very hard and cleave a wide cone.",
            "Smash — heavy short-range AoE"),
        // Zombie — short range melee with extra lifesteal on kill, lower HP than Goblin
        // (but higher than Skeleton) to balance the sustain.
        new BeastConfig(BeastType.Zombie,   "Zombie",   0.85f, 1.00f, 0.95f, 1.00f, 4f,
            "Sustain bruiser. Lower max HP but every kill heals — chains stay alive forever.",
            "Bite — short range melee with lifesteal"),
    };

    public static BeastConfig Get(BeastType type)
    {
        int idx = (int)type;
        if (idx < 0 || idx >= _configs.Length) return _configs[0];
        return _configs[idx];
    }

    public static int Count => _configs.Length;

    /// <summary>Iterate the canonical beast order — used by the Mutation panel carousel.</summary>
    public static BeastConfig At(int index)
    {
        if (index < 0 || index >= _configs.Length) return _configs[0];
        return _configs[index];
    }
}
