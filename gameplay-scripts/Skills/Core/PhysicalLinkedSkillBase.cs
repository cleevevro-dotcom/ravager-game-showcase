using UnityEngine;

/// <summary>
/// Base for "fire on swing" casting skills (Fire, Lightning, Axe, Plague, Earthshatter,
/// WindCutter). These no longer have their own input or cooldown — they fire when whichever
/// attack component is active executes a swing, piggybacking on its cadence and aim direction.
/// Listening to all <see cref="IPlayerAttack"/> components on the player means the skill works
/// regardless of which beast was selected. Disabled attack components don't fire OnSwing.
/// </summary>
public abstract class PhysicalLinkedSkillBase : SkillBase
{
    IPlayerAttack[] _attacks;

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

    protected virtual void OnDestroy()
    {
        Unsubscribe();
    }

    void HandleSwing(Vector2 origin, Vector2 aimDir)
    {
        if (!Unlocked) return;
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        OnSwingCast(origin, aimDir);
    }

    /// <summary>Invoked on every physical claw swing while this skill is unlocked.</summary>
    protected abstract void OnSwingCast(Vector2 origin, Vector2 aimDir);
}
