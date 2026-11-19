using UnityEngine;

public class PlagueSkill : PhysicalLinkedSkillBase
{
    public override SkillId       Id          => SkillId.Plague;
    public override SkillCategory Category    => SkillCategory.Elemental;
    public override string        DisplayName => "Plague";
    public override SkillRarity   Rarity      => SkillRarity.Legendary;

    const float TargetRange = 7f;

    DynamicPool<PlaguePuddle> _pool;
    Transform _root;

    float _radius        = 1.2f;
    float _dps           = 6f;
    float _slowFactor    = 0.7f;
    float _slowDuration  = 0.6f;
    float _lifetime      = 1f;
    float _rampDpsPerSec = 0f;
    float _rampDpsCap    = 0f;
    float _knockback     = 1.0f;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        _root = new GameObject("[PlaguePool]").transform;
        _root.SetParent(null);
        _pool = new DynamicPool<PlaguePuddle>(CreatePuddle, 6);
    }

    PlaguePuddle CreatePuddle()
    {
        var go = new GameObject("PlaguePuddle");
        go.transform.SetParent(_root);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = PlaceholderSprites.GetCircle(64);
        sr.color        = SkillColors.Plague;
        sr.sortingOrder = 5;
        var p = go.AddComponent<PlaguePuddle>();
        p.Configure(_pool, sr);
        return p;
    }

    protected override void OnSwingCast(Vector2 origin, Vector2 aimDir)
    {
        Vector2 spawnPos;
        var target = TargetingUtility.FindNearestEnemy(origin, TargetRange);
        if (target != null) spawnPos = target.Position;
        else                spawnPos = origin + aimDir * 2.5f;

        var puddle = _pool.Get(spawnPos, Quaternion.identity);
        puddle.Launch(spawnPos, _radius, _dps, _slowFactor, _slowDuration,
                      _knockback, _lifetime, _rampDpsPerSec, _rampDpsCap);
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — base DPS + lifetime per stack.
        _dps      = 6f + 1.8f * (t1 - 1);
        _lifetime = 1f + 0.20f * (t1 - 1);

        // T2 — radius + dwell ramp.
        _radius        = 1.2f + 0.25f * t2;
        _slowFactor    = Mathf.Max(0.45f, 0.7f - 0.05f * t2);
        _slowDuration  = 0.6f + 0.10f * t2;
        _rampDpsPerSec = t2 >= 1 ? 4f + 1.5f * (t2 - 1) : 0f;
        _rampDpsCap    = t2 >= 1 ? 18f + 3f  * (t2 - 1) : 0f;

        // T3 — flat damage multiplier per stack.
        if (t3 >= 1) _dps *= 1f + 0.40f * t3;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Puddle DPS & lifetime per stack",
        2 => "T2: +Radius & dwell-time ramp per stack",
        3 => "T3: ×40% DPS per stack",
        _ => "",
    };
}
