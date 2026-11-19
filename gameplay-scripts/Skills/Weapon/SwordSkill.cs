using System.Collections.Generic;
using UnityEngine;

public class SwordSkill : PassiveSkillBase
{
    public override SkillId       Id          => SkillId.Sword;
    public override SkillCategory Category    => SkillCategory.Weapon;
    public override string        DisplayName => "Orbit Swords";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    const int MaxSwords = 6;

    float _orbitRadius = 2.3f;
    float _orbitSpeed  = 80f;
    float _damage      = 30f;
    float _knockback   = 8f;

    readonly List<OrbitSword> _swords = new List<OrbitSword>(MaxSwords);
    Transform _root;
    float _angle;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        interval = 0.01f;
    }

    public override void SkillTick(float dt)
    {
        base.SkillTick(dt);
        if (!Unlocked || playerTransform == null) return;
        _angle += _orbitSpeed * dt;
        if (_angle >= 360f) _angle -= 360f;

        Vector2 origin = playerTransform.position;
        for (int i = 0; i < _swords.Count; i++)
            _swords[i].Tick(origin, _angle, _orbitRadius);
    }

    protected override void PassiveFire() { /* unused; we tick per-frame */ }

    void EnsureSwords(int targetCount)
    {
        if (_root == null)
        {
            _root = new GameObject("[OrbitSwordRoot]").transform;
            _root.SetParent(null);
        }
        while (_swords.Count < targetCount)
        {
            var go = new GameObject("OrbitSword");
            go.transform.SetParent(_root);
            // Per brief: replace the placeholder with the new sword sprite and slightly enlarge
            // so the visual + hitbox read better. The shape is no longer a 0.45×0.12 sliver —
            // we render the actual sprite at a uniform scale so the rotation reads correctly.
            Sprite art = SkillArt.Instance != null ? SkillArt.Instance.weaponSword : null;
            go.transform.localScale = art != null ? new Vector3(0.75f, 0.75f, 1f)
                                                  : new Vector3(0.55f, 0.15f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = SkillArt.SpriteOrSquareFallback(art, 32);
            sr.color        = art != null ? Color.white : SkillColors.Sword;
            sr.sortingOrder = 30000;
            sr.flipY        = true;
            var sw = go.AddComponent<OrbitSword>();
            sw.damagePerHit = _damage;
            sw.knockback    = _knockback;
            _swords.Add(sw);
            RedistributeAngles();
        }
        while (_swords.Count > targetCount)
        {
            var last = _swords[_swords.Count - 1];
            _swords.RemoveAt(_swords.Count - 1);
            if (last != null) Destroy(last.gameObject);
            RedistributeAngles();
        }
        for (int i = 0; i < _swords.Count; i++)
        {
            _swords[i].damagePerHit = _damage;
            _swords[i].knockback    = _knockback;
            _swords[i].ResetHits();
        }
    }

    void RedistributeAngles()
    {
        int n = _swords.Count;
        if (n == 0) return;
        float step = 360f / n;
        for (int i = 0; i < n; i++) _swords[i].angleOffsetDeg = step * i;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — number of orbiting swords (cap 6) + damage scales with stack count.
        int count = Mathf.Clamp(t1, 0, MaxSwords);
        _damage   = 30f + 10f * t1;

        // T2 — orbit speed per stack, hard cap so visuals don't blur.
        _orbitSpeed = Mathf.Min(280f, 80f + 40f * t2);

        // T3 — bigger radius + heavier knockback, plus damage per stack.
        _orbitRadius = 2.3f + 0.40f * t3;
        _knockback   = 8f + 1f * t3;
        _damage     += 10f * t3;

        EnsureSwords(count);
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +1 orbit sword per stack (cap 6) + damage",
        2 => "T2: +Orbit speed per stack",
        3 => "T3: +Radius, knockback, and damage per stack",
        _ => "",
    };
}
