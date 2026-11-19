using System.Collections.Generic;
using UnityEngine;

public class LightningSkill : PhysicalLinkedSkillBase
{
    public override SkillId       Id          => SkillId.Lightning;
    public override SkillCategory Category    => SkillCategory.Elemental;
    public override string        DisplayName => "Lightning";
    public override SkillRarity   Rarity      => SkillRarity.Epic;

    const float InitialRange = 7f;
    const float ChainRange   = 1.6f;

    int   _chainCount = 2;
    float _damage     = 6f;
    float _skyChance  = 0f;

    DynamicPool<LightningBolt> _boltPool;
    Transform _root;

    readonly HashSet<EnemyBase> _alreadyHit = new HashSet<EnemyBase>();
    readonly List<EnemyBase>    _candidates = new List<EnemyBase>(32);

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        _root = new GameObject("[LightningBoltPool]").transform;
        _root.SetParent(null);
        _boltPool = new DynamicPool<LightningBolt>(CreateBolt, 12);
    }

    LightningBolt CreateBolt()
    {
        var go = new GameObject("LightningBolt");
        go.transform.SetParent(_root);
        var sr = go.AddComponent<SpriteRenderer>();
        Sprite art = SkillArt.Instance != null ? SkillArt.FirstOr(SkillArt.Instance.elementalLightning) : null;
        sr.sprite       = SkillArt.SpriteOrSquareFallback(art, 32);
        sr.color        = art != null ? Color.white : SkillColors.Lightning;
        sr.sortingOrder = 30000;
        var b = go.AddComponent<LightningBolt>();
        b.Configure(_boltPool, sr);
        return b;
    }

    protected override void OnSwingCast(Vector2 origin, Vector2 aimDir)
    {
        var first = TargetingUtility.FindNearestEnemy(origin, InitialRange);
        if (first == null)
        {
            // No enemy in range — let the bolt strike a nearby destructible (tower/house)
            // so the player can still chip away at structures from a safe distance. Per-skill
            // rule: Lightning prefers enemies; falls back to buildings only when nothing else
            // is around.
            var d = DestructibleDamageUtility.FindNearest(origin, InitialRange);
            if (d == null) return;
            ShowBolt(origin, d.Position);
            d.TakeDamage(_damage);
            return;
        }

        _alreadyHit.Clear();
        ChainFrom(origin, first, _chainCount);

        if (_skyChance > 0f && Random.value < _skyChance)
        {
            var skyTarget = TargetingUtility.FindNearestEnemy(origin, InitialRange * 1.4f);
            if (skyTarget != null)
            {
                Vector2 skyFrom = (Vector2)skyTarget.Position + new Vector2(0f, 6f);
                _alreadyHit.Clear();
                ChainFrom(skyFrom, skyTarget, _chainCount);
            }
        }
    }

    void ChainFrom(Vector2 from, EnemyBase first, int remainingChains)
    {
        EnemyBase current = first;
        Vector2 fromPos   = from;
        int chains = 0;
        while (current != null && chains < remainingChains + 1)
        {
            ShowBolt(fromPos, current.Position);
            current.TakeDamage(_damage);
            _alreadyHit.Add(current);

            fromPos = current.Position;
            current = FindNextChain(fromPos);
            chains++;
        }
    }

    void ShowBolt(Vector2 from, Vector2 to)
    {
        var b = _boltPool.Get(Vector3.zero, Quaternion.identity);
        b.Show(from, to, 0.8f, SkillColors.Lightning);
    }

    EnemyBase FindNextChain(Vector2 origin)
    {
        TargetingUtility.FillEnemiesInRadius(origin, ChainRange, _candidates);
        EnemyBase best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < _candidates.Count; i++)
        {
            var e = _candidates[i];
            if (_alreadyHit.Contains(e)) continue;
            float sqr = ((Vector2)e.Position - origin).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = e; }
        }
        return best;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — base damage per stack.
        _damage = 6f + 2f * (t1 - 1);

        // T2 — extra chain link per stack.
        _chainCount = 2 + t2;

        // T3 — sky strike chance scales per stack.
        _skyChance = t3 >= 1 ? 0.20f + 0.10f * (t3 - 1) : 0f;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Damage per stack",
        2 => "T2: +1 chain link per stack",
        3 => "T3: Chance to call a sky strike",
        _ => "",
    };
}
