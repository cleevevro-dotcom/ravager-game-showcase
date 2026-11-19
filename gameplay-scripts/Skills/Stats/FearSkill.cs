using System.Collections.Generic;
using UnityEngine;

public class FearSkill : PassiveSkillBase
{
    public override SkillId       Id          => SkillId.Fear;
    public override SkillCategory Category    => SkillCategory.Bonus;
    public override string        DisplayName => "Fear";
    public override SkillRarity   Rarity      => SkillRarity.Rare;

    const int AuraSpriteSize = 128;

    float _radius         = 3.5f;
    float _stunChance     = 0.07f;
    float _stunDur        = 0.8f;
    float _bonusDmgOnStun = 0f;

    readonly List<EnemyBase> _hits = new List<EnemyBase>(32);

    GameObject     _auraGO;
    SpriteRenderer _auraSR;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        interval = 1f;
        EnsureAura();
    }

    void EnsureAura()
    {
        if (_auraGO != null) return;
        _auraGO = new GameObject("FearAura");
        _auraGO.transform.SetParent(playerTransform, false);
        _auraGO.transform.localPosition = Vector3.zero;
        _auraSR = _auraGO.AddComponent<SpriteRenderer>();
        _auraSR.sprite       = PlaceholderSprites.GetRing(AuraSpriteSize, 0.04f);
        _auraSR.color        = new Color(SkillColors.Fear.r, SkillColors.Fear.g, SkillColors.Fear.b, 0.30f);
        _auraSR.sortingOrder = 1;
        _auraGO.SetActive(false);
    }

    void LateUpdate()
    {
        if (_auraGO != null && playerTransform != null)
            _auraGO.transform.position = playerTransform.position;
    }

    void RefreshAuraVisual()
    {
        if (_auraGO == null) return;
        float scale = _radius * 2f / PlaceholderSprites.GetVisibleDiameterUnits(AuraSpriteSize);
        _auraGO.transform.localScale = new Vector3(scale, scale, 1f);
        _auraGO.SetActive(Unlocked);
    }

    protected override void PassiveFire()
    {
        Vector2 center = playerTransform.position;
        TargetingUtility.FillEnemiesInRadius(center, _radius, _hits);
        float radiusSqr = _radius * _radius;
        for (int i = 0; i < _hits.Count; i++)
        {
            var e = _hits[i];
            if ((e.Position - center).sqrMagnitude > radiusSqr) continue;

            float effChance = _stunChance * (1f - Mathf.Clamp01(e.fearResistance));
            if (effChance > 0f && Random.value < effChance)
            {
                e.ApplyStun(_stunDur);
                if (_bonusDmgOnStun > 0f) e.TakeDamage(_bonusDmgOnStun);
            }
            else if (e.fearResistance >= 0.99f && _bonusDmgOnStun > 0f)
            {
                e.TakeDamage(_bonusDmgOnStun * 0.5f);
            }
        }
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        // T1 — stun chance per stack.
        _stunChance = 0.07f + 0.03f * (t1 - 1);
        if (_stunChance < 0f) _stunChance = 0f;

        // T2 — bigger aura per stack.
        _radius = 3.5f + 0.4f * t2;

        // T3 — bonus damage when stunned, plus a chance bump.
        _bonusDmgOnStun = t3 >= 1 ? 3f + 1.5f * (t3 - 1) : 0f;
        if (t3 >= 1) _stunChance += 0.06f;

        RefreshAuraVisual();
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: +Stun chance per stack",
        2 => "T2: +Aura radius per stack",
        3 => "T3: Bonus damage on stunned foes (incl. bosses)",
        _ => "",
    };
}
