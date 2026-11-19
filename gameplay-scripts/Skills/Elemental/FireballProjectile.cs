using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fireball that PIERCES all enemies in its path. Direct damage is small; the burn is the threat.
/// Each enemy can only be hit/burned once per fireball lifetime. T3 adds an explosion at end-of-life.
/// </summary>
public class FireballProjectile : MonoBehaviour
{
    Vector2 _dir;
    float   _speed;
    float   _damage;
    float   _burnDps;
    float   _burnDuration;
    float   _hitRadius;
    float   _spawnTime;
    float   _lifetime;
    float   _knockback;
    bool    _explodeOnEnd;
    float   _explodeRadius;

    SpriteRenderer _sr;
    DynamicPool<FireballProjectile> _pool;

    readonly List<ISpatialEntity> _hitBuffer      = new List<ISpatialEntity>(16);
    readonly List<ISpatialEntity> _buildingBuffer = new List<ISpatialEntity>(4);
    readonly HashSet<EnemyBase>   _hitOnce        = new HashSet<EnemyBase>();

    public void Configure(DynamicPool<FireballProjectile> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
    }

    public void Launch(
        Vector2 origin, Vector2 dir, float speed, float damage,
        float hitRadius, float lifetime, float knockback,
        float burnDps, float burnDuration,
        bool explode, float explodeRadius,
        float visualScale)
    {
        transform.position   = origin;
        transform.localScale = Vector3.one * visualScale;
        _dir              = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
        // Sprite's RIGHT side is the front of the fireball, so align +X to the flight direction.
        // Atan2 of the normalized dir gives the world Z-rotation that rotates Vector2.right onto _dir.
        float angleDeg    = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
        _speed            = speed;
        _damage           = damage;
        _hitRadius        = hitRadius;
        _lifetime         = lifetime;
        _knockback        = knockback;
        _burnDps          = burnDps;
        _burnDuration     = burnDuration;
        _explodeOnEnd     = explode;
        _explodeRadius    = explodeRadius;
        _spawnTime        = Time.time;
        _hitOnce.Clear();

        if (_sr != null) _sr.color = SkillColors.Fire;
    }

    void Update()
    {
        transform.position += (Vector3)(_dir * _speed * Time.deltaTime);

        if (Time.time - _spawnTime >= _lifetime) { Retire(endOfLife: true); return; }

        var hash = SpatialHashManager.Instance;
        if (hash == null) return;

        _hitBuffer.Clear();
        hash.Query(SpatialCategory.Enemy, transform.position, _hitRadius, _hitBuffer);
        for (int i = 0; i < _hitBuffer.Count; i++)
        {
            if (_hitBuffer[i] is EnemyBase e && !e.IsDead && _hitOnce.Add(e))
                BurnEnemy(e);
        }

        // Buildings block the fireball — damage first hit, then retire.
        _buildingBuffer.Clear();
        hash.Query(SpatialCategory.Building, transform.position, _hitRadius + 1.5f, _buildingBuffer);
        Vector2 pos = transform.position;
        for (int i = 0; i < _buildingBuffer.Count; i++)
        {
            if (!(_buildingBuffer[i] is Destructible d) || d.IsDead) continue;
            if (!BuildingCollision.CircleOverlapsBox(d.Position, d.ObstacleHalfExtents,
                                                     pos, _hitRadius, out _, out _))
                continue;
            d.TakeDamage(_damage * 2f);
            Retire(endOfLife: false);
            return;
        }
    }

    void BurnEnemy(EnemyBase e)
    {
        if (_damage > 0f) e.TakeDamage(_damage);
        if (_burnDps > 0f && _burnDuration > 0f) e.ApplyBurn(_burnDps, _burnDuration);
        if (_knockback > 0f) KnockbackUtility.ApplyRadial(e, transform.position, _knockback);
    }

    void Retire(bool endOfLife)
    {
        if (_explodeOnEnd && endOfLife) Explode();
        _pool?.Return(this);
    }

    void Explode()
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _hitBuffer.Clear();
        hash.Query(SpatialCategory.Enemy, transform.position, _explodeRadius, _hitBuffer);
        for (int i = 0; i < _hitBuffer.Count; i++)
        {
            if (_hitBuffer[i] is EnemyBase e && !e.IsDead)
            {
                e.TakeDamage(_damage * 2f);
                if (_burnDps > 0f) e.ApplyBurn(_burnDps, _burnDuration);
            }
        }
    }
}
