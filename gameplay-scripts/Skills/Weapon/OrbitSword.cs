using System.Collections.Generic;
using UnityEngine;

public class OrbitSword : MonoBehaviour
{
    public float angleOffsetDeg;
    public float hitRadius = 0.8f;
    public float damagePerHit;
    public float knockback;
    public float hitCooldown = 0.4f;

    readonly Dictionary<EnemyBase, float> _hitCooldowns = new Dictionary<EnemyBase, float>();
    readonly List<ISpatialEntity> _buf = new List<ISpatialEntity>(8);

    public void Tick(Vector2 origin, float angleDeg, float orbitRadius)
    {
        float totalAngle = angleDeg + angleOffsetDeg;
        float rad = totalAngle * Mathf.Deg2Rad;
        Vector2 pos = origin + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * orbitRadius;
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, 0f, totalAngle + 90f);

        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _buf.Clear();
        hash.Query(SpatialCategory.Enemy, pos, hitRadius, _buf);
        float now = Time.time;
        for (int i = 0; i < _buf.Count; i++)
        {
            if (_buf[i] is EnemyBase e && !e.IsDead)
            {
                if (_hitCooldowns.TryGetValue(e, out float next) && now < next) continue;
                _hitCooldowns[e] = now + hitCooldown;
                e.TakeDamage(damagePerHit);
                if (knockback > 0f) KnockbackUtility.ApplyRadial(e, pos, knockback);
            }
        }

        // The sword orbit slashes at hitRadius — if a building/tower sits in that arc, slice
        // it too. Throttled by the same cadence as enemy hits so a parked orbit doesn't melt
        // a tower in one frame.
        if (now >= _nextBuildingHitTime)
        {
            int bld = DestructibleDamageUtility.DamageInRadius(pos, hitRadius, damagePerHit);
            if (bld > 0) _nextBuildingHitTime = now + hitCooldown;
        }
    }

    float _nextBuildingHitTime;

    public void ResetHits() => _hitCooldowns.Clear();
}
