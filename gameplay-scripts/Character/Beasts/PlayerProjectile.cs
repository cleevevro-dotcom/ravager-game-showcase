using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pooled projectile used by Skeleton/Slime/WindCutter. Travels in a straight line, hits all
/// enemies inside its hit radius (with optional pierce), can apply damage / slow / knockback,
/// and optionally explodes on impact for AoE damage.
///
/// Pure pool play — no Instantiate at runtime.
/// </summary>
public class PlayerProjectile : MonoBehaviour
{
    SpriteRenderer _sr;
    DynamicPool<PlayerProjectile> _pool;

    Vector2 _dir;
    float _speed;
    float _damage;
    float _hitRadius;
    float _maxDistance;
    float _traveled;
    int   _piercesLeft;
    float _slowFactor;
    float _slowDuration;
    float _knockback;
    float _aoeRadius;        // 0 means no AoE on hit
    bool  _alive;

    readonly HashSet<EnemyBase> _hitOnce = new HashSet<EnemyBase>();
    readonly List<ISpatialEntity> _scratch = new List<ISpatialEntity>(16);

    public void Configure(DynamicPool<PlayerProjectile> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
    }

    public void Launch(
        Vector2 origin, Vector2 dir,
        float speed, float damage, float hitRadius, float maxDistance,
        int pierceCount = 0,
        float slowFactor = 1f, float slowDuration = 0f,
        float knockback = 0f, float aoeRadius = 0f,
        Color? tint = null, float visualScale = 1f)
    {
        _dir          = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        _speed        = speed;
        _damage       = damage;
        _hitRadius    = hitRadius;
        _maxDistance  = maxDistance;
        _piercesLeft  = pierceCount;
        _slowFactor   = slowFactor;
        _slowDuration = slowDuration;
        _knockback    = knockback;
        _aoeRadius    = aoeRadius;
        _traveled     = 0f;
        _alive        = true;
        _hitOnce.Clear();

        transform.position = origin;
        float angleDeg = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
        transform.localScale = new Vector3(visualScale, visualScale, 1f);

        if (_sr != null && tint.HasValue) _sr.color = tint.Value;
    }

    void Update()
    {
        if (!_alive) return;
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

        float dt = Time.deltaTime;
        Vector2 step = _dir * _speed * dt;
        transform.position += (Vector3)step;
        _traveled += step.magnitude;

        TickHits();
        if (!_alive) return;

        if (_traveled >= _maxDistance) Recycle();
    }

    void TickHits()
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return;

        Vector2 pos = transform.position;
        _scratch.Clear();
        hash.Query(SpatialCategory.Enemy, pos, _hitRadius, _scratch);

        for (int i = 0; i < _scratch.Count; i++)
        {
            if (!(_scratch[i] is EnemyBase e) || e.IsDead) continue;
            if (_hitOnce.Contains(e)) continue;
            _hitOnce.Add(e);

            ApplyHit(e, pos);

            if (_piercesLeft <= 0)
            {
                if (_aoeRadius > 0f) ApplyAoe(pos);
                Recycle();
                return;
            }
            _piercesLeft--;
        }

        // Buildings stop the projectile (same as Fireball/Axe). Do this AFTER enemy hits so a
        // pierce shot can clear a soldier-line first, then bonk the tower behind it. AoE on
        // impact still applies via ApplyAoe.
        int bld = DestructibleDamageUtility.DamageInRadius(pos, _hitRadius, _damage);
        if (bld > 0)
        {
            if (_aoeRadius > 0f) ApplyAoe(pos);
            Recycle();
        }
    }

    void ApplyHit(EnemyBase e, Vector2 pos)
    {
        e.TakeDamage(_damage);
        if (_slowFactor < 1f && _slowDuration > 0f) e.ApplySlow(_slowFactor, _slowDuration);
        if (_knockback > 0f) KnockbackUtility.ApplyRadial(e, pos, _knockback);
    }

    void ApplyAoe(Vector2 center)
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _scratch.Clear();
        hash.Query(SpatialCategory.Enemy, center, _aoeRadius, _scratch);
        float r2 = _aoeRadius * _aoeRadius;
        for (int i = 0; i < _scratch.Count; i++)
        {
            if (!(_scratch[i] is EnemyBase e) || e.IsDead) continue;
            if ((e.Position - center).sqrMagnitude > r2) continue;
            if (_hitOnce.Contains(e)) continue; // already hit by direct
            e.TakeDamage(_damage * 0.6f);
            if (_slowFactor < 1f && _slowDuration > 0f) e.ApplySlow(_slowFactor, _slowDuration);
        }
    }

    void Recycle()
    {
        if (!_alive) return;
        _alive = false;
        if (_pool != null) _pool.Return(this);
        else gameObject.SetActive(false);
    }
}
