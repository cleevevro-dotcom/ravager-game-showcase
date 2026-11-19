public class VisionSkill : SkillBase
{
    public override SkillId       Id          => SkillId.Vision;
    public override SkillCategory Category    => SkillCategory.Bonus;
    public override string        DisplayName => "Vision";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    // Base visible radius (in world units) when no Vision stacks have been picked. Mirrors the
    // PlayerStats.visionRadius default. The Dark Vision overlay (DarkVisionOverlay) reads
    // playerStats.visionRadius every frame to size the visible "hole" in the dark screen.
    const float BaseVisionRadius = 6f;

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        if (playerStats == null) return;

        // T1 — XP gain + small bump to vision radius.
        // T2 — magnet radius + larger bump to vision radius + slight middle-ring brighten.
        // T3 — adds extra to all on top, including a stronger middle-ring brighten.
        playerStats.xpGainMultiplier         = 1f + 0.20f * t1 + 0.40f * t3;
        playerStats.xpMagnetRadiusMul        = 1f + 0.20f * t2 + 0.30f * t3;
        playerStats.visionRadius             = BaseVisionRadius * (1f + 0.15f * t1 + 0.25f * t2 + 0.35f * t3);
        // Subtractive: each stack lifts the middle-ring darkness slightly. Clamped inside the
        // overlay so even maxed Vision can't push the middle ring below 0 darkness.
        playerStats.visionMidBrightnessBonus = 0.04f * t2 + 0.06f * t3;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +20% XP gain, +15% vision per stack",
        2 => "T2: +20% magnet radius, +25% vision per stack",
        3 => "T3: +40% XP, +30% magnet, +35% vision per stack",
        _ => "",
    };
}
