using UnityEngine;

/// <summary>
/// Slime mid-range AoE spit. Slow projectile that explodes on contact for moderate radius
/// damage. Faster cooldown than Goblin to reflect "faster movement / faster cast".
/// </summary>
public class SlimeSpitAttack : BaseBeastAttack
{
    [Header("Slime Spit")]
    [SerializeField] float _spitSpeed     = 7f;
    [SerializeField] float _spitDamage    = 7f;
    [SerializeField] float _spitHitRadius = 0.55f;
    [SerializeField] float _spitMaxDist   = 5.5f;
    [SerializeField] float _spitAoeRadius = 1.2f;

    GameObject _root;
    DynamicPool<PlayerProjectile> _pool;

    protected override void Awake()
    {
        base.Awake();
        baseCooldown   = 0.75f;   // slime is twitchier
        autoLockRange  = 6.5f;
        attackSlowdown = 0.65f;
    }

    protected override void DoAttack(Vector2 origin, Vector2 aimDir)
    {
        EnsurePool();
        float dmg = ComputeDamage(_spitDamage);
        var p = _pool.Get(origin, Quaternion.identity);
        p.Launch(
            origin, aimDir,
            _spitSpeed, dmg, _spitHitRadius + rangeAdd, _spitMaxDist,
            pierceCount: 0,
            slowFactor: 0.75f, slowDuration: 0.4f,
            aoeRadius: _spitAoeRadius + rangeAdd * 0.5f,
            tint: SkillColors.Plague,
            visualScale: 0.9f);
    }

    void EnsurePool()
    {
        if (_pool != null) return;
        _root = new GameObject("[SlimeSpitPool]");
        _root.transform.SetParent(null);
        _pool = new DynamicPool<PlayerProjectile>(CreateBlob, 6);
    }

    PlayerProjectile CreateBlob()
    {
        var go = new GameObject("SlimeSpit");
        go.transform.SetParent(_root.transform);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = PlaceholderSprites.GetCircle(48);
        sr.color        = SkillColors.Plague;
        sr.sortingOrder = 30000;
        var proj = go.AddComponent<PlayerProjectile>();
        proj.Configure(_pool, sr);
        return proj;
    }
}
