using System;
using UnityEngine;

/// <summary>
/// Shared boilerplate for all non-Goblin beast attack components: cooldown timer, auto-lock
/// targeting, OnSwing event for linked skills, attack-state slowdown. Each concrete subclass
/// only fills in DoAttack(origin, aimDir).
///
/// (OOP demonstration: abstract base + virtual hooks + interface implementation. Subclasses
/// can override <see cref="GetCooldown"/> to react to PlayerStats, override
/// <see cref="ApplyPhysicalStacks"/> for beast-specific Physical-skill scaling, and just
/// implement the abstract <see cref="DoAttack"/>.)
/// </summary>
[RequireComponent(typeof(PlayerStats))]
public abstract class BaseBeastAttack : MonoBehaviour, IPlayerAttack
{
    [Header("Common Tuning")]
    [SerializeField] protected float baseCooldown    = 0.9f;
    [SerializeField] protected float autoLockRange   = 6f;
    [SerializeField] protected float attackSlowdown  = 0.5f;

    [Header("Physical Stacks (runtime)")]
    public float cooldownMul = 1f;
    public float damageMul   = 1f;
    public float rangeAdd    = 0f;

    protected PlayerStats     stats;
    protected PlayerMovements movement;

    Vector2 _aimDir = Vector2.right;
    float   _nextAttackTime;
    bool    _isDead;
    bool    _wasAttacking;

    public Vector2 AimDir => _aimDir;

    // --- IPlayerAttack events (Phase 7) -------------------------------------
    public event Action<Vector2, Vector2> OnSwing;
    public event Action<Vector2, Vector2> OnAttackStarted;
    public event Action<EnemyBase>        OnAttackHit;
    public event Action                   OnAttackFinished;

    /// <summary>Current cooldown — exposed so linked skills can pace effects.</summary>
    public float CurrentAttackInterval => GetCooldown();

    /// <summary>Subclass calls this from inside DoAttack for every enemy actually struck.
    /// Forwards to the public OnAttackHit event so bonus skills can react per hit.</summary>
    protected void RaiseAttackHit(EnemyBase enemy)
    {
        if (enemy != null) OnAttackHit?.Invoke(enemy);
    }

    protected virtual void Awake()
    {
        stats    = GetComponent<PlayerStats>();
        movement = GetComponent<PlayerMovements>();
        stats.OnDeath += () => _isDead = true;
    }

    protected virtual void Update()
    {
        if (_isDead || GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        UpdateAutoLock();

        float cooldown = GetCooldown();
        bool  isAttacking = InputService.AttackHeld && Time.time >= _nextAttackTime;

        if (isAttacking)
        {
            _nextAttackTime = Time.time + cooldown;
            Vector2 origin = transform.position;
            // Phase 7: announce the swing BEFORE damage so linked skills spawn
            // their projectiles at the same origin/aim the beast is using. We
            // fire BOTH OnAttackStarted (new) and OnSwing (legacy) to keep
            // existing skills working without modification.
            OnAttackStarted?.Invoke(origin, _aimDir);
            OnSwing?.Invoke(origin, _aimDir);
            DoAttack(origin, _aimDir);
            OnAttackFinished?.Invoke();
        }

        if (InputService.AttackHeld)
        {
            movement?.SetAttackSlowdown(attackSlowdown);
            _wasAttacking = true;
        }
        else if (_wasAttacking)
        {
            movement?.SetAttackSlowdown(1f);
            _wasAttacking = false;
        }
    }

    protected virtual float GetCooldown()
    {
        float atkSpd = stats != null ? Mathf.Max(0.1f, stats.attackSpeed) : 1f;
        return (baseCooldown * cooldownMul) / atkSpd;
    }

    protected virtual void UpdateAutoLock()
    {
        Vector2 pos    = transform.position;
        var     target = TargetingUtility.FindNearestEnemy(pos, autoLockRange);
        if (target != null)
        {
            Vector2 dir = ((Vector2)target.Position - pos);
            if (dir.sqrMagnitude > 0.0001f) _aimDir = dir.normalized;
        }
        else if (movement != null)
        {
            _aimDir = movement.LastMoveDir;
        }
    }

    /// <summary>Concrete attack logic — one frame, one call. Subclass spawns projectiles / does AoE / etc.</summary>
    protected abstract void DoAttack(Vector2 origin, Vector2 aimDir);

    /// <summary>Default Physical scaling: cooldown faster, damage up, range up. Override for beast-specific behavior.</summary>
    public virtual void ApplyPhysicalStacks(int t1, int t2, int t3)
    {
        cooldownMul = Mathf.Max(0.30f, 1f - 0.10f * t1);
        damageMul   = 1f + 0.10f * t1 + 0.10f * t2 + 0.15f * t3;
        rangeAdd    = 0.25f * t2;
    }

    /// <summary>
    /// Player base attack damage at design time (matches PlayerStats.attackDamage default in
    /// the Inspector and the README "Base Stats" table). Used to express the beast's hardcoded
    /// per-attack damage relative to the player's CURRENT attackDamage so beast-level damage
    /// scaling (BeastConfig.damageMul, baked into stats.attackDamage by BeastApplier) and any
    /// future attackDamage shop upgrade flow uniformly through every beast attack.
    /// </summary>
    const float DesignBaseAttackDamage = 10f;

    protected float ComputeDamage(float baseDamage)
    {
        // Per-skill physical multiplier (set by PhysicalSkill.ApplyPhysicalStacks).
        float dmg = baseDamage * damageMul;

        // Pick up the beast-level damage scaling. BeastApplier writes BeastConfig.damageMul into
        // stats.attackDamage (Goblin 1.0× / Skeleton 0.95× / Slime 0.85× / Golem 1.5× / Zombie 1.0×).
        // Multiplying the beast's hardcoded base damage by the ratio (current / design baseline)
        // is the only way that scaling shows up for non-Goblin attacks — without this, Skeleton /
        // Slime / Golem / Zombie all silently ignore their own beast damage modifier.
        if (stats != null && stats.attackDamage > 0f)
            dmg *= stats.attackDamage / DesignBaseAttackDamage;

        // Agility T3+ "Shadow Agility": every point of moveSpeed adds flat damage. Matches
        // PlayerClawAttack ordering — added before the outgoing-damage multiplier.
        if (stats != null && stats.damagePerMoveSpeed > 0f)
            dmg += stats.damagePerMoveSpeed * stats.moveSpeed;

        // Berserk (missing-HP ramp) / Bloodlust (T3 frenzy) / Soul Harvest stacking buff all push
        // through stats.outgoingDamageMul. PlayerClawAttack already multiplies by it; without this
        // line, those skills silently no-op for non-Goblin beasts.
        if (stats != null && stats.outgoingDamageMul != 1f)
            dmg *= stats.outgoingDamageMul;

        return dmg;
    }
}
