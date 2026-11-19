using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zombie short-range bite. Lower base damage than Goblin claw but every kill heals via
/// PlayerStats.lifePerKill (BeastApplier already adds the lifesteal bonus). On hit, bite applies
/// a small bleed (burn-style DoT) so kills come faster, ramping the lifesteal sustain.
/// </summary>
public class ZombieBiteAttack : BaseBeastAttack
{
    [Header("Zombie Bite")]
    [SerializeField] float _biteOffset = 0.7f;
    [SerializeField] float _biteRadius = 0.95f;
    [SerializeField] float _biteDamage = 9f;
    [SerializeField] float _biteKnockback = 2.0f;
    [SerializeField] float _bleedDps      = 4f;
    [SerializeField] float _bleedDuration = 1.2f;

    readonly List<ISpatialEntity> _hits = new List<ISpatialEntity>(16);

    protected override void Awake()
    {
        base.Awake();
        baseCooldown   = 0.7f;
        autoLockRange  = 5f;
        attackSlowdown = 0.55f;
    }

    protected override void DoAttack(Vector2 origin, Vector2 aimDir)
    {
        Vector2 center = origin + aimDir * _biteOffset;
        float   r      = _biteRadius + rangeAdd;
        float   dmg    = ComputeDamage(_biteDamage);

        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _hits.Clear();
        hash.Query(SpatialCategory.Enemy, center, r, _hits);
        for (int i = 0; i < _hits.Count; i++)
        {
            if (!(_hits[i] is EnemyBase e) || e.IsDead) continue;
            e.TakeDamage(dmg);
            e.ApplyBurn(_bleedDps, _bleedDuration);
            KnockbackUtility.ApplyRadial(e, center, _biteKnockback);
            // Phase 7: per-enemy hit notification — bonus skills depend on this.
            RaiseAttackHit(e);
        }
    }
}
