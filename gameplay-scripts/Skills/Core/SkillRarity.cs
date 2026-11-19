using UnityEngine;

public enum SkillRarity : byte
{
    Common    = 0,
    Rare      = 1,
    Epic      = 2,
    Legendary = 3,
}

public static class SkillRarityExtensions
{
    public const int Count = 4;

    /// <summary>1..100 weight buckets per design: Common 60 / Rare 25 / Epic 12 / Legendary 3.</summary>
    public static SkillRarity Roll(System.Random rng)
    {
        int r = rng.Next(1, 101);
        if (r <= 60) return SkillRarity.Common;
        if (r <= 85) return SkillRarity.Rare;
        if (r <= 97) return SkillRarity.Epic;
        return SkillRarity.Legendary;
    }

    /// <summary>Legendary→Epic→Rare→Common→(reroll all).</summary>
    public static SkillRarity StepDown(SkillRarity rarity) => rarity switch
    {
        SkillRarity.Legendary => SkillRarity.Epic,
        SkillRarity.Epic      => SkillRarity.Rare,
        SkillRarity.Rare      => SkillRarity.Common,
        _                     => SkillRarity.Common,
    };

    public static string ShortLabel(SkillRarity rarity) => rarity switch
    {
        SkillRarity.Common    => "Common",
        SkillRarity.Rare      => "Rare",
        SkillRarity.Epic      => "Epic",
        SkillRarity.Legendary => "Legendary",
        _                     => "Common",
    };

    public static Color Tint(SkillRarity rarity) => rarity switch
    {
        SkillRarity.Common    => new Color(0.69f, 0.69f, 0.69f, 1f),
        SkillRarity.Rare      => new Color(0.31f, 0.76f, 0.97f, 1f),
        SkillRarity.Epic      => new Color(0.73f, 0.41f, 0.78f, 1f),
        SkillRarity.Legendary => new Color(1.00f, 0.70f, 0.00f, 1f),
        _                     => Color.white,
    };

    /// <summary>Hex (without #) for TMP rich-text usage.</summary>
    public static string TintHex(SkillRarity rarity) => rarity switch
    {
        SkillRarity.Common    => "B0B0B0",
        SkillRarity.Rare      => "4FC3F7",
        SkillRarity.Epic      => "BA68C8",
        SkillRarity.Legendary => "FFB300",
        _                     => "FFFFFF",
    };
}
