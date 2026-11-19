using System.Collections.Generic;
using UnityEngine;

public class FreezeSkill : PassiveSkillBase
{
    public override SkillId       Id          => SkillId.Freeze;
    public override SkillCategory Category    => SkillCategory.Elemental;
    public override string        DisplayName => "Freeze";
    public override SkillRarity   Rarity      => SkillRarity.Epic;

    const int RingSpriteSize = 96;
    const int DiscSpriteSize = 64;

    const float PulseDuration = 0.25f;
    const float DarkAlpha     = 0.10f;
    const float LightAlpha    = 0.35f;

    GameObject     _auraGO;
    SpriteRenderer _auraSR;
    SpriteRenderer _discSR;

    float _radius        = 3f;
    float _damagePerTick = 1.4f;
    float _slowFactor    = 0.7f;
    float _slowDur       = 0.8f;
    float _stunChance    = 0f;
    float _stunDur       = 0.6f;
    float _lastPulseTime = -999f;

    readonly List<EnemyBase> _hits = new List<EnemyBase>(32);

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        interval = 0.55f;
        EnsureAura();
        RefreshAuraVisual();
        if (_auraGO != null) _auraGO.SetActive(false);
    }

    void EnsureAura()
    {
        if (_auraGO != null) return;
        _auraGO = new GameObject("FreezeAura");
        _auraGO.transform.SetParent(playerTransform, worldPositionStays: false);
        _auraGO.transform.localPosition = Vector3.zero;

        _auraSR = _auraGO.AddComponent<SpriteRenderer>();
        _auraSR.sprite       = PlaceholderSprites.GetRing(RingSpriteSize, 0.08f);
        _auraSR.color        = new Color(SkillColors.Ice.r, SkillColors.Ice.g, SkillColors.Ice.b, 0.55f);
        _auraSR.sortingOrder = 1;

        var discGO = new GameObject("FreezeDisc");
        discGO.transform.SetParent(_auraGO.transform, false);
        discGO.transform.localPosition = Vector3.zero;
        _discSR = discGO.AddComponent<SpriteRenderer>();
        _discSR.sprite       = PlaceholderSprites.GetCircle(DiscSpriteSize);
        _discSR.color        = new Color(SkillColors.Ice.r, SkillColors.Ice.g, SkillColors.Ice.b, DarkAlpha);
        _discSR.sortingOrder = 0;
    }

    void RefreshAuraVisual()
    {
        if (_auraGO == null) return;
        float ringScale = _radius * 2f / PlaceholderSprites.GetVisibleDiameterUnits(RingSpriteSize);
        _auraGO.transform.localScale = new Vector3(ringScale, ringScale, 1f);

        if (_discSR != null)
        {
            float discWorld = _radius * 2f / PlaceholderSprites.GetVisibleDiameterUnits(DiscSpriteSize);
            float local     = ringScale > 0.0001f ? discWorld / ringScale : discWorld;
            _discSR.transform.localScale = new Vector3(local, local, 1f);
        }

        _auraGO.SetActive(Unlocked);
    }

    void LateUpdate()
    {
        if (_auraGO != null && playerTransform != null)
            _auraGO.transform.position = playerTransform.position;

        if (_discSR != null)
        {
            float t = Mathf.Clamp01((Time.time - _lastPulseTime) / PulseDuration);
            float a = Mathf.Lerp(LightAlpha, DarkAlpha, t);
            var c = _discSR.color; c.a = a;
            _discSR.color = c;
        }
    }

    protected override void PassiveFire()
    {
        Vector2 origin = playerTransform.position;
        TargetingUtility.FillEnemiesInRadius(origin, _radius, _hits);
        float radiusSqr = _radius * _radius;
        bool dealtDamage = false;
        for (int i = 0; i < _hits.Count; i++)
        {
            var e = _hits[i];
            if ((e.Position - origin).sqrMagnitude > radiusSqr) continue;

            e.TakeDamage(_damagePerTick);
            e.ApplySlow(_slowFactor, _slowDur);
            if (_stunChance > 0f && Random.value < _stunChance)
                e.ApplyStun(_stunDur);
            dealtDamage = true;
        }

        if (dealtDamage) _lastPulseTime = Time.time;
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — damage tick + slow strength.
        _damagePerTick = 1.4f + 0.6f * (t1 - 1);
        _slowFactor    = Mathf.Max(0.30f, 0.7f - 0.06f * (t1 - 1));

        // T2 — bigger aura per stack.
        _radius = 3f + 0.6f * t2;

        // T3 — chance to freeze-stun, with longer stun per stack.
        _stunChance = t3 >= 1 ? 0.08f + 0.04f * (t3 - 1) : 0f;
        _stunDur    = 0.6f + 0.10f * (t3 - 1);
        if (_stunDur < 0.6f) _stunDur = 0.6f;

        RefreshAuraVisual();
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Aura damage & slow per stack",
        2 => "T2: +Aura radius per stack",
        3 => "T3: Freeze-stun chance (stronger per stack)",
        _ => "",
    };
}
