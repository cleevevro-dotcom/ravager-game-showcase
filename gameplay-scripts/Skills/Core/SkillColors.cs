using UnityEngine;

public static class SkillColors
{
    public static readonly Color Fire      = new Color(1.00f, 0.45f, 0.10f, 1f);
    public static readonly Color Ice       = new Color(0.45f, 0.85f, 1.00f, 1f);
    public static readonly Color Lightning = new Color(1.00f, 0.95f, 0.30f, 1f);
    public static readonly Color Tentacle  = new Color(0.55f, 0.25f, 0.55f, 1f);
    public static readonly Color Axe       = new Color(0.80f, 0.80f, 0.85f, 1f);
    public static readonly Color Sword     = new Color(0.90f, 0.90f, 0.95f, 1f);
    public static readonly Color Physical  = new Color(0.95f, 0.60f, 0.45f, 1f);
    public static readonly Color Agility   = new Color(0.55f, 1.00f, 0.60f, 1f);
    public static readonly Color Health    = new Color(1.00f, 0.35f, 0.45f, 1f);
    public static readonly Color Fear      = new Color(0.70f, 0.40f, 1.00f, 1f);
    public static readonly Color Plague       = new Color(0.55f, 0.85f, 0.30f, 1f);
    public static readonly Color Vision       = new Color(1.00f, 0.85f, 0.55f, 1f);
    public static readonly Color Earth        = new Color(0.55f, 0.40f, 0.25f, 1f);
    public static readonly Color Wind         = new Color(0.75f, 1.00f, 0.95f, 1f);
    public static readonly Color SpiritOrb    = new Color(0.65f, 0.80f, 1.00f, 1f);
    public static readonly Color Berserk      = new Color(0.95f, 0.20f, 0.20f, 1f);
    public static readonly Color IronSkin     = new Color(0.70f, 0.75f, 0.85f, 1f);
    public static readonly Color Bloodlust    = new Color(0.85f, 0.10f, 0.30f, 1f);
    public static readonly Color ChaosSurge   = new Color(0.85f, 0.40f, 0.95f, 1f);
    public static readonly Color SoulHarvest  = new Color(0.65f, 0.95f, 0.80f, 1f);

    public static Color Get(SkillId id) => id switch
    {
        SkillId.Fire         => Fire,
        SkillId.Freeze       => Ice,
        SkillId.Lightning    => Lightning,
        SkillId.Tentacle     => Tentacle,
        SkillId.Axe          => Axe,
        SkillId.Sword        => Sword,
        SkillId.Physical     => Physical,
        SkillId.Agility      => Agility,
        SkillId.Health       => Health,
        SkillId.Fear         => Fear,
        SkillId.Plague       => Plague,
        SkillId.Vision       => Vision,
        SkillId.Earthshatter => Earth,
        SkillId.WindCutter   => Wind,
        SkillId.SpiritOrb    => SpiritOrb,
        SkillId.Berserk      => Berserk,
        SkillId.IronSkin     => IronSkin,
        SkillId.Bloodlust    => Bloodlust,
        SkillId.ChaosSurge   => ChaosSurge,
        SkillId.SoulHarvest  => SoulHarvest,
        _ => Color.white
    };
}
