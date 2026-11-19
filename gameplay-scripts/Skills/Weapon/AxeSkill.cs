using UnityEngine;

public class AxeSkill : PhysicalLinkedSkillBase
{
    public override SkillId       Id          => SkillId.Axe;
    public override SkillCategory Category    => SkillCategory.Weapon;
    public override string        DisplayName => "Axe";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    float _damage       = 15f;
    float _maxDistance  = 4f;
    float _speed        = 7.5f;
    int   _bounces      = 0;
    float _bounceRange  = 4f;
    float _visualScale  = 1f;

    DynamicPool<AxeProjectile> _pool;
    Transform _root;

    int _activeAxes;

    GameObject     _readyMarker;
    SpriteRenderer _readyMarkerSR;
    float          _readyBobTime;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        _root = new GameObject("[AxePool]").transform;
        _root.SetParent(null);
        _pool = new DynamicPool<AxeProjectile>(CreateAxe, 2);
        EnsureReadyMarker();
    }

    AxeProjectile CreateAxe()
    {
        var go = new GameObject("Axe");
        go.transform.SetParent(_root);
        go.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        Sprite art = SkillArt.Instance != null ? SkillArt.Instance.weaponAxe : null;
        sr.sprite       = SkillArt.SpriteOrSquareFallback(art, 32);
        sr.color        = art != null ? Color.white : SkillColors.Axe;
        sr.sortingOrder = 30000;
        var p = go.AddComponent<AxeProjectile>();
        p.Configure(_pool, sr, OnAxeReturned);
        return p;
    }

    void EnsureReadyMarker()
    {
        if (_readyMarker != null || playerTransform == null) return;
        _readyMarker = new GameObject("AxeReadyMarker");
        _readyMarker.transform.SetParent(playerTransform, false);
        _readyMarker.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        _readyMarker.transform.localScale    = new Vector3(0.25f, 0.25f, 1f);
        _readyMarkerSR = _readyMarker.AddComponent<SpriteRenderer>();
        _readyMarkerSR.sprite       = PlaceholderSprites.GetSquare(32);
        _readyMarkerSR.color        = new Color(SkillColors.Axe.r, SkillColors.Axe.g, SkillColors.Axe.b, 0.65f);
        _readyMarkerSR.sortingOrder = 31000;
        _readyMarker.SetActive(false);
    }

    void OnAxeReturned()
    {
        _activeAxes = Mathf.Max(0, _activeAxes - 1);
    }

    int MaxConcurrent()
    {
        // Beast-agnostic: Physical T2 unlocks the second concurrent axe (matches the
        // Goblin double-swing behavior). Reading the stack via SkillManager keeps this
        // working for Skeleton/Slime/Golem/Zombie since they don't have a doubleSwing
        // field — only Goblin's claw does, but the player's intent ("I picked Physical T2")
        // is what should drive the cap.
        int max = 1;
        var mgr = SkillManager.Instance;
        if (mgr != null && mgr.GetTierStacks(SkillId.Physical, 2) >= 1) max += 1;
        return max;
    }

    protected override void OnSwingCast(Vector2 origin, Vector2 aimDir)
    {
        if (_activeAxes >= MaxConcurrent()) return;

        if (_activeAxes > 0)
        {
            float ang = 12f * Mathf.Deg2Rad * (_activeAxes % 2 == 0 ? 1f : -1f);
            float cs  = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            aimDir = new Vector2(aimDir.x * cs - aimDir.y * sn, aimDir.x * sn + aimDir.y * cs);
        }

        var axe = _pool.Get(origin, Quaternion.identity);
        _activeAxes++;
        axe.Launch(
            player: playerTransform,
            origin: origin,
            dir: aimDir,
            speed: _speed,
            damage: _damage,
            hitRadius: 0.45f * _visualScale,
            maxDistance: _maxDistance,
            bounces: _bounces,
            bounceRange: _bounceRange,
            visualScale: _visualScale * 0.6f,
            maxReturnDistance: _maxDistance * 2.5f
        );
    }

    public override void SkillTick(float dt)
    {
        base.SkillTick(dt);
        if (_readyMarker == null) EnsureReadyMarker();
        if (_readyMarker == null) return;

        bool show = Unlocked && _activeAxes == 0;
        if (_readyMarker.activeSelf != show) _readyMarker.SetActive(show);

        if (show)
        {
            _readyBobTime += dt;
            _readyMarker.transform.Rotate(0f, 0f, 180f * dt);
            float bob = 0.9f + Mathf.Sin(_readyBobTime * 4f) * 0.08f;
            _readyMarker.transform.localPosition = new Vector3(0f, bob, 0f);
        }
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — damage, throw speed, range per stack.
        _damage      = 15f + 7.5f * (t1 - 1);
        _speed       = 7.5f + 2f * (t1 - 1);
        _maxDistance = 4f  + 1f * (t1 - 1);

        // T2 — extra bounce per stack.
        _bounces = t2;

        // T3 — bigger axe + extra damage per stack.
        _visualScale = 1f + 0.30f * t3;
        _damage     += 10f * t3;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Damage / speed / range per stack",
        2 => "T2: +1 bounce per stack",
        3 => "T3: Bigger axe + extra damage per stack",
        _ => "",
    };
}
