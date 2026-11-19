using UnityEngine;

/// <summary>
/// Linked attack skill: every player swing fires a wind blade in the aim direction. Pierces with
/// T3 stacks. Same pooled <see cref="PlayerProjectile"/> the beast attacks use.
/// </summary>
public class WindCutterSkill : PhysicalLinkedSkillBase
{
    public override SkillId       Id          => SkillId.WindCutter;
    public override SkillCategory Category    => SkillCategory.Elemental;
    public override string        DisplayName => "Wind Cutter";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    int _t1, _t2, _t3;
    GameObject _root;
    DynamicPool<PlayerProjectile> _pool;

    protected override void OnSwingCast(Vector2 origin, Vector2 aimDir)
    {
        if (_t1 < 1) return;
        EnsurePool();

        // Per brief: base Wind got a small buff — bigger size and more knockback at the
        // starting tier so it reads better with the new sprite art.
        float damage    = 6f + 2f * (_t1 - 1) + 3f * _t3;
        float speed     = 9f + 1.5f * _t2;
        float maxDist   = 5.2f + 0.6f * _t2;        // was 4.5f + 0.5f
        float knockback = 3.0f + 0.8f * _t2;        // was 1.5f + 0.6f
        int   pierce    = _t3 >= 1 ? Mathf.Min(4, 1 + (_t3 - 1)) : 0;

        var blade = _pool.Get(origin, Quaternion.identity);
        blade.Launch(
            origin, aimDir,
            speed, damage, 0.65f, maxDist,          // hit radius bumped to match larger sprite
            pierceCount: pierce,
            knockback: knockback,
            tint: SkillColors.Wind,
            visualScale: 1.4f + 0.12f * _t2);        // was 1f + 0.1f
    }

    void EnsurePool()
    {
        if (_pool != null) return;
        _root = new GameObject("[WindBladePool]");
        _root.transform.SetParent(null);
        _pool = new DynamicPool<PlayerProjectile>(CreateBlade, 4);
    }

    PlayerProjectile CreateBlade()
    {
        var go = new GameObject("WindBlade");
        go.transform.SetParent(_root.transform);
        // Wider visual for the new Wind art; the projectile auto-scales further per OnSwingCast.
        Sprite art = SkillArt.Instance != null ? SkillArt.Instance.elementalWind : null;
        go.transform.localScale = art != null ? new Vector3(1f, 1f, 1f)
                                              : new Vector3(1.2f, 0.35f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = SkillArt.SpriteOrSquareFallback(art, 8);
        sr.color        = art != null ? Color.white : SkillColors.Wind;
        sr.sortingOrder = 30000;
        var proj = go.AddComponent<PlayerProjectile>();
        proj.Configure(_pool, sr);
        return proj;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: Swings release a wind blade",
        2 => "T2: +Range, speed, knockback",
        3 => "T3: Blades pierce enemies",
        _ => "",
    };
}
