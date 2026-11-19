using UnityEngine;

public class HealthSkill : SkillBase
{
    public override SkillId       Id          => SkillId.Health;
    public override SkillCategory Category    => SkillCategory.Stat;
    public override string        DisplayName => "Health";
    public override SkillRarity   Rarity      => SkillRarity.Common;

    float _appliedMaxHp;
    float _appliedRegen;
    float _appliedLifeKill;
    float _appliedKbResist;

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        if (playerStats == null) return;

        // Cancel previously-applied deltas so re-applies stay idempotent.
        playerStats.maxHp           -= _appliedMaxHp;
        playerStats.hpRegenPerSec   -= _appliedRegen;
        playerStats.lifePerKill     -= _appliedLifeKill;
        playerStats.knockbackResist -= _appliedKbResist;

        // T1 — flat Max HP per stack.
        float maxAdd   = 30f * t1;
        // T2 — slow regen per stack.
        float regenAdd = 1f * t2;
        // T3 — life on kill, plus knockback resist scaling.
        float lifeKill = 3f * t3;
        float kbResist = Mathf.Min(0.9f, 0.30f * t3);

        playerStats.maxHp           += maxAdd;
        playerStats.hpRegenPerSec   += regenAdd;
        playerStats.lifePerKill     += lifeKill;
        playerStats.knockbackResist += kbResist;

        float maxDelta = maxAdd - _appliedMaxHp;
        if (maxDelta > 0f) playerStats.Heal(maxDelta);

        _appliedMaxHp    = maxAdd;
        _appliedRegen    = regenAdd;
        _appliedLifeKill = lifeKill;
        _appliedKbResist = kbResist;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +30 Max HP per stack",
        2 => "T2: +1 HP/sec regen per stack",
        3 => "T3: +3 HP per kill + knockback resist",
        _ => "",
    };
}
