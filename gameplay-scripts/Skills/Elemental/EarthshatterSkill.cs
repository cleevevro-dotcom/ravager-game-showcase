using UnityEngine;

/// <summary>
/// Linked attack skill: every player swing drops an earth crack at the swing point. The crack
/// ticks damage in a radius, slows (T2), and can stun (T3).
/// </summary>
public class EarthshatterSkill : PhysicalLinkedSkillBase
{
    public override SkillId       Id          => SkillId.Earthshatter;
    public override SkillCategory Category    => SkillCategory.Elemental;
    public override string        DisplayName => "Earthshatter";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    int _t1, _t2, _t3;
    GameObject _root;
    DynamicPool<EarthCrack> _pool;

    protected override void OnSwingCast(Vector2 origin, Vector2 aimDir)
    {
        if (_t1 < 1) return;
        EnsurePool();
        Vector2 spawn = origin + aimDir * 1f;

        float radius        = 0.9f + 0.10f * (_t1 - 1) + 0.30f * _t2;
        float dmgPerTick    = 1.4f + 0.6f * (_t1 - 1);
        float slowFactor    = _t2 >= 1 ? 0.7f - 0.05f * (_t2 - 1) : 1f;
        if (slowFactor < 0.4f) slowFactor = 0.4f;
        float slowDuration  = _t2 >= 1 ? 0.6f : 0f;
        float stunChance    = _t3 >= 1 ? 0.15f + 0.05f * (_t3 - 1) : 0f;
        float stunDur       = 0.6f + 0.10f * (_t3 - 1);
        float bonusOnStun   = _t3 >= 1 ? 4f + 1.5f * (_t3 - 1) : 0f;
        float lifetime      = 1.4f + 0.10f * _t1;

        var crack = _pool.Get(spawn, Quaternion.identity);
        crack.Launch(spawn, radius, dmgPerTick, slowFactor, slowDuration,
                     stunChance, stunDur, bonusOnStun, lifetime);
    }

    void EnsurePool()
    {
        if (_pool != null) return;
        _root = new GameObject("[EarthCrackPool]");
        _root.transform.SetParent(null);
        _pool = new DynamicPool<EarthCrack>(CreateCrack, 4);
    }

    EarthCrack CreateCrack()
    {
        var go = new GameObject("EarthCrack");
        go.transform.SetParent(_root.transform);
        var sr = go.AddComponent<SpriteRenderer>();
        Sprite art = SkillArt.Instance != null ? SkillArt.FirstOr(SkillArt.Instance.elementalEarthshatter) : null;
        sr.sprite       = art != null ? art : PlaceholderSprites.GetCircle(64);
        sr.color        = art != null ? Color.white : SkillColors.Earth;
        sr.sortingOrder = 5;          // ground-layer (below characters which start at YSortBase=20000)
        var c = go.AddComponent<EarthCrack>();
        c.Configure(_pool, sr);
        return c;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: Cracks tick damage in an area",
        2 => "T2: Bigger AoE + slow",
        3 => "T3: Chance to stun + bonus damage on stunned",
        _ => "",
    };
}
