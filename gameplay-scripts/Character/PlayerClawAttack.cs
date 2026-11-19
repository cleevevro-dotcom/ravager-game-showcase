using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PlayerStats))]
public class PlayerClawAttack : MonoBehaviour, IPlayerAttack
{
    [Header("Visuals")]
    [SerializeField] ClawEffect _clawEffect;

    [Header("Attack")]
    [SerializeField] float _clawOffset    = 0.8f;
    [SerializeField] float _attackRange   = 1.2f;      // hit radius from claw center
    [SerializeField] float _autoLockRange = 6f;        // how far we look for targets
    [SerializeField] float _baseCooldown  = 0.9f;      // slow base swing
    [SerializeField] float _knockback     = 3.5f;

    [Header("Upgrades (runtime, set by PhysicalSkill)")]
    public float cooldownMul    = 1f;    // <1 = faster
    public float rangeAdd       = 0f;    // extra radius
    public float damageMul      = 1f;
    public bool  doubleSwing    = false; // T2: extra attack in front
    public bool  backAttack     = false; // T3: attack behind
    public bool  sideAttacks    = false; // T3: attack left and right
    public float attackSlowdown = 0.5f;  // movement speed multiplier while attacking

    /// <summary>Fires every physical swing with (origin, aimDir). Used by Fire/Lightning/Axe/Plague.</summary>
    public event Action<Vector2, Vector2> OnSwing;

    // --- Phase 7: extra IPlayerAttack events for OOP parity with BaseBeastAttack.
    public event Action<Vector2, Vector2> OnAttackStarted;
    public event Action<EnemyBase>        OnAttackHit;
    public event Action                   OnAttackFinished;

    /// <summary>Effective seconds between Goblin swings — exposed so linked skills can pace effects.</summary>
    public float CurrentAttackInterval =>
        (_baseCooldown * cooldownMul) / Mathf.Max(0.1f, _stats != null ? _stats.attackSpeed : 1f);

    PlayerStats     _stats;
    PlayerMovements _movement;
    float           _nextAttackTime;
    bool            _isDead;
    Vector2         _aimDir = Vector2.right;
    bool            _wasAttacking;

    readonly List<ISpatialEntity> _hitBuffer      = new List<ISpatialEntity>(16);
    readonly List<ISpatialEntity> _buildingBuffer = new List<ISpatialEntity>(8);
    // Tracks enemies already hit during a single multi-direction swing so back/side attacks
    // don't pile on the same target the front swing already hit. Cleared at the start of
    // every swing cycle, never allocates per-swing.
    readonly HashSet<EnemyBase>   _swingHits      = new HashSet<EnemyBase>();

    public Vector2 AimDir => _aimDir;

    void Awake()
    {
        _stats    = GetComponent<PlayerStats>();
        _movement = GetComponent<PlayerMovements>();
        _stats.OnDeath += () => _isDead = true;
    }

