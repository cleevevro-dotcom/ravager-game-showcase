using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bonus passive: every swing has a random chance to apply a random bonus effect on enemies in
/// front of the player. T2 ups the chance and effect potency; T3 lets multiple effects roll on
/// the same hit.
/// </summary>
public class ChaosSurgeSkill : SkillBase
{
    public override SkillId       Id          => SkillId.ChaosSurge;
    public override SkillCategory Category    => SkillCategory.Bonus;
    public override string        DisplayName => "Chaos Surge";
    public override SkillRarity   Rarity      => SkillRarity.Epic;

    int _t1, _t2, _t3;
    IPlayerAttack[] _attacks;
    readonly List<EnemyBase> _hits = new List<EnemyBase>(8);

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        Unsubscribe();
        _attacks = player != null ? player.GetComponents<IPlayerAttack>() : null;
        if (_attacks != null)
            for (int i = 0; i < _attacks.Length; i++)
                _attacks[i].OnSwing += HandleSwing;
    }

    void Unsubscribe()
    {
        if (_attacks == null) return;
        for (int i = 0; i < _attacks.Length; i++)
            if (_attacks[i] != null) _attacks[i].OnSwing -= HandleSwing;
    }

    void OnDestroy() => Unsubscribe();

    void HandleSwing(Vector2 origin, Vector2 dir)
    {
        if (!Unlocked) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        float chance = 0.10f + 0.05f * (_t1 - 1) + 0.10f * _t2;
        if (chance <= 0f) return;

        Vector2 center = origin + dir * 1f;
        TargetingUtility.FillEnemiesInRadius(center, 1.5f, _hits);
        int rolls = _t3 >= 1 ? 1 + Random.Range(0, _t3 + 1) : 1;
        float power = 1f + 0.30f * _t2;

        for (int i = 0; i < _hits.Count; i++)
        {
            var e = _hits[i];
            if (Random.value > chance) continue;
            for (int k = 0; k < rolls; k++) ApplyRandomEffect(e, power);
        }
    }

    void ApplyRandomEffect(EnemyBase e, float power)
    {
        if (e == null || e.IsDead) return;
        int kind = Random.Range(0, 5);
        switch (kind)
        {
            case 0: e.TakeDamage(8f * power); break;
            case 1: e.ApplyBurn(3f * power, 2f); break;
            case 2: e.ApplySlow(Mathf.Max(0.3f, 0.6f - 0.05f * _t2), 1.5f); break;
            case 3: e.ApplyStun(0.5f * power); break;
            case 4: KnockbackUtility.ApplyRadial(e, e.Position, 4f * power); break;
        }
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        _t1 = t1; _t2 = t2; _t3 = t3;
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: Random bonus effect chance on hit",
        2 => "T2: Higher chance + stronger effects",
        3 => "T3: Multiple effects can trigger at once",
        _ => "",
    };
}
