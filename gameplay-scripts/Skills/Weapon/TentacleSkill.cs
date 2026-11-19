using System.Collections.Generic;
using UnityEngine;

public class TentacleSkill : PassiveSkillBase
{
    public override SkillId       Id          => SkillId.Tentacle;
    public override SkillCategory Category    => SkillCategory.Weapon;
    public override string        DisplayName => "Tentacles";
    public override SkillRarity   Rarity      => SkillRarity.Epic;

    const float TargetRange = 8f;

    int   _simultaneous  = 1;
    float _strikeDamage  = 20f;
    float _hitRadius     = 2f;
    float _knockback     = 10f;

    DynamicPool<TentacleInstance> _pool;
    Transform _root;

    readonly List<EnemyBase> _candidates = new List<EnemyBase>(32);
    readonly HashSet<EnemyBase> _claimed = new HashSet<EnemyBase>();

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        interval = 4.5f;
        _root = new GameObject("[TentaclePool]").transform;
        _root.SetParent(null);
        _pool = new DynamicPool<TentacleInstance>(CreateTentacle, 6);
    }

    TentacleInstance CreateTentacle()
    {
        var go = new GameObject("Tentacle");
        go.transform.SetParent(_root);
        go.transform.localScale = new Vector3(1.1f, 0.35f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        Sprite art = SkillArt.Instance != null ? SkillArt.FirstOr(SkillArt.Instance.weaponTentacles) : null;
        sr.sprite       = SkillArt.SpriteOrSquareFallback(art, 32);
        sr.color        = art != null ? Color.white : SkillColors.Tentacle;
        sr.sortingOrder = 30000;
        var t = go.AddComponent<TentacleInstance>();
        t.Configure(_pool, sr);
        return t;
    }

    protected override void PassiveFire()
    {
        Vector2 origin = playerTransform.position;
        TargetingUtility.FillEnemiesInRadius(origin, TargetRange, _candidates);
        _claimed.Clear();

        for (int i = 0; i < _simultaneous; i++)
        {
            EnemyBase target = NearestUnclaimed(origin);
            Vector2 spawnPos;
            if (target != null)
            {
                _claimed.Add(target);
                spawnPos = target.Position;
            }
            else
            {
                float ang = Mathf.PI * 2f * (i / (float)Mathf.Max(1, _simultaneous));
                spawnPos = origin + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 2.2f;
            }

            var t = _pool.Get(spawnPos, Quaternion.identity);
            t.Launch(spawnPos, 0f, _strikeDamage, _hitRadius, _knockback);
        }
    }

    EnemyBase NearestUnclaimed(Vector2 origin)
    {
        EnemyBase best = null;
        float bestSqr = float.MaxValue;
        for (int i = 0; i < _candidates.Count; i++)
        {
            var e = _candidates[i];
            if (e == null || e.IsDead || _claimed.Contains(e)) continue;
            float sqr = ((Vector2)e.Position - origin).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = e; }
        }
        return best;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — one tentacle per stack (cap 6 to keep visuals readable).
        _simultaneous = Mathf.Clamp(t1, 1, 6);

        // T2 — damage per stack.
        _strikeDamage = 20f + 20f * t2;

        // T3 — cooldown shortens per stack, floor 1.5s so it doesn't trivialize.
        interval = Mathf.Max(1f, 3f - 0.7f * t3);
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +1 tentacle per stack",
        2 => "T2: +Damage per stack",
        3 => "T3: Faster cooldown per stack",
        _ => "",
    };
}
