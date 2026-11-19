using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persistent ground puddle dropped by <see cref="PlagueSkill"/>. Damages and slows enemies
/// inside its radius on a fixed tick. The longer an enemy stands inside, the more bonus damage
/// it takes (the T2+ "ramp"). Pooled — no per-spawn allocations.
/// </summary>
public class PlaguePuddle : MonoBehaviour
{
    const float TickInterval = 0.2f;

    SpriteRenderer _sr;
    DynamicPool<PlaguePuddle> _pool;

    float _radius;
    float _dps;
    float _slowFactor;
    float _slowDuration;
    float _knockback;
    float _spawnTime;
    float _lifetime;
    float _nextTick;
    float _rampDpsPerSec;     // T2 passive: extra DPS added per second the puddle has lived
    float _rampDpsCap;

    readonly List<ISpatialEntity> _hits = new List<ISpatialEntity>(16);
    readonly Dictionary<EnemyBase, float> _enemyDwell = new Dictionary<EnemyBase, float>(16);
    readonly List<EnemyBase> _toForget = new List<EnemyBase>(8);

    public void Configure(DynamicPool<PlaguePuddle> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
    }

    public void Launch(
        Vector2 position, float radius, float dps, float slowFactor, float slowDuration,
        float knockback, float lifetime, float rampDpsPerSec, float rampDpsCap)
    {
        transform.position = position;
        _radius        = radius;
        _dps           = dps;
        _slowFactor    = slowFactor;
        _slowDuration  = slowDuration;
        _knockback     = knockback;
        _lifetime      = lifetime;
        _rampDpsPerSec = rampDpsPerSec;
        _rampDpsCap    = rampDpsCap;
        _spawnTime     = Time.time;
        _nextTick      = Time.time;
        _enemyDwell.Clear();

        if (_sr != null)
        {
            float visualScale = radius * 2f / PlaceholderSprites.GetVisibleDiameterUnits(64);
            transform.localScale = new Vector3(visualScale, visualScale, 1f);
            var c = SkillColors.Plague;
            c.a = 0.5f;
            _sr.color = c;
        }
    }

    void Update()
    {
        float elapsed = Time.time - _spawnTime;
        if (elapsed >= _lifetime) { _pool?.Return(this); return; }

        // Fade alpha out across the lifetime so the player sees it clearing.
        if (_sr != null)
        {
            float k = 1f - (elapsed / _lifetime);
            var c = _sr.color;
            c.a = Mathf.Lerp(0.15f, 0.5f, k);
            _sr.color = c;
        }

        if (Time.time < _nextTick) return;
        _nextTick += TickInterval;

        var hash = SpatialHashManager.Instance;
        if (hash == null) return;

        _hits.Clear();
        hash.Query(SpatialCategory.Enemy, transform.position, _radius, _hits);
        float radiusSqr = _radius * _radius;
        float baseDmgThisTick = _dps * TickInterval;

        // Process current hits & accumulate dwell time.
        for (int i = 0; i < _hits.Count; i++)
        {
            if (!(_hits[i] is EnemyBase e) || e.IsDead) continue;
            if ((e.Position - (Vector2)transform.position).sqrMagnitude > radiusSqr) continue;

            float dwell = 0f;
            _enemyDwell.TryGetValue(e, out dwell);
            dwell += TickInterval;
            _enemyDwell[e] = dwell;

            float bonus = Mathf.Min(_rampDpsCap, dwell * _rampDpsPerSec);
            float dmg   = baseDmgThisTick + bonus * TickInterval;
            e.TakeDamage(dmg);
            e.ApplySlow(_slowFactor, _slowDuration);
            // Visual-only plague mark: zone deals its own damage above, so dps=0. The status mark
            // gives the green tint via EnemyBase's tint priority chain and persists briefly after
            // the enemy leaves the puddle so the player sees who was infected.
            e.ApplyPlague(0f, TickInterval * 2f);
            if (_knockback > 0f) KnockbackUtility.ApplyRadial(e, transform.position, _knockback * TickInterval);
        }

        // Drop dwell entries for dead/escaped enemies so we don't leak references.
        _toForget.Clear();
        foreach (var kv in _enemyDwell)
        {
            var e = kv.Key;
            if (e == null || e.IsDead ||
                (e.Position - (Vector2)transform.position).sqrMagnitude > radiusSqr)
                _toForget.Add(e);
        }
        for (int i = 0; i < _toForget.Count; i++) _enemyDwell.Remove(_toForget[i]);

        // Aura also chips buildings inside the puddle. Uses the base per-tick value (no dwell
        // ramp) so towers don't get melted faster than enemies in the same zone.
        DestructibleDamageUtility.DamageInRadius(transform.position, _radius, baseDmgThisTick);
    }
}
