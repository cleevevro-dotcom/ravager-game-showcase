using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stat passive: damage scales with missing HP (T1), attack speed scales below 50% HP (T2),
/// and at critical HP (≤20%) a one-shot invulnerability + shockwave saves the player (T3).
/// </summary>
public class BerserkSkill : SkillBase
{
    public override SkillId       Id          => SkillId.Berserk;
    public override SkillCategory Category    => SkillCategory.Stat;
    public override string        DisplayName => "Berserk";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    const float CritThreshold        = 0.20f;
    const float EmergencyInvulnDur   = 1.5f;
    const float EmergencyCooldownBase = 30f;
    const float ShockwaveBaseDamage  = 30f;
    const float ShockwaveBaseRadius  = 4f;

    int   _t1, _t2, _t3;
    float _appliedDmgMul    = 1f;
    float _appliedAtkSpdMul = 1f;
    float _invulnEndTime;
    float _nextEmergencyTime;
    bool  _invulnActive;

    readonly List<EnemyBase> _hits = new List<EnemyBase>(32);

    public override void SkillTick(float dt)
    {
        if (!Unlocked || playerStats == null) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        float hpFrac = playerStats.maxHp > 0f ? playerStats.CurrentHp / playerStats.maxHp : 1f;

        // T1 — damage ramps with missing HP. Stronger ramp below 50%.
        float dmgMul = 1f;
        if (_t1 >= 1)
        {
            float missing = 1f - hpFrac;
            dmgMul = 1f + 0.10f * _t1 * missing;
            if (hpFrac < 0.5f) dmgMul += 0.20f * _t1 * (0.5f - hpFrac) * 2f;
        }
        ApplyDmg(dmgMul);

        // T2 — attack speed ramps below 50% HP.
        float atkSpdMul = 1f;
        if (_t2 >= 1 && hpFrac < 0.5f)
            atkSpdMul = 1f + 0.20f * _t2 * (0.5f - hpFrac) * 2f;
        ApplyAtkSpd(atkSpdMul);

        // T3 — emergency invulnerability + shockwave at crit HP.
        if (_t3 >= 1 && !_invulnActive && hpFrac <= CritThreshold && Time.time >= _nextEmergencyTime)
        {
            playerStats.invulnCount++;
            _invulnActive      = true;
            _invulnEndTime     = Time.time + EmergencyInvulnDur;
            _nextEmergencyTime = Time.time + Mathf.Max(15f, EmergencyCooldownBase - 3f * (_t3 - 1));
            FireShockwave();
        }
        if (_invulnActive && Time.time >= _invulnEndTime)
        {
            playerStats.invulnCount = Mathf.Max(0, playerStats.invulnCount - 1);
            _invulnActive = false;
        }
    }

    void FireShockwave()
    {
        if (playerTransform == null) return;
        Vector2 origin = playerTransform.position;
        float radius = ShockwaveBaseRadius + 0.5f * (_t3 - 1);
        float damage = ShockwaveBaseDamage + 8f * (_t3 - 1);

        TargetingUtility.FillEnemiesInRadius(origin, radius, _hits);
        for (int i = 0; i < _hits.Count; i++)
        {
            var e = _hits[i];
            e.TakeDamage(damage);
            KnockbackUtility.ApplyRadial(e, origin, 6f);
        }
    }

    void ApplyDmg(float newMul)
    {
        if (Mathf.Approximately(newMul, _appliedDmgMul)) return;
        if (_appliedDmgMul > 0.001f) playerStats.outgoingDamageMul /= _appliedDmgMul;
        playerStats.outgoingDamageMul *= newMul;
        _appliedDmgMul = newMul;
    }

    void ApplyAtkSpd(float newMul)
    {
        if (Mathf.Approximately(newMul, _appliedAtkSpdMul)) return;
        if (_appliedAtkSpdMul > 0.001f) playerStats.attackSpeed /= _appliedAtkSpdMul;
        playerStats.attackSpeed *= newMul;
        _appliedAtkSpdMul = newMul;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;
    }

    void OnDestroy()
    {
        // Release any held invuln so resetting the scene doesn't leave the counter pinned.
        if (_invulnActive && playerStats != null)
            playerStats.invulnCount = Mathf.Max(0, playerStats.invulnCount - 1);
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: Damage ramps as HP drops (much stronger below 50%)",
        2 => "T2: Attack speed climbs at low HP",
        3 => "T3: At critical HP — short invuln + shockwave",
        _ => "",
    };
}
