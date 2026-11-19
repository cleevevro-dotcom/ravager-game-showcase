using UnityEngine;

/// <summary>
/// Bonus passive: extra XP from kills (T1), chance for kills to drop a healing soul (T2), and
/// chance for kills to drop a buff soul that boosts damage briefly (T3). Subscribes to
/// <see cref="PlayerStats.OnEnemyKilled"/> so it integrates with whatever beast/skill killed.
/// </summary>
public class SoulHarvestSkill : SkillBase
{
    public override SkillId       Id          => SkillId.SoulHarvest;
    public override SkillCategory Category    => SkillCategory.Bonus;
    public override string        DisplayName => "Soul Harvest";
    public override SkillRarity   Rarity      => SkillRarity.Epic;

    int   _t1, _t2, _t3;
    float _appliedXpMul = 0f;
    float _appliedDmgMul = 1f;
    float _buffEndTime;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        if (playerStats != null) playerStats.OnEnemyKilled += HandleKill;
    }

    void OnDestroy()
    {
        if (playerStats == null) return;
        playerStats.xpGainMultiplier -= _appliedXpMul;
        if (_appliedDmgMul > 0.001f) playerStats.outgoingDamageMul /= _appliedDmgMul;
        playerStats.OnEnemyKilled -= HandleKill;
    }

    void HandleKill(EnemyBase enemy)
    {
        if (!Unlocked) return;
        // T2 — chance to drop a healing soul on kill.
        if (_t2 >= 1 && Random.value < 0.15f * _t2)
            playerStats.Heal(3f * _t2);

        // T3 — chance to drop a buff soul: refresh the timed damage buff.
        if (_t3 >= 1 && Random.value < 0.10f * _t3)
            _buffEndTime = Time.time + 5f + 1f * (_t3 - 1);
    }

    public override void SkillTick(float dt)
    {
        if (!Unlocked || playerStats == null) return;
        bool buffActive = _t3 >= 1 && Time.time < _buffEndTime;
        float dmgMul = buffActive ? 1f + 0.20f + 0.05f * (_t3 - 1) : 1f;
        if (!Mathf.Approximately(dmgMul, _appliedDmgMul))
        {
            if (_appliedDmgMul > 0.001f) playerStats.outgoingDamageMul /= _appliedDmgMul;
            playerStats.outgoingDamageMul *= dmgMul;
            _appliedDmgMul = dmgMul;
        }
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;
        if (playerStats == null) return;

        // T1 — extra XP, additive on the multiplier (cancels prior delta on re-apply).
        playerStats.xpGainMultiplier -= _appliedXpMul;
        float xpAdd = 0.50f * t1;
        playerStats.xpGainMultiplier += xpAdd;
        _appliedXpMul = xpAdd;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +20% XP gain per stack",
        2 => "T2: Kills can drop healing souls",
        3 => "T3: Kills can drop buff souls (temp damage)",
        _ => "",
    };
}
