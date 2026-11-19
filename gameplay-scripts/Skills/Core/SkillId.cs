public enum SkillId : byte
{
    // Elemental
    Fire         = 0,
    Freeze       = 1,
    Lightning    = 2,
    Plague       = 10,
    Earthshatter = 12,  // NEW (linked, on-attack AoE crack)
    WindCutter   = 13,  // NEW (linked, on-attack wind blade)

    // Weapon
    Tentacle     = 3,
    Axe          = 4,
    Sword        = 5,
    SpiritOrb    = 14,  // NEW (passive orbiting orb that strikes outward)

    // Stat
    Physical     = 6,
    Agility      = 7,
    Health       = 8,
    Berserk      = 15,  // NEW (HP-scaling damage, AS, emergency invuln)
    IronSkin     = 16,  // NEW (damage reduction, reflect, periodic shield)

    // Bonus
    Fear         = 9,
    Vision       = 11,
    Bloodlust    = 17,  // NEW (attack-speed-on-hit, lifesteal, kill frenzy)
    ChaosSurge   = 18,  // NEW (random on-hit effects)
    SoulHarvest  = 19,  // NEW (XP / heal souls / temp buff souls)
}
