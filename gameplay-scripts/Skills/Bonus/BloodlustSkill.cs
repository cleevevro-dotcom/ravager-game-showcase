using UnityEngine;

/// <summary>
/// Bonus passive: stacks attack-speed on every swing (decays over time, slower with more T1
/// stacks), heals on kill (T2), and grants a damage+speed frenzy when killing an enemy (T3).
/// </summary>
public class BloodlustSkill : SkillBase
{
    public override SkillId       Id          => SkillId.Bloodlust;
    public override SkillCategory Category    => SkillCategory.Bonus;
    public override string        DisplayName => "Bloodlust";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    const int   MaxOnHitStacks       = 10;
    const float AtkSpdPerStack       = 0.05f;
    const float FrenzyDurationBase   = 3f;
    const float FrenzyDamageBonus    = 0.40f;
    const float FrenzySpeedBonus     = 0.20f;
    const float HealPerKillT2Base    = 2f;

    int     _t1, _t2, _t3;
    int     _onHitStacks;
    float   _stackDecayTimer;
    float   _frenzyEndTime;

    float _appliedAtkSpdMul = 1f;
    float _appliedDmgMul    = 1f;
    float _appliedSpdMul    = 1f;

    IPlayerAttack[] _attacks;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        Unsubscribe();
        _attacks = player != null ? player.GetComponents<IPlayerAttack>() : null;
        if (_attacks != null)
            for (int i = 0; i < _attacks.Length; i++)
                _attacks[i].OnSwing += HandleSwing;

        if (playerStats != null) playerStats.OnEnemyKilled += HandleKill;
    }

    void Unsubscribe()
    {
        if (_attacks != null)
            for (int i = 0; i < _attacks.Length; i++)
                if (_attacks[i] != null) _attacks[i].OnSwing -= HandleSwing;
        if (playerStats != null) playerStats.OnEnemyKilled -= HandleKill;
    }

    void OnDestroy() => Unsubscribe();

    void HandleSwing(Vector2 origin, Vector2 dir)
    {
        if (!Unlocked) return;
        if (_onHitStacks < MaxOnHitStacks) _onHitStacks++;
        _stackDecayTimer = 0f;
    }

    void HandleKill(EnemyBase enemy)
    {
        if (!Unlocked || playerStats == null) return;
        if (_t2 >= 1) playerStats.Heal(HealPerKillT2Base * _t2);
        if (_t3 >= 1) _frenzyEndTime = Time.time + FrenzyDurationBase + 0.5f * (_t3 - 1);
    }

    public override void SkillTick(float dt)
    {
        if (!Unlocked || playerStats == null) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        // T1 makes the decay interval longer per stack (slower decay).
        float decayInterval = Mathf.Max(0.6f, 1.6f - 0.10f * _t1);
        _stackDecayTimer += dt;
        if (_stackDecayTimer >= decayInterval)
        {
            _stackDecayTimer = 0f;
            if (_onHitStacks > 0) _onHitStacks--;
        }

        // Per-stack attack-speed buff. Re-applied via delta so reverting stays clean.
        float atkSpdMul = 1f + AtkSpdPerStack * _onHitStacks;
        ApplyAtkSpd(atkSpdMul);

        // Frenzy buff from T3 — extra damage + move speed for a short time after a kill.
        bool frenzy = _t3 >= 1 && Time.time < _frenzyEndTime;
        float dmgMul = frenzy ? 1f + FrenzyDamageBonus + 0.10f * (_t3 - 1) : 1f;
        float spdMul = frenzy ? 1f + FrenzySpeedBonus  + 0.05f * (_t3 - 1) : 1f;
        ApplyDmg(dmgMul);
        ApplySpd(spdMul);
    }

    void ApplyAtkSpd(float newMul)
    {
        if (Mathf.Approximately(newMul, _appliedAtkSpdMul)) return;
        if (_appliedAtkSpdMul > 0.001f) playerStats.attackSpeed /= _appliedAtkSpdMul;
        playerStats.attackSpeed *= newMul;
        _appliedAtkSpdMul = newMul;
    }

    void ApplyDmg(float newMul)
    {
        if (Mathf.Approximately(newMul, _appliedDmgMul)) return;
        if (_appliedDmgMul > 0.001f) playerStats.outgoingDamageMul /= _appliedDmgMul;
        playerStats.outgoingDamageMul *= newMul;
        _appliedDmgMul = newMul;
    }

    void ApplySpd(float newMul)
    {
        if (Mathf.Approximately(newMul, _appliedSpdMul)) return;
        if (_appliedSpdMul > 0.001f) playerStats.moveSpeed /= _appliedSpdMul;
        playerStats.moveSpeed *= newMul;
        _appliedSpdMul = newMul;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Attack speed on every swing (decays slower per stack)",
        2 => "T2: Lifesteal — heal on every kill",
        3 => "T3: Kill frenzy — bonus damage & speed after a kill",
        _ => "",
    };
}
