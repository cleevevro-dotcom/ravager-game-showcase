using UnityEngine;

public abstract class SkillBase : MonoBehaviour, ISkill
{
    protected PlayerStats        playerStats;
    protected Transform          playerTransform;
    protected SpatialHashManager spatial;

    public int T1Stacks { get; private set; }
    public int T2Stacks { get; private set; }
    public int T3Stacks { get; private set; }

    public int  Level    => T1Stacks + T2Stacks + T3Stacks;
    public bool Unlocked => T1Stacks > 0;

    public abstract SkillId       Id          { get; }
    public abstract SkillCategory Category    { get; }
    public abstract string        DisplayName { get; }
    public virtual  SkillRarity   Rarity      => SkillRarity.Common;

    public virtual void Bind(PlayerStats stats, Transform player, SpatialHashManager spatialMgr)
    {
        playerStats     = stats;
        playerTransform = player;
        spatial         = spatialMgr;
    }

    public void ApplyStacks(int t1, int t2, int t3)
    {
        T1Stacks = t1;
        T2Stacks = t2;
        T3Stacks = t3;
        OnStacksChanged(t1, t2, t3);
    }

    /// <summary>Short description of what one stack of the given tier (1..3) adds.</summary>
    public abstract string GetTierDescription(int tier);

    protected abstract void OnStacksChanged(int t1, int t2, int t3);

    public virtual void SkillTick(float dt) { }
}
