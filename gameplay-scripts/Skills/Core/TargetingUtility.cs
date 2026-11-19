using System.Collections.Generic;
using UnityEngine;

public static class TargetingUtility
{
    static readonly List<ISpatialEntity> _scratch = new List<ISpatialEntity>(32);

    public static EnemyBase FindNearestEnemy(Vector2 origin, float radius)
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return null;
        _scratch.Clear();
        hash.Query(SpatialCategory.Enemy, origin, radius, _scratch);

        EnemyBase best = null;
        float bestSqr  = float.MaxValue;
        for (int i = 0; i < _scratch.Count; i++)
        {
            if (_scratch[i] is EnemyBase e && !e.IsDead)
            {
                float sqr = ((Vector2)e.Position - origin).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = e; }
            }
        }
        return best;
    }

    public static int FillEnemiesInRadius(Vector2 origin, float radius, List<EnemyBase> output)
    {
        output.Clear();
        var hash = SpatialHashManager.Instance;
        if (hash == null) return 0;
        _scratch.Clear();
        hash.Query(SpatialCategory.Enemy, origin, radius, _scratch);
        for (int i = 0; i < _scratch.Count; i++)
            if (_scratch[i] is EnemyBase e && !e.IsDead) output.Add(e);
        return output.Count;
    }
}
