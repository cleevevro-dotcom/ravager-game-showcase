using System;
using UnityEngine;

/// <summary>
/// Beast-agnostic attack contract.
///
/// Every beast attack component (Goblin claw, Skeleton arrow, Slime spit,
/// Golem smash, Zombie bite) implements this so mutation skills can subscribe
/// to a single interface and have their projectiles / on-hit effects work
/// regardless of which beast the player picked.
///
/// Event ordering during a single swing:
///   1. <see cref="OnAttackStarted"/> — origin + aim known, before any damage.
///      Linked attack skills (Fire, Lightning, Axe, Plague, Earthshatter,
///      WindCutter) spawn their projectiles here.
///   2. <see cref="OnSwing"/> — backward-compatible alias for OnAttackStarted.
///      Existing skills keep working without modification.
///   3. <see cref="OnAttackHit"/> — fires once per enemy struck (after damage).
///      On-hit bonus skills (Bloodlust attack-speed stacks, Soul Harvest
///      bonus XP, Chaos Surge random effects) read this.
///   4. <see cref="OnAttackFinished"/> — fires at the end of the swing.
///      Cleanup hook for skills that need to know the attack window closed.
///
/// Phase 7 (Skeleton/Golem skill compatibility): the new explicit OnAttackHit
/// / OnAttackFinished events let bonus skills correctly observe non-Goblin
/// attacks without having to override anything. SkillManager.RebindAllSkills
/// is called after a beast swap so subscriptions stay valid on the new
/// active component.
/// </summary>
public interface IPlayerAttack
{
    // --- Backward-compat (Phase 0) -----------------------------------------

    /// <summary>(origin, aimDir) fired every time the player executes a swing.
    /// Equivalent to <see cref="OnAttackStarted"/> — kept so legacy skills don't break.</summary>
    event Action<Vector2, Vector2> OnSwing;

    /// <summary>Last computed aim direction (auto-locked or based on movement).</summary>
    Vector2 AimDir { get; }

    /// <summary>Allow the Physical skill to layer on extra damage / cooldown / range / multi-strike.</summary>
    void ApplyPhysicalStacks(int t1, int t2, int t3);

    // --- Phase 7 additions --------------------------------------------------

    /// <summary>(origin, aimDir) fired at the START of every swing — before damage is dealt.
    /// Linked attack skills spawn projectiles in response.</summary>
    event Action<Vector2, Vector2> OnAttackStarted;

    /// <summary>Fires once per enemy actually struck by the swing.
    /// On-hit bonus skills (Bloodlust, Soul Harvest, Chaos Surge) listen here.</summary>
    event Action<EnemyBase> OnAttackHit;

    /// <summary>Fires after the swing's damage + linked-skill projectiles have been processed.</summary>
    event Action OnAttackFinished;

    /// <summary>Current effective seconds between swings, accounting for
    /// PlayerStats.attackSpeed and the Physical-skill cooldownMul. Linked
    /// skills that want to time visual ramp-ups can read this.</summary>
    float CurrentAttackInterval { get; }
}
