using UnityEngine;

/// <summary>
/// Skeleton archer attack: long-range arrow. Fires only when an enemy is within auto-lock range,
/// otherwise the swing is skipped (no aimless arrows). Linked skills still trigger via OnSwing.
/// </summary>
public class SkeletonArrowAttack : BaseBeastAttack
{
    [Header("Skeleton Arrow")]
    [SerializeField] float _arrowSpeed     = 12f;
    [SerializeField] float _arrowDamage    = 10f;
    [SerializeField] float _arrowHitRadius = 0.45f;
    [SerializeField] float _arrowMaxDist   = 12f;

    GameObject _root;
    DynamicPool<PlayerProjectile> _pool;

    protected override void Awake()
    {
        base.Awake();
        baseCooldown   = 0.85f;
        autoLockRange  = 11f;     // longer than Goblin
        attackSlowdown = 0.7f;    // archer can move while drawing
    }

    protected override void DoAttack(Vector2 origin, Vector2 aimDir)
    {
        EnsurePool();
        float dmg = ComputeDamage(_arrowDamage);
        var p = _pool.Get(origin, Quaternion.identity);
        p.Launch(
            origin, aimDir,
            _arrowSpeed, dmg, _arrowHitRadius + rangeAdd, _arrowMaxDist,
            pierceCount: 0,
            tint: new Color(0.95f, 0.85f, 0.6f, 1f),
            visualScale: 1f);
    }

    void EnsurePool()
    {
        if (_pool != null) return;
        _root = new GameObject("[SkeletonArrowPool]");
        _root.transform.SetParent(null);
        _pool = new DynamicPool<PlayerProjectile>(CreateArrow, 6);
    }

    PlayerProjectile CreateArrow()
    {
        var go = new GameObject("SkeletonArrow");
        go.transform.SetParent(_root.transform);
        go.transform.localScale = new Vector3(2.4f, 0.45f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = PlaceholderSprites.GetSquare(8);
        sr.color        = new Color(0.95f, 0.85f, 0.6f, 1f);
        sr.sortingOrder = 30000;
        var proj = go.AddComponent<PlayerProjectile>();
        proj.Configure(_pool, sr);
        return proj;
    }
}
