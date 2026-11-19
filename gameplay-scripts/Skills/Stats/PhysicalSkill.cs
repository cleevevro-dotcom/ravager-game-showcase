using UnityEngine;

public class PhysicalSkill : SkillBase
{
    public override SkillId       Id          => SkillId.Physical;
    public override SkillCategory Category    => SkillCategory.Stat;
    public override string        DisplayName => "Physical";
    public override SkillRarity   Rarity      => SkillRarity.Common;

    IPlayerAttack[] _attacks;

    public override void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        base.Bind(stats, player, spatialMgr);
        if (player != null) _attacks = player.GetComponents<IPlayerAttack>();
    }

    protected override void OnStacksChanged(int t1, int t2, int t3)
    {
        if ((_attacks == null || _attacks.Length == 0) && playerTransform != null)
            _attacks = playerTransform.GetComponents<IPlayerAttack>();
        if (_attacks == null) return;
        for (int i = 0; i < _attacks.Length; i++)
            _attacks[i]?.ApplyPhysicalStacks(t1, t2, t3);
    }

    public override string GetTierDescription(int tier) => tier switch
    {
        1 => "T1: Faster swings + a bit of damage",
        2 => "T2: Double-swing in front, wider radius",
        3 => "T3: Strikes behind + sides, more damage",
        _ => "",
    };
}
