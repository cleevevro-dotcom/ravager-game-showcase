using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

/// <summary>
/// Stat passive: damage reduction (T1), reflect-shockwave when taking damage (T2), and a
/// periodic invulnerability shield (T3). Tracks HP delta per frame to detect "took damage".
/// </summary>
public class IronSkinSkill : SkillBase
{
    public override SkillId       Id          => SkillId.IronSkin;
    public override SkillCategory Category    => SkillCategory.Stat;
    public override string        DisplayName => "Iron Skin";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    const float ShieldDuration       = 1.0f;
    // Anti-spike: even with the player getting hit every frame, the reflect shockwave can
    // fire at most once per ReflectCooldown seconds. Saves the frame from a 30-enemy
    // TakeDamage + Knockback + Hurt-trigger burst per hit when many enemies are stacked.
    // Tightened from 0.25f → 0.4f (BUG-01 follow-up) so back-to-back hits at 150 enemies
    // don't fire two shockwaves inside the same frame budget.
    const float ReflectCooldown      = 0.4f;
    // Hard cap on how many enemies a single shockwave can damage. Late game can have 40+
    // enemies in the radius, and a single AoE doesn't need to scrub all of them — the
    // closest few produce the same readable visual without the per-frame stutter.
    // Tightened 12 → 6 (BUG-01 follow-up) for 150-enemy load.
    const int   ReflectMaxTargets    = 6;
    // Don't fire reflect for trivial damage (burn ticks, fractional poke). Saves a query +
    // 6-target burst whenever the player only loses a sliver of HP.
    const float ReflectMinDamage     = 4f;

    int   _t1, _t2, _t3;
    float _appliedDR;
    float _lastHp;
    float _nextReflectTime;

    bool  _shieldActive;
    float _shieldEndTime;
    float _nextShieldTime;

    readonly List<EnemyBase> _hits = new List<EnemyBase>(32);

    static readonly CustomSampler s_ReflectSampler = CustomSampler.Create("IronSkin.ReflectShockwave");

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        if (playerStats != null) _lastHp = playerStats.CurrentHp;
    }

    public override void SkillTick(float dt)
    {
        if (!Unlocked || playerStats == null) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        // T2 — Reflect shockwave when player just took damage. Triple-gated to keep the
        // hit-frame cheap under heavy horde load:
        //   1. Cooldown — collapses a barrage of hits into one shockwave per ReflectCooldown.
        //   2. Damage threshold — trivial damage (burn ticks, sliver hits) doesn't fire AoE.
        //   3. Target cap — even with 40+ enemies in radius, only ReflectMaxTargets eat work.
        float hp = playerStats.CurrentHp;
        float dmgTaken = _lastHp - hp;
        if (_t2 >= 1 && dmgTaken >= ReflectMinDamage && Time.time >= _nextReflectTime)
        {
            _nextReflectTime = Time.time + ReflectCooldown;
            FireReflectShockwave(dmgTaken);
        }
        _lastHp = hp;

        // T3 — periodic shield (invulnerability).
        if (_t3 >= 1 && !_shieldActive && Time.time >= _nextShieldTime)
        {
            playerStats.invulnCount++;
            _shieldActive   = true;
            _shieldEndTime  = Time.time + ShieldDuration;
            _nextShieldTime = Time.time + Mathf.Max(6f, 15f - 1.5f * (_t3 - 1));
        }
        if (_shieldActive && Time.time >= _shieldEndTime)
        {
            playerStats.invulnCount = Mathf.Max(0, playerStats.invulnCount - 1);
            _shieldActive = false;
        }
    }

    void FireReflectShockwave(float dmgTaken)
    {
        if (playerTransform == null) return;
        s_ReflectSampler.Begin();
        Vector2 origin = playerTransform.position;
        float radius   = 2.5f + 0.3f * (_t2 - 1);
        float damage   = Mathf.Max(2f, dmgTaken * (0.4f + 0.2f * (_t2 - 1)));

        TargetingUtility.FillEnemiesInRadius(origin, radius, _hits);
        // Cap targets so 40 enemies stacked on the player don't all eat a TakeDamage +
        // SetTrigger("Hurt") + ApplyKnockback in one frame. Hits in spatial-hash order — close
        // enough to the player that "fairness" doesn't matter for a thorn-mail effect.
        int n = Mathf.Min(_hits.Count, ReflectMaxTargets);
        for (int i = 0; i < n; i++)
        {
            var e = _hits[i];
            // fireHurt: false — reflect skips per-enemy Animator.SetTrigger("Hurt"). With 6
            // targets the SetTrigger calls were the dominant in-frame cost. Damage + radial
            // knockback alone still feels like thorn-mail; the lack of a hurt clip on the
            // mob is invisible amid a player hit's screen-shake.
            e.TakeDamage(damage, fireHurt: false);
            KnockbackUtility.ApplyRadial(e, origin, 4f);
        }
        s_ReflectSampler.End();
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;
        if (playerStats == null) return;

        // T1 — flat damage reduction. Cap at 0.6 (60%) so the player can still die.
        playerStats.damageReduction -= _appliedDR;
        float dr = Mathf.Min(0.60f, 0.06f * t1);
        playerStats.damageReduction += dr;
        _appliedDR = dr;
    }

    void OnDestroy()
    {
        if (playerStats == null) return;
        if (_shieldActive)
            playerStats.invulnCount = Mathf.Max(0, playerStats.invulnCount - 1);
        playerStats.damageReduction -= _appliedDR;
        _appliedDR = 0f;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Damage reduction per stack",
        2 => "T2: Reflect — damaging hits trigger a shockwave",
        3 => "T3: Periodic invulnerability shield",
        _ => "",
    };
}
