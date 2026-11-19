using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Golem heavy melee. Slow swing, big damage, wide front cone, big knockback. No projectile —
/// pure AoE in front of the player. Spatial-hash query, no colliders.
/// </summary>
public class GolemSmashAttack : BaseBeastAttack
{
    [Header("Golem Smash")]
    [SerializeField] float _smashOffset    = 1.0f;
    [SerializeField] float _smashRadius    = 1.5f;
    [SerializeField] float _smashDamage    = 22f;
    [SerializeField] float _smashKnockback = 6f;
    [SerializeField] float _coneDeg        = 130f;

    readonly List<ISpatialEntity> _hits = new List<ISpatialEntity>(16);
    GolemSmashEffect _effect;

    protected override void Awake()
    {
        base.Awake();
        baseCooldown   = 1.5f;    // slow swing
        autoLockRange  = 5f;
        attackSlowdown = 0.35f;   // very heavy
        EnsureEffect();
    }

    void EnsureEffect()
    {
        if (_effect != null) return;
        var go = new GameObject("GolemSmashEffect");
        go.transform.SetParent(transform, false);
        _effect = go.AddComponent<GolemSmashEffect>();
    }

    protected override void DoAttack(Vector2 origin, Vector2 aimDir)
    {
        Vector2 center  = origin + aimDir * _smashOffset;
        float   radius  = _smashRadius + rangeAdd;
        float   dmg     = ComputeDamage(_smashDamage);
        float   aimAng  = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
        float   halfCone = _coneDeg * 0.5f;

        // Visual: dark brown impact circle + earthshatter sprite. Damage logic below is untouched.
        if (_effect != null) _effect.Play(center, radius);

        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _hits.Clear();
        hash.Query(SpatialCategory.Enemy, center, radius, _hits);

        for (int i = 0; i < _hits.Count; i++)
        {
            if (!(_hits[i] is EnemyBase e) || e.IsDead) continue;
            Vector2 toEnemy = (Vector2)e.Position - origin;
            if (toEnemy.sqrMagnitude < 0.0001f) toEnemy = aimDir;
            float ang = Mathf.Atan2(toEnemy.y, toEnemy.x) * Mathf.Rad2Deg;
            if (Mathf.Abs(Mathf.DeltaAngle(aimAng, ang)) > halfCone) continue;
            e.TakeDamage(dmg);
            KnockbackUtility.ApplyRadial(e, center, _smashKnockback);
            // Phase 7: report per-enemy hit so bonus skills (Bloodlust on-hit
            // stacking, Soul Harvest, Chaos Surge) trigger for Golem too.
            RaiseAttackHit(e);
        }

        // Buildings — golem is good at smashing things.
        _hits.Clear();
        hash.Query(SpatialCategory.Building, center, radius, _hits);
        for (int i = 0; i < _hits.Count; i++)
            if (_hits[i] is Destructible d && !d.IsDead) d.TakeDamage(dmg);
    }
}
