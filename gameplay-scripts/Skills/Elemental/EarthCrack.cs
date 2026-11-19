using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pooled ground crack dropped by EarthshatterSkill. Ticks damage in a radius and applies slow.
/// T3 stuns enemies inside a chance roll on tick.
/// </summary>
public class EarthCrack : MonoBehaviour
{
    const float TickInterval = 0.25f;

    SpriteRenderer _sr;
    SpriteRenderer _brownDisc;  // dark brown impact ring rendered BELOW the earthshatter sprite
    DynamicPool<EarthCrack> _pool;

    float _radius;
    float _dmgPerTick;
    float _slowFactor;
    float _slowDuration;
    float _stunChance;
    float _stunDur;
    float _bonusDmgOnStun;
    float _spawnTime;
    float _lifetime;
    float _nextTick;

    readonly List<ISpatialEntity> _hits = new List<ISpatialEntity>(16);

    public void Configure(DynamicPool<EarthCrack> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
        BuildBrownDisc();
    }

    void BuildBrownDisc()
    {
        if (_brownDisc != null) return;
        // Dark brown impact ring rendered UNDER the earthshatter sprite so the
        // skill reads as "ground cracked open" rather than a bright icon floating
        // on its own. Sorting order strictly below _sr (which sits at 5).
        var go = new GameObject("BrownDisc");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;
        _brownDisc = go.AddComponent<SpriteRenderer>();
        _brownDisc.sprite = PlaceholderSprites.GetCircle(64);
        _brownDisc.color  = new Color(0.27f, 0.16f, 0.08f, 0.55f);  // dark brown, semi-transparent
        _brownDisc.sortingOrder = (_sr != null ? _sr.sortingOrder : 5) - 1;
    }

    public void Launch(
        Vector2 position, float radius, float dmgPerTick,
        float slowFactor, float slowDuration,
        float stunChance, float stunDur, float bonusDmgOnStun,
        float lifetime)
    {
        transform.position = position;
        _radius         = radius;
        _dmgPerTick     = dmgPerTick;
        _slowFactor     = slowFactor;
        _slowDuration   = slowDuration;
        _stunChance     = stunChance;
        _stunDur        = stunDur;
        _bonusDmgOnStun = bonusDmgOnStun;
        _spawnTime      = Time.time;
        _lifetime       = lifetime;
        _nextTick       = Time.time;

        if (_sr != null)
        {
            float scale = radius * 2f / PlaceholderSprites.GetVisibleDiameterUnits(64);
            transform.localScale = new Vector3(scale, scale, 1f);
            var c = SkillColors.Earth; c.a = 0.55f;
            _sr.color = c;
        }
        if (_brownDisc != null)
        {
            // Brown disc starts opaque, fades in lockstep with _sr inside Update().
            var bc = _brownDisc.color; bc.a = 0.55f; _brownDisc.color = bc;
        }
    }

    void Update()
    {
        float elapsed = Time.time - _spawnTime;
        if (elapsed >= _lifetime) { _pool?.Return(this); return; }

        if (_sr != null)
        {
            float k = 1f - elapsed / _lifetime;
            var c = _sr.color;
            c.a = Mathf.Lerp(0.10f, 0.55f, k);
            _sr.color = c;
            if (_brownDisc != null)
            {
                var bc = _brownDisc.color;
                bc.a = Mathf.Lerp(0.05f, 0.55f, k);
                _brownDisc.color = bc;
            }
        }

        if (Time.time < _nextTick) return;
        _nextTick += TickInterval;

        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _hits.Clear();
        hash.Query(SpatialCategory.Enemy, transform.position, _radius, _hits);
        float r2 = _radius * _radius;
        for (int i = 0; i < _hits.Count; i++)
        {
            if (!(_hits[i] is EnemyBase e) || e.IsDead) continue;
            if ((e.Position - (Vector2)transform.position).sqrMagnitude > r2) continue;

            e.TakeDamage(_dmgPerTick);
            if (_slowFactor < 1f && _slowDuration > 0f) e.ApplySlow(_slowFactor, _slowDuration);
            if (_stunChance > 0f && Random.value < _stunChance)
            {
                e.ApplyStun(_stunDur);
                if (_bonusDmgOnStun > 0f) e.TakeDamage(_bonusDmgOnStun);
            }
        }

        // Ground crack is genuine AoE — also chip at any building/tower whose obstacle box
        // overlaps the radius. Towers can be cracked open from inside an Earthshatter zone.
        DestructibleDamageUtility.DamageInRadius(transform.position, _radius, _dmgPerTick);
    }
}
