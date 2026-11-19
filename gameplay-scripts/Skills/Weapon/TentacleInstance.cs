using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A summoned tentacle that appears at the target, plays a short rise-and-strike visual,
/// deals damage ONCE at the strike frame, then hides and returns to its pool. Uses object
/// pooling — no Instantiate/Destroy during gameplay.
/// </summary>
public class TentacleInstance : MonoBehaviour
{
    // Phase timings (seconds).
    const float RiseDuration   = 0.10f;   // pop up
    const float StrikeDelay    = 0.20f;   // wind-up before the single hit
    const float HideDelay      = 0.12f;   // linger after strike, then hide
    const float SpinSpeedDeg   = 1080f;

    GameObject     _radiusDisplay;
    SpriteRenderer _radiusSR;

    float _spawnTime;
    float _damage;
    float _hitRadius;
    float _knockback;
    bool  _struck;

    SpriteRenderer _sr;
    DynamicPool<TentacleInstance> _pool;

    readonly List<ISpatialEntity> _buf = new List<ISpatialEntity>(8);

    public void Configure(DynamicPool<TentacleInstance> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
    }

    public void Launch(Vector2 pos, float _unusedLifetime, float damage, float hitRadius, float knockback)
    {
        transform.position = pos;
        transform.rotation = Quaternion.identity;
        _damage    = damage;
        _hitRadius = hitRadius;
        _knockback = knockback;
        _spawnTime = Time.time;
        _struck    = false;

        if (_sr != null)
        {
            var c = SkillColors.Tentacle;
            c.a = 0f;
            _sr.color = c;
            transform.localScale = new Vector3(0.4f, 0.1f, 1f);
        }

        // Create radius display if not exists
        if (_radiusDisplay == null)
        {
            _radiusDisplay = new GameObject("RadiusDisplay");
            _radiusDisplay.transform.SetParent(transform, false);
            _radiusSR = _radiusDisplay.AddComponent<SpriteRenderer>();
            _radiusSR.sprite = PlaceholderSprites.GetCircle(48);
            _radiusSR.color = new Color(SkillColors.Tentacle.r, SkillColors.Tentacle.g, SkillColors.Tentacle.b, 0.3f);
            _radiusSR.sortingOrder = 29999;  // below tentacle body
        }

        if (_radiusSR != null)
        {
            _radiusDisplay.transform.localScale = new Vector3(hitRadius * 2f, hitRadius * 2f, 1f);
            var c = _radiusSR.color;
            c.a = 0f;
            _radiusSR.color = c;
        }
    }

    void Update()
    {
        float elapsed = Time.time - _spawnTime;
        transform.Rotate(0f, 0f, SpinSpeedDeg * Time.deltaTime);

        if (elapsed < RiseDuration)
        {
            float k = elapsed / RiseDuration;
            if (_sr != null)
            {
                var c = _sr.color; c.a = k;
                _sr.color = c;
            }
            transform.localScale = new Vector3(
                Mathf.Lerp(0.4f, 1.1f,  k),
                Mathf.Lerp(0.1f, 0.35f, k),
                1f);
            return;
        }

        // Show damage radius during wind-up
        if (elapsed < StrikeDelay && _radiusSR != null)
        {
            float k = (elapsed - RiseDuration) / (StrikeDelay - RiseDuration);
            var c = _radiusSR.color;
            c.a = Mathf.Lerp(0f, 0.4f, k);
            _radiusSR.color = c;
        }

        if (!_struck && elapsed >= StrikeDelay)
        {
            Strike();
            _struck = true;
        }

        if (elapsed >= StrikeDelay + HideDelay)
        {
            if (_sr != null)
            {
                var c = _sr.color; c.a = 0f;
                _sr.color = c;
            }
            if (_radiusSR != null)
            {
                var c = _radiusSR.color; c.a = 0f;
                _radiusSR.color = c;
            }
            _pool?.Return(this);
        }
        else if (_struck && _sr != null)
        {
            float k = 1f - Mathf.Clamp01((elapsed - StrikeDelay) / HideDelay);
            var c = _sr.color; c.a = k;
            _sr.color = c;

            if (_radiusSR != null)
            {
                var rc = _radiusSR.color;
                rc.a = 0f;
                _radiusSR.color = rc;
            }
        }
    }

    void Strike()
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _buf.Clear();
        hash.Query(SpatialCategory.Enemy, transform.position, _hitRadius, _buf);
        for (int i = 0; i < _buf.Count; i++)
        {
            if (_buf[i] is EnemyBase e && !e.IsDead)
            {
                e.TakeDamage(_damage);
                if (_knockback > 0f) KnockbackUtility.ApplyRadial(e, transform.position, _knockback);
            }
        }

        // Tentacle is a one-shot AoE strike — also damage a tower/house whose box overlaps
        // the strike radius.
        DestructibleDamageUtility.DamageInRadius(transform.position, _hitRadius, _damage);
    }
}
