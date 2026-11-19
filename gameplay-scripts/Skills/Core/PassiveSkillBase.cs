using UnityEngine;

/// <summary>
/// Passive skill: ticks an interval timer; subclass implements PassiveFire().
/// </summary>
public abstract class PassiveSkillBase : SkillBase
{
    protected float interval = 1f;
    protected float nextFireTime;

    protected abstract void PassiveFire();

    public override void SkillTick(float dt)
    {
        if (!Unlocked) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        if (Time.time < nextFireTime) return;
        nextFireTime = Time.time + Mathf.Max(0.05f, interval);
        PassiveFire();
    }
}
