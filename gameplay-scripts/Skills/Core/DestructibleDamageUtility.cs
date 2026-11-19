using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single shared place for "this AoE/aura/projectile should damage buildings too".
/// Skills that want building damage just call DamageInRadius — keeps the spatial-hash query
/// pattern consistent and avoids each skill rolling its own padded query + box-overlap test.
/// All buffers are static and reused per call, so callers pay no allocations.
/// </summary>
public static class DestructibleDamageUtility
{
    // Pad the query radius by enough to catch towers/houses whose obstacle box overlaps the
    // circle even when their center sits a few units outside `radius`. Matches the convention
    // FireballProjectile / AxeProjectile use, but tuned for the largest building (tower).
    const float QueryPadding = 4f;

    static readonly List<ISpatialEntity> _buf = new List<ISpatialEntity>(8);

    /// <summary>
    /// Apply <paramref name="damage"/> to every live <see cref="Destructible"/> whose obstacle
    /// box overlaps a circle at <paramref name="center"/> with <paramref name="radius"/>.
    /// Returns the number of unique destructibles damaged.
    /// </summary>
    public static int DamageInRadius(Vector2 center, float radius, float damage)
    {
        if (damage <= 0f || radius <= 0f) return 0;
        var hash = SpatialHashManager.Instance;
        if (hash == null) return 0;

        _buf.Clear();
        hash.Query(SpatialCategory.Building, center, radius + QueryPadding, _buf);

        int hits = 0;
        for (int i = 0; i < _buf.Count; i++)
        {
            if (!(_buf[i] is Destructible d) || d.IsDead) continue;
            if (!BuildingCollision.CircleOverlapsBox(d.Position, d.ObstacleHalfExtents,
                                                     center, radius, out _, out _))
                continue;
            d.TakeDamage(damage);
            hits++;
        }
        return hits;
    }

    /// <summary>
    /// Nearest live destructible to <paramref name="origin"/> within <paramref name="radius"/>,
    /// or null. Used by skills that want a "fall back to a building when no enemies are around"
    /// rule (e.g. Lightning's first bolt).
    /// </summary>
    public static Destructible FindNearest(Vector2 origin, float radius)
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return null;

        _buf.Clear();
        hash.Query(SpatialCategory.Building, origin, radius + QueryPadding, _buf);

        Destructible best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < _buf.Count; i++)
        {
            if (!(_buf[i] is Destructible d) || d.IsDead) continue;
            float sqr = ((Vector2)d.Position - origin).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = d; }
        }
        return best;
    }
}
