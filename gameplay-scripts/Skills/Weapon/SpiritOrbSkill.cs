using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weapon passive: orbiting orbs cycle between idle orbit and reach-out attacks. Reuses
/// PassiveSkillBase tick rate but does its own per-frame angle integration so orbits look smooth.
/// T1 = orb count, T2 = fly speed, T3 = damage.
/// </summary>
public class SpiritOrbSkill : PassiveSkillBase
{
    public override SkillId       Id          => SkillId.SpiritOrb;
    public override SkillCategory Category    => SkillCategory.Weapon;
    public override string        DisplayName => "Spirit Orb";
    public override SkillRarity   Rarity      => SkillRarity.Epic;

    const int MaxOrbs = 14;

    readonly List<SpiritOrbInstance> _orbs = new List<SpiritOrbInstance>(MaxOrbs);
    Transform _root;
    float _angle;

    // Targeting coordination: orbs claim a target on their way out and release it on hit/death,
    // so 14 orbs no longer all converge on the single nearest enemy. The candidate buffer is
    // refreshed at most once per frame (multiple orbs ticking on the same frame share the query).
    readonly HashSet<EnemyBase> _claimedTargets = new HashSet<EnemyBase>(MaxOrbs);
    readonly List<EnemyBase>    _candidateBuf   = new List<EnemyBase>(32);
    int _candidateFrame = -1;

    int   _t1, _t2, _t3;
    float _orbitRadius = 1.8f;
    float _orbitSpeed  = 70f;
    float _flySpeed    = 7.5f;
    float _damage      = 18f;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        interval = 0.01f;
    }

    protected override void PassiveFire() { /* unused — driven by SkillTick */ }

    public override void SkillTick(float dt)
    {
        if (!Unlocked || playerTransform == null) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        _angle += _orbitSpeed * dt;
        if (_angle >= 360f) _angle -= 360f;

        for (int i = 0; i < _orbs.Count; i++)
        {
            var o = _orbs[i];
            if (o == null) continue;
            o.flySpeed       = _flySpeed;
            o.damage         = _damage;
            o.orbitRadius    = _orbitRadius;
            o.Tick(dt, _angle);
        }
    }

    void EnsureOrbs(int targetCount)
    {
        if (_root == null)
        {
            _root = new GameObject("[SpiritOrbRoot]").transform;
            _root.SetParent(null);
        }
        while (_orbs.Count < targetCount)
        {
            var go = new GameObject("SpiritOrb");
            go.transform.SetParent(_root);
            var sr = go.AddComponent<SpriteRenderer>();
            Sprite art = SkillArt.Instance != null ? SkillArt.Instance.weaponSpiritOrb : null;
            sr.sprite       = art != null ? art : PlaceholderSprites.GetCircle(32);
            sr.color        = SkillColors.SpiritOrb;
            sr.sortingOrder = 30000;
            var orb = go.AddComponent<SpiritOrbInstance>();
            orb.Bind(playerTransform, this);
            _orbs.Add(orb);
            RedistributeAngles();
        }
        while (_orbs.Count > targetCount)
        {
            var last = _orbs[_orbs.Count - 1];
            _orbs.RemoveAt(_orbs.Count - 1);
            if (last != null) Destroy(last.gameObject);
            RedistributeAngles();
        }
    }

    void RedistributeAngles()
    {
        int n = _orbs.Count;
        if (n == 0) return;
        float step = 360f / n;
        for (int i = 0; i < n; i++) _orbs[i].angleOffsetDeg = step * i;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;

        int count = Mathf.Clamp(t1 * 2, 0, MaxOrbs);
        _flySpeed = 7.5f + 2f * t2;
        _damage   = 18f + 18f * t3;

        EnsureOrbs(count);
    }

    /// <summary>
    /// Pick a live nearby enemy that no other orb has already claimed. When all candidates are
    /// claimed (e.g. only one or two enemies in range), falls back to a random claimed candidate
    /// so orbs still hit something — multiple orbs converging on a boss is intentional.
    /// </summary>
    public EnemyBase AcquireTarget(Vector2 origin, float searchRange)
    {
        if (_candidateFrame != Time.frameCount)
        {
            TargetingUtility.FillEnemiesInRadius(origin, searchRange, _candidateBuf);
            _candidateFrame = Time.frameCount;
        }
        if (_candidateBuf.Count == 0) return null;

        EnemyBase best  = null;
        float bestSqr   = float.MaxValue;
        for (int i = 0; i < _candidateBuf.Count; i++)
        {
            var e = _candidateBuf[i];
            if (e == null || e.IsDead) continue;
            if (_claimedTargets.Contains(e)) continue;
            float sqr = ((Vector2)e.Position - origin).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = e; }
        }

        if (best == null)
        {
            // All in-range enemies are already claimed. Pick a random live one so orbs don't
            // sit idle when there's only a single target (boss / last survivor).
            for (int tries = 0; tries < _candidateBuf.Count; tries++)
            {
                var e = _candidateBuf[Random.Range(0, _candidateBuf.Count)];
                if (e != null && !e.IsDead) { best = e; break; }
            }
            if (best == null) return null;
        }

        _claimedTargets.Add(best);
        return best;
    }

    /// <summary>Called by an orb when it hits, gives up on a dead target, or is disabled.</summary>
    public void ReleaseTarget(EnemyBase t)
    {
        if (t == null) return;
        _claimedTargets.Remove(t);
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +2 orbiting orb per stack (cap 14)",
        2 => "T2: Faster projectile fly speed",
        3 => "T3: Extra damage per orb hit",
        _ => "",
    };
}
