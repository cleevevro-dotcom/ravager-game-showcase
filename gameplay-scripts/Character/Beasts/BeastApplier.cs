using UnityEngine;

/// <summary>
/// Applies a BeastConfig to the player at run start (and on live swap): scales HP/move/damage,
/// adjusts transform scale, swaps the active attack component. Uses reflection-free getters
/// for performance.
///
/// Resolution order delegates to <see cref="CharacterManager.ResolveEffectiveBeast"/>:
///   1. CharacterManager force override (testing only — never persisted)
///   2. BeastManager selection (shop/profile/PlayerPrefs)
///   3. Default Goblin
/// </summary>
public static class BeastApplier
{
    // Cached "design baseline" so re-applying never compounds. Captured the first time we
    // touch a player so the editor-visible PlayerStats values stay the canonical baseline.
    static bool  _baselineCaptured;
    static float _baseHp;
    static float _baseDamage;
    static float _baseMove;
    static Vector3 _baseScale;
    static float _baseLifePerKill;

    /// <summary>
    /// Live / mid-run swap. Reads BeastManager + CharacterManager, applies the resolved beast,
    /// and DOES NOT auto-fill HP (so the player keeps their current HP through a swap).
    /// Use <see cref="ApplyAtRunStart"/> for the initial run-start application — it tops the
    /// player's HP back up to the new max.
    /// </summary>
    public static void ApplyToPlayer(BeastManager mgr) => ApplyInternal(mgr, fullHealOnApply: false);

    /// <summary>
    /// Run-start application: same as ApplyToPlayer but also tops the player up to the new max HP.
    /// Called from GameBootstrap.Step_WireGameplay so the run starts at full HP for the chosen beast.
    /// </summary>
    public static void ApplyAtRunStart(BeastManager mgr) => ApplyInternal(mgr, fullHealOnApply: true);

    static void ApplyInternal(BeastManager mgr, bool fullHealOnApply)
    {
        // Resolve the effective beast through CharacterManager → BeastManager → default Goblin.
        // Keeps the priority logic in one place; ApplyToPlayer never trusts mgr.Selected directly.
        BeastType   effective = CharacterManager.ResolveEffectiveBeast(mgr);
        BeastConfig cfg       = BeastDatabase.Get(effective);

        var stats = Object.FindObjectOfType<PlayerStats>();
        if (stats == null) return;
        var t = stats.transform;

        if (!_baselineCaptured)
        {
            _baseHp           = stats.maxHp;
            _baseDamage       = stats.attackDamage;
            _baseMove         = stats.moveSpeed;
            _baseScale        = t.localScale;
            _baseLifePerKill  = stats.lifePerKill;
            _baselineCaptured = true;
        }

        // Reset to baseline before applying the new beast so swaps stay clean.
        stats.maxHp        = _baseHp        * cfg.hpMul;
        stats.attackDamage = _baseDamage    * cfg.damageMul;
        stats.moveSpeed    = _baseMove      * cfg.moveSpeedMul;
        stats.lifePerKill  = _baseLifePerKill + cfg.lifePerKillBonus;
        t.localScale       = _baseScale * cfg.scaleMul;

        // Force HP UI to show the new max (and clamp current HP). On run-start we also top
        // CurrentHp up to maxHp via Heal — Heal already clamps, so a single big call is enough.
        stats.RefreshStatsUI();
        if (fullHealOnApply) stats.Heal(stats.maxHp);

        EnsureAttackComponents(t.gameObject);
        EnableForBeast(t.gameObject, cfg.type);
    }

    /// <summary>Make sure every beast has its attack component on the player. Inactive ones simply do nothing.</summary>
    public static void EnsureAttackComponents(GameObject playerGO)
    {
        Ensure<PlayerClawAttack>(playerGO);          // Goblin — baseline claw
        Ensure<SkeletonArrowAttack>(playerGO);
        Ensure<SlimeSpitAttack>(playerGO);
        Ensure<GolemSmashAttack>(playerGO);
        Ensure<ZombieBiteAttack>(playerGO);
    }

    static T Ensure<T>(GameObject go) where T : MonoBehaviour
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    static void EnableForBeast(GameObject playerGO, BeastType type)
    {
        var goblin   = playerGO.GetComponent<PlayerClawAttack>();
        var skeleton = playerGO.GetComponent<SkeletonArrowAttack>();
        var slime    = playerGO.GetComponent<SlimeSpitAttack>();
        var golem    = playerGO.GetComponent<GolemSmashAttack>();
        var zombie   = playerGO.GetComponent<ZombieBiteAttack>();

        if (goblin   != null) goblin.enabled   = type == BeastType.Goblin;
        if (skeleton != null) skeleton.enabled = type == BeastType.Skeleton;
        if (slime    != null) slime.enabled    = type == BeastType.Slime;
        if (golem    != null) golem.enabled    = type == BeastType.Golem;
        if (zombie   != null) zombie.enabled   = type == BeastType.Zombie;

        // Reapply the Physical-skill stacks to the now-active attack so swaps don't lose buffs.
        var skillMgr = SkillManager.Instance;
        if (skillMgr == null) return;
        var phys = skillMgr.Get(SkillId.Physical) as PhysicalSkill;
        if (phys != null)
        {
            var inv = skillMgr.Inventory;
            phys.ApplyStacks(
                inv.GetTierStacks(SkillId.Physical, 1),
                inv.GetTierStacks(SkillId.Physical, 2),
                inv.GetTierStacks(SkillId.Physical, 3));
        }

        // Phase 7: every linked/bonus skill re-subscribes to the IPlayerAttack
        // events on the (potentially changed) active beast component. Bind
        // methods are idempotent so this is safe to call every swap. Without
        // this, picking up a new mutation mid-run between beast swaps could
        // leave the skill subscribed to a stale set of components.
        skillMgr.RebindAllSkills();
    }
}
