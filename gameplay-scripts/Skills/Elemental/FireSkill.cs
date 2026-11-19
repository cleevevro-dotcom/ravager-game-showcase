using UnityEngine;

public class FireSkill : PhysicalLinkedSkillBase
{
    public override SkillId       Id          => SkillId.Fire;
    public override SkillCategory Category    => SkillCategory.Elemental;
    public override string        DisplayName => "Fire";
    public override SkillRarity   Rarity      => SkillRarity.Epic;

    DynamicPool<FireballProjectile> _pool;
    Transform _root;

    float _damage         = 4f;
    float _burnDps        = 5f;
    float _burnDuration   = 2f;
    float _projectileSize = 1f;
    bool  _explosive      = false;
    float _explodeRadius  = 0f;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        _root = new GameObject("[FireballPool]").transform;
        _root.SetParent(null);
        _pool = new DynamicPool<FireballProjectile>(CreateFireball, 8);
    }

    FireballProjectile CreateFireball()
    {
        var go = new GameObject("Fireball");
        go.transform.SetParent(_root);
        var sr = go.AddComponent<SpriteRenderer>();
        Sprite art = SkillArt.Instance != null ? SkillArt.FirstOr(SkillArt.Instance.elementalFire) : null;
        sr.sprite       = art != null ? art : PlaceholderSprites.GetCircle(48);
        sr.color        = art != null ? Color.white : SkillColors.Fire;
        sr.sortingOrder = 30000;
        var p = go.AddComponent<FireballProjectile>();
        p.Configure(_pool, sr);
        return p;
    }

    protected override void OnSwingCast(Vector2 origin, Vector2 aimDir)
    {
        var fb = _pool.Get(origin, Quaternion.identity);
        float hitRadius = 0.25f * _projectileSize;
        fb.Launch(
            origin, aimDir,
            speed: 9f,
            damage: _damage,
            hitRadius: hitRadius,
            lifetime: 2.2f,
            knockback: 0f,
            burnDps: _burnDps,
            burnDuration: _burnDuration,
            explode: _explosive,
            explodeRadius: _explodeRadius,
            visualScale: _projectileSize
        );
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — direct damage + burn ramp.
        _damage       = 4f + 1.5f * (t1 - 1);
        _burnDps      = 5f + 2.5f * (t1 - 1);
        _burnDuration = 2f + 0.30f * (t1 - 1);

        // T2 — projectile size grows.
        _projectileSize = 1f + 0.4f * t2;

        // T3 — first stack unlocks the explosion; further stacks grow the radius.
        _explosive     = t3 >= 1;
        _explodeRadius = t3 >= 1 ? 1.4f + 0.4f * (t3 - 1) : 0f;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Damage and burn per stack",
        2 => "T2: +Projectile size per stack",
        3 => "T3: Explodes on impact (bigger per stack)",
        _ => "",
    };
}
