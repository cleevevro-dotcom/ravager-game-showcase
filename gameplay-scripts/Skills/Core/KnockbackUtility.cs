using UnityEngine;

public static class KnockbackUtility
{
    public static void ApplyRadial(EnemyBase enemy, Vector2 from, float force, float duration = 0.15f)
    {
        if (enemy == null || enemy.IsDead || force <= 0f) return;
        Vector2 dir = (enemy.Position - from);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        else dir.Normalize();
        enemy.ApplyKnockback(dir * force, duration);
    }

    public static void ApplyDirectional(EnemyBase enemy, Vector2 dir, float force, float duration = 0.15f)
    {
        if (enemy == null || enemy.IsDead || force <= 0f) return;
        if (dir.sqrMagnitude < 0.0001f) return;
        enemy.ApplyKnockback(dir.normalized * force, duration);
    }
}