    void Update()
    {
        if (_isDead || GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;

        UpdateAutoLock();

        float cooldown = (_baseCooldown * cooldownMul) / Mathf.Max(0.1f, _stats.attackSpeed);
        bool isAttacking = InputService.AttackHeld && Time.time >= _nextAttackTime;

        if (isAttacking)
        {
            _nextAttackTime = Time.time + cooldown;
            _swingHits.Clear();

            // Phase 7: announce the swing BEFORE the first sub-attack so linked
            // skills spawn ONCE per cycle even when T2 doubleSwing + T3 multi-
            // direction is active. Per-direction OnSwing keeps firing inside
            // PerformAttack so per-direction projectiles (existing behavior)
            // are unchanged.
            Vector2 startOrigin = transform.position;
            OnAttackStarted?.Invoke(startOrigin, _aimDir);

            // Front: always swings (visual + linked-skill cast happen even if no enemy is in cone).
            PerformAttack(_aimDir);

            if (doubleSwing)
                PerformAttack(_aimDir); // second front attack — also subject to swingHits dedupe

            if (backAttack)
                PerformAttack(-_aimDir);

            if (sideAttacks)
            {
                Vector2 leftDir  = new Vector2(-_aimDir.y,  _aimDir.x);  // 90° CCW
                Vector2 rightDir = new Vector2( _aimDir.y, -_aimDir.x);  // 90° CW
                PerformAttack(leftDir);
                PerformAttack(rightDir);
            }

            // Phase 7: cycle complete. Bonus skills that want to fire once per
            // physical attack (not per directional sub-swing) listen here.
            OnAttackFinished?.Invoke();
        }

        // Apply slowdown while attacking (not for passive skills)
        if (InputService.AttackHeld)
        {
            _movement?.SetAttackSlowdown(attackSlowdown);
            _wasAttacking = true;
        }
        else if (_wasAttacking)
        {
            _movement?.SetAttackSlowdown(1f);
            _wasAttacking = false;
        }
    }

    void UpdateAutoLock()
    {
        Vector2 pos    = transform.position;
        var     target = TargetingUtility.FindNearestEnemy(pos, _autoLockRange);
        if (target != null)
        {
            Vector2 dir = ((Vector2)target.Position - pos);
            if (dir.sqrMagnitude > 0.0001f) _aimDir = dir.normalized;
        }
        else if (_movement != null)
        {
            _aimDir = _movement.LastMoveDir;
        }
    }

    void PerformAttack(Vector2 dir)
    {
        const float DirectionCone = 90f; // half-cone tolerance ~45° each side

        Vector2 playerPos  = transform.position;
        Vector2 clawCenter = playerPos + dir * _clawOffset;
        float   angleDeg   = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        _clawEffect?.Play(clawCenter, angleDeg);

        var hash = SpatialHashManager.Instance;
        if (hash != null)
        {
            float effRange = _attackRange + rangeAdd;
            hash.Query(SpatialCategory.Enemy, clawCenter, effRange, _hitBuffer);

            float dmg = _stats.attackDamage * damageMul;
            // Agility T3+ "Shadow Agility": every point of moveSpeed adds flat damage.
            if (_stats.damagePerMoveSpeed > 0f) dmg += _stats.damagePerMoveSpeed * _stats.moveSpeed;
            // Berserk / Bloodlust outgoing-damage multipliers stack with everything else.
            if (_stats.outgoingDamageMul != 1f) dmg *= _stats.outgoingDamageMul;
            float halfCone = DirectionCone * 0.5f;

            for (int i = 0; i < _hitBuffer.Count; i++)
            {
                if (!(_hitBuffer[i] is EnemyBase enemy) || enemy.IsDead) continue;
                if (_swingHits.Contains(enemy)) continue; // already hit by an earlier direction this swing

                Vector2 toEnemy = (Vector2)enemy.Position - playerPos;
                if (toEnemy.sqrMagnitude < 0.0001f) toEnemy = dir;
                float enemyAngle = Mathf.Atan2(toEnemy.y, toEnemy.x) * Mathf.Rad2Deg;
                float angleDiff  = Mathf.Abs(Mathf.DeltaAngle(angleDeg, enemyAngle));
                if (angleDiff > halfCone) continue;

                enemy.TakeDamage(dmg);
                KnockbackUtility.ApplyRadial(enemy, clawCenter, _knockback);
                _swingHits.Add(enemy);
                // Phase 7: per-enemy hit event so bonus skills (Bloodlust on-hit
                // stacks, Soul Harvest, Chaos Surge effect rolls) can observe.
                OnAttackHit?.Invoke(enemy);
            }

            hash.Query(SpatialCategory.Building, clawCenter, effRange, _buildingBuffer);
            for (int i = 0; i < _buildingBuffer.Count; i++)
            {
                if (_buildingBuffer[i] is Destructible d && !d.IsDead)
                    d.TakeDamage(dmg);
            }
        }

        // Linked skills (Fire/Lightning/Axe/Plague) ride every directional swing — even if
        // no enemy was in the cone — so they project in that direction regardless of targets.
        OnSwing?.Invoke(playerPos, dir);
    }

    /// <summary>Called by PhysicalSkill when its tier stacks change.</summary>
    public void ApplyPhysicalStacks(int t1, int t2, int t3)
    {
        // T1 — base swing throughput: each stack speeds up cooldown and adds a bit of damage.
        float cdReduce = 0.10f * t1;
        cooldownMul = Mathf.Max(0.30f, 1f - cdReduce);
        damageMul   = 1f + 0.10f * t1;

        // T2 — area / utility: first stack unlocks the double swing; further stacks widen radius
        // and grow damage.
        doubleSwing = t2 >= 1;
        rangeAdd    = 0.25f * t2;
        damageMul  += 0.10f * t2;

        // T3 — multi-direction evolution: first stack adds a back attack, second stack adds
        // side attacks, further stacks grow damage.
        backAttack  = t3 >= 1;
        sideAttacks = t3 >= 2;
        damageMul  += 0.15f * t3;
    }
}
