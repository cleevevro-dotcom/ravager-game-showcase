using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Serialization;

public abstract class EnemyBase : MonoBehaviour, ISpatialEntity
{
    [Header("Stats")]
    public EnemyType enemyType;
    public float maxHp = 30f;
    public float moveSpeed = 2f;
    public float damage = 8f;
    public float attackRange = 0.8f;
    public float attackCooldown = 1.2f;
    public float xpValue = 10f;

    [Header("Soft Horde — Separation")]
    [Tooltip("Distance below which separation push ramps to maximum.")]
    public float personalSpace      = 0.45f;
    [Tooltip("Outer search range for neighbor avoidance.")]
    public float separationRadius   = 0.9f;
    [Tooltip("Multiplier on the summed avoidance vector. Replaces legacy separationStrength.")]
    [FormerlySerializedAs("separationStrength")]
    public float separationWeight   = 1.2f;

    [Header("Soft Horde — Flow / Offset")]
    [Tooltip("Multiplier on the tangent (perpendicular-to-chase) component. Higher = swirlier horde, less head-on collapse.")]
    public float sideFlowWeight     = 0.25f;
    [Tooltip("Each enemy aims at player + a random unit-circle * this radius, so they don't all stack on one point.")]
    public float targetOffsetRadius = 1.2f;

    [Header("Soft Horde — Crowd Slowdown")]
    [Tooltip("Speed reduction per nearby neighbor. Crowded enemies slow rather than push harder.")]
    public float crowdSlowdownStrength = 0.08f;
    [Tooltip("Cap on crowd-driven speed reduction (0 = none, 0.55 = up to 55% slower).")]
    [Range(0f, 0.95f)] public float maxCrowdSlowdown = 0.55f;

    [Header("Resistances (0..1)")]
    [Range(0f, 1f)] public float fearResistance = 0f;

    /// <summary>Set true on enemies summoned BY the boss (e.g. Dorado's Soldier Circle Attack).
    /// They bypass the encounter's retreat-orbit so they actively chase + attack the player.
    /// Reset to false on every Init so a recycled pool entry doesn't inherit the flag.</summary>
    [System.NonSerialized] public bool ignoreBossRetreat;

    protected float currentHp;
    protected float nextAttackTime;
    protected Transform playerTransform;
    protected PlayerStats playerStats;
    protected SpatialHashManager spatial;
    protected EnemyManager enemyManager;
    protected Animator anim;
    protected SpriteRenderer sr;

    readonly List<ISpatialEntity> _neighborBuffer = new List<ISpatialEntity>(8);
    readonly List<ISpatialEntity> _obstacleBuffer = new List<ISpatialEntity>(8);
    public float obstacleRadius = 0.35f;
    bool _registered;

    // Per-instance random offset around the player so two enemies don't aim at the same point.
    // Re-rolled on each Init so a recycled enemy doesn't reuse its old offset. Protected so
    // subclass movement overrides (EnemySoldier flank-flow) can keep the same spread behavior
    // without re-rolling on every Tick.
    protected Vector2 _targetOffset;

    // --- Status effect state ---
    float   _burnDps;
    float   _burnTimer;
    float   _burnTickTimer;
    float   _plagueDps;
    float   _plagueTimer;
    float   _plagueTickTimer;
    float   _slowFactor = 1f;
    float   _slowTimer;
    float   _stunTimer;
    Vector2 _kbVelocity;
    float   _kbTimer;

    // Tint arbitration: capture once on first Awake/Init, then status priority chain
    // (stun > plague > burn > slow > base) decides what sr.color is each frame.
    Color _baseColor       = Color.white;
    bool  _baseColorCaptured;
    static readonly Color StunTint   = new Color(1.00f, 1.00f, 0.50f, 1f);
    static readonly Color PlagueTint = new Color(0.50f, 0.85f, 0.40f, 1f);
    static readonly Color BurnTint   = new Color(1.00f, 0.55f, 0.30f, 1f);
    static readonly Color SlowTint   = new Color(0.65f, 0.85f, 1.00f, 1f);

    /// <summary>Subclasses (e.g. HorseMan during telegraph) override to true to suppress status
    /// tint arbitration while they own the sprite color directly.</summary>
    protected virtual bool IsExternallyTinted => false;

    public bool IsDead                       { get; protected set; }
    public Vector2         Position          => (Vector2)transform.position;
    public SpatialCategory SpatialCategory   => SpatialCategory.Enemy;
    public bool            IsActiveInHash    => !IsDead && gameObject.activeSelf;
    public bool            IsStunned         => _stunTimer > 0f;

    /// <summary>Animation LOD bucket. EnemyManager assigns this once per frame from the
    /// existing distance-to-player and a single camera-bounds query, so individual enemies
    /// never run camera-space math themselves.</summary>
    public enum AnimLOD : byte { Near = 0, Mid = 1, Far = 2, Off = 3 }

    byte _appliedLod = 255;  // sentinel — first ApplyAnimLOD always writes through

    /// <summary>Override to true on enemies that must always run full Animator (e.g. the boss).
    /// The EnemyManager's LOD pass skips these so they keep 1× anim speed regardless of distance.</summary>
    public virtual bool IsLodExempt => false;

    protected static readonly int HashWalk   = Animator.StringToHash("Walk");
    protected static readonly int HashAttack = Animator.StringToHash("Attack");
    protected static readonly int HashHurt   = Animator.StringToHash("Hurt");
    protected static readonly int HashDead   = Animator.StringToHash("Dead");

    // Profiler markers — visible in Unity Profiler under "Scripts" so the BUG-01 hit-path
    // diagnosis can localize spikes per call. Allocated once.
    static readonly CustomSampler s_TakeDamageSampler   = CustomSampler.Create("EnemyBase.TakeDamage");
    static readonly CustomSampler s_HurtAnimSampler     = CustomSampler.Create("EnemyBase.HurtAnimation");
    static readonly CustomSampler s_ApplyKnockbackSampler = CustomSampler.Create("EnemyBase.ApplyKnockback");
    static readonly CustomSampler s_DieSampler          = CustomSampler.Create("EnemyBase.Die");

    protected virtual void Awake()
    {
        anim = GetComponent<Animator>();
        sr   = GetComponent<SpriteRenderer>();
        if (sr != null && !_baseColorCaptured)
        {
            _baseColor         = sr.color;
            _baseColorCaptured = true;
        }
    }

    // Top-down Y-sort. Base is high enough that characters stay above tiles (-10..-8) and
    // buildings (5..6) even at extreme Y values; clamped to the int16 range `sortingOrder`
    // actually supports so characters never wrap around and vanish behind tiles.
    protected const int YSortBase       = 20000;
    protected const int YSortMultiplier = 10;
    // Keep the floor ABOVE world sprites (tiles -10..-8, buildings 5..6) so a character at an
    // extreme Y never falls behind the ground and vanishes.
    protected const int YSortMin        = 10;
    protected const int YSortMax        = 32000;

    protected virtual void LateUpdate()
    {
        if (sr == null) return;
        int raw = YSortBase - Mathf.RoundToInt(transform.position.y * YSortMultiplier);
        sr.sortingOrder = Mathf.Clamp(raw, YSortMin, YSortMax);
    }

    protected virtual void OnEnable()
    {
        if (_registered || SpatialHashManager.Instance == null) return;
        SpatialHashManager.Instance.Register(this);
        _registered = true;
    }

    protected virtual void OnDisable()
    {
        if (!_registered || SpatialHashManager.Instance == null) { _registered = false; return; }
        SpatialHashManager.Instance.Unregister(this);
        _registered = false;
    }

    public virtual void Init(Transform player, PlayerStats stats, SpatialHashManager spatialMgr, EnemyManager mgr)
    {
        playerTransform = player;
        playerStats     = stats;
        spatial         = spatialMgr;
        enemyManager    = mgr;
        // Scale max HP by the player-level milestone multiplier on EnemyManager. Already-spawned
        // enemies keep their old HP — only fresh pool spawns see the new value.
        float hpMul     = mgr != null ? mgr.GlobalHpMul : 1f;
        currentHp       = maxHp * hpMul;
        IsDead          = false;
        nextAttackTime  = 0f;

        _burnDps = _burnTimer = _burnTickTimer = 0f;
        _plagueDps = _plagueTimer = _plagueTickTimer = 0f;
        _slowFactor = 1f;
        _slowTimer = _stunTimer = _kbTimer = 0f;
        _kbVelocity = Vector2.zero;
        ignoreBossRetreat = false;
        if (sr != null && _baseColorCaptured) sr.color = _baseColor;

        // Random unit-circle * radius. Same offset for the lifetime of this spawn so the enemy
        // commits to a position rather than wobbling.
        _targetOffset = targetOffsetRadius > 0f
            ? Random.insideUnitCircle * targetOffsetRadius
            : Vector2.zero;

        if (anim != null)
        {
            anim.Rebind();
            // Reset LOD state so a recycled enemy re-applies on its first manager tick instead of
            // inheriting the previous spawn's anim.enabled / anim.speed.
            if (!anim.enabled) anim.enabled = true;
            anim.speed  = 1f;
            _appliedLod = 255;

            // Stagger animation phase so 200 enemies don't walk in lockstep. Guarded with HasState
            // because subclasses may use a controller without a Walk state (boss).
            if (anim.HasState(0, HashWalk))
                anim.Play(HashWalk, 0, Random.value);
        }

        if (!_registered && SpatialHashManager.Instance != null)
        {
            SpatialHashManager.Instance.Register(this);
            _registered = true;
        }
    }

    public void TakeDamage(float amount) => TakeDamage(amount, fireHurt: true);

    /// <summary>
    /// TakeDamage overload used by batched / AoE damage sources (IronSkin reflect, etc.) that
    /// want to skip the per-enemy Animator.SetTrigger("Hurt") call. With many enemies hit in
    /// one frame the cumulative SetTrigger cost is the spike, not the HP arithmetic.
    /// </summary>
    public void TakeDamage(float amount, bool fireHurt)
    {
        if (IsDead) return;
        s_TakeDamageSampler.Begin();
        currentHp -= amount;
        if (fireHurt) OnHurt();
        if (currentHp <= 0f) Die();
        s_TakeDamageSampler.End();
    }

    protected virtual void OnHurt()
    {
        // Animator.SetTrigger costs the same whether enabled or not (writes parameter dict).
        // Off-LOD enemies have anim.enabled=false and won't visibly play the hurt clip anyway,
        // so just skip the trigger queue. Free win when many off-screen enemies eat AoE damage.
        if (anim == null || !anim.enabled) return;
        s_HurtAnimSampler.Begin();
        anim.SetTrigger(HashHurt);
        s_HurtAnimSampler.End();
    }

    protected virtual void Die()
    {
        if (IsDead) return;
        s_DieSampler.Begin();
        IsDead = true;
        float xpMul = enemyManager != null ? enemyManager.GlobalXpMul : 1f;
        XpManager.Instance?.SpawnOrb(Position, xpValue * xpMul);
        playerStats?.NotifyKilled(this);
        enemyManager?.ReturnEnemy(this);
        s_DieSampler.End();
    }

    // --- Status effect API ---

    public void ApplyBurn(float dps, float duration)
    {
        if (IsDead) return;
        if (dps > _burnDps)      _burnDps   = dps;
        if (duration > _burnTimer) _burnTimer = duration;
    }

    /// <summary>Marks the enemy as plagued for `duration` seconds. dps may be 0 for visual-only
    /// marks (e.g. brief tint refresh while standing in a PlaguePuddle whose zone deals its own
    /// damage), or non-zero for skills that do per-tick poison damage. Plague ticks every 0.5s.</summary>
    public void ApplyPlague(float dps, float duration)
    {
        if (IsDead) return;
        if (dps > _plagueDps)        _plagueDps   = dps;
        if (duration > _plagueTimer) _plagueTimer = duration;
    }

    public void ApplySlow(float factor, float duration)
    {
        if (IsDead) return;
        if (factor < _slowFactor) _slowFactor = Mathf.Max(0.05f, factor);
        if (duration > _slowTimer) _slowTimer  = duration;
    }

    public void ApplyStun(float duration)
    {
        if (IsDead) return;
        if (duration > _stunTimer) _stunTimer = duration;
    }

    public void ApplyKnockback(Vector2 velocity, float duration = 0.15f)
    {
        if (IsDead) return;
        s_ApplyKnockbackSampler.Begin();
        _kbVelocity = velocity;
        _kbTimer    = duration;
        s_ApplyKnockbackSampler.End();
    }

    public virtual void Tick(float dt)
    {
        if (IsDead || playerTransform == null) return;

        TickStatusEffects(dt);
        if (IsDead) return;

        if (_stunTimer > 0f)
        {
            anim?.SetBool(HashWalk, false);
            return;
        }

        if (_kbTimer > 0f)
        {
            transform.position += (Vector3)(_kbVelocity * dt);
            _kbVelocity *= 0.88f;
            return;
        }

        MoveTowardPlayer(dt);
        TryAttack();
    }

    // Lightweight tick for distant/throttled enemies: only status effects, no movement.
    // Called when movement is throttled to stay under frame budget.
    public virtual void TickStatusEffectsOnly(float dt)
    {
        if (IsDead) return;
        TickStatusEffects(dt);
    }

    void TickStatusEffects(float dt)
    {
        if (_burnTimer > 0f)
        {
            _burnTimer -= dt;
            _burnTickTimer -= dt;
            if (_burnTickTimer <= 0f)
            {
                _burnTickTimer = 0.5f;
                TakeDamage(_burnDps * 0.5f);
                if (IsDead) return;
            }
            if (_burnTimer <= 0f) { _burnDps = 0f; _burnTickTimer = 0f; }
        }

        if (_plagueTimer > 0f)
        {
            _plagueTimer -= dt;
            _plagueTickTimer -= dt;
            if (_plagueTickTimer <= 0f)
            {
                _plagueTickTimer = 0.5f;
                if (_plagueDps > 0f) TakeDamage(_plagueDps * 0.5f);
                if (IsDead) return;
            }
            if (_plagueTimer <= 0f) { _plagueDps = 0f; _plagueTickTimer = 0f; }
        }

        if (_slowTimer > 0f)
        {
            _slowTimer -= dt;
            if (_slowTimer <= 0f) _slowFactor = 1f;
        }

        if (_stunTimer > 0f) _stunTimer -= dt;
        if (_kbTimer   > 0f) _kbTimer   -= dt;

        ApplyStatusTint();
    }

    /// <summary>
    /// Manager-driven animation LOD. Only writes to the Animator when the bucket changes, so
    /// the steady-state cost is one byte compare per enemy per frame. Off disables the Animator
    /// outright; transitions still queue (SetTrigger / SetBool) and replay the moment we switch
    /// back to a non-Off bucket.
    /// </summary>
    public void ApplyAnimLOD(AnimLOD lod, float midSpeed, float farSpeed)
    {
        if (anim == null) return;
        if ((byte)lod == _appliedLod) return;
        _appliedLod = (byte)lod;
        switch (lod)
        {
            case AnimLOD.Near: if (!anim.enabled) anim.enabled = true; anim.speed = 1f;       break;
            case AnimLOD.Mid:  if (!anim.enabled) anim.enabled = true; anim.speed = midSpeed; break;
            case AnimLOD.Far:  if (!anim.enabled) anim.enabled = true; anim.speed = farSpeed; break;
            case AnimLOD.Off:  anim.enabled = false;                                          break;
        }
    }

    /// <summary>Single-slot tint arbitration. Priority: stun > plague > burn > slow > base.
    /// Skipped when a subclass has temporarily claimed the sprite color (HorseMan telegraph).</summary>
    void ApplyStatusTint()
    {
        if (sr == null || IsExternallyTinted) return;
        Color target = _baseColor;
        if      (_stunTimer   > 0f) target = StunTint;
        else if (_plagueTimer > 0f) target = PlagueTint;
        else if (_burnTimer   > 0f) target = BurnTint;
        else if (_slowTimer   > 0f) target = SlowTint;
        if (sr.color != target) sr.color = target;
    }

    protected virtual void MoveTowardPlayer(float dt)
    {
        // During a boss encounter, non-boss enemies retreat to (and orbit) the boss arena edge so
        // the player has room to fight. The boss subclass overrides MoveTowardPlayer outright, so
        // this branch only ever applies to mooks. Boss SUMMONS (Dorado's soldier-circle) bypass
        // this — they're meant to pressure the player by closing in.
        var encounter = BossEncounter.Instance;
        if (encounter != null && encounter.NonBossRetreat && !ignoreBossRetreat)
        {
            MoveRetreatOrbit(dt, encounter.RetreatCenter, encounter.RetreatRadius);
            return;
        }

        // Soft horde: Final = Chase(targetOffset) + Separation + SideFlow.
        // Crowd slowdown kicks in instead of compressing — keeps the swarm aggressive but not
        // collapsed into a single ugly blob.
        Vector2 pos         = Position;
        Vector2 targetPos   = (Vector2)playerTransform.position + _targetOffset;
        Vector2 toTarget    = targetPos - pos;
        Vector2 dirChase    = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;

        Vector2 separation  = ComputeSeparation(pos, out int neighborCount);

        // Side flow: a stable per-enemy tangent component scaled by local crowding so the
        // horde swirls around the player rather than crashing head-on.
        Vector2 perp        = new Vector2(-dirChase.y, dirChase.x);
        float   sideSign    = _targetOffset.x >= 0f ? 1f : -1f;
        float   crowdNorm   = Mathf.Min(1f, neighborCount * 0.18f);
        Vector2 sideFlow    = perp * (sideSign * crowdNorm * sideFlowWeight);

        Vector2 dir         = dirChase + separation + sideFlow;
        Vector2 moveDir     = dir.sqrMagnitude > 0.0001f ? dir.normalized : dirChase;

        float   crowdSlow   = 1f - Mathf.Min(maxCrowdSlowdown, neighborCount * crowdSlowdownStrength);
        float   effSpeed    = ComputeEffectiveSpeed(pos) * crowdSlow;

        Vector2 move        = moveDir * effSpeed * dt;
        Vector2 desired     = pos + move;
        Vector2 resolved    = ResolveBuildingCollision(desired);

        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(move.x);
        SetWalking(true);
    }

    // --- Movement helpers shared with subclasses (Archer kite, HorseMan dash, etc.) ---

    protected float SlowFactor => _slowFactor;

    /// <summary>
    /// Capped separation vector from nearby enemies, with falloff between personalSpace
    /// and separationRadius. Returns the neighbor count via <paramref name="neighborCount"/>
    /// so callers can drive crowd-aware behavior (side flow, crowd slowdown) off it without a
    /// second spatial query.
    /// </summary>
    protected Vector2 ComputeSeparation(Vector2 pos, out int neighborCount)
    {
        neighborCount = 0;
        if (spatial == null) return Vector2.zero;

        Vector2 sep   = Vector2.zero;
        float   denom = Mathf.Max(0.01f, separationRadius - personalSpace);
        spatial.Query(SpatialCategory.Enemy, pos, separationRadius, _neighborBuffer);

        for (int i = 0; i < _neighborBuffer.Count; i++)
        {
            var other = _neighborBuffer[i];
            if (ReferenceEquals(other, this)) continue;
            Vector2 diff = pos - other.Position;
            float dist   = diff.magnitude;
            if (dist <= 0f) continue;
            neighborCount++;

            // Linear falloff: 1.0 at personalSpace, 0.0 at separationRadius. Add an extra +1 push
            // when the neighbor is inside personalSpace so close avoidance dominates.
            float falloff = Mathf.Clamp01((separationRadius - dist) / denom);
            if (dist < personalSpace) falloff += 1f;

            sep += (diff / dist) * falloff;
        }
        return Vector2.ClampMagnitude(sep * separationWeight * 0.5f, 1f);
    }

    /// <summary>Backwards-compat overload for subclasses (Archer kite, HorseMan approach) that
    /// don't need the neighbor count. Allocates nothing — just discards the out value.</summary>
    protected Vector2 ComputeSeparation(Vector2 pos) => ComputeSeparation(pos, out _);

    /// <summary>moveSpeed combined with slow, global ramp, and terrain (water) multipliers.</summary>
    protected float ComputeEffectiveSpeed(Vector2 pos)
    {
        float globalMul = enemyManager != null ? enemyManager.GlobalSpeedMul : 1f;
        float waterMul  = WorldManager.Instance != null
            ? WorldManager.Instance.GetMoveSpeedMultiplier(pos)
            : 1f;
        return moveSpeed * _slowFactor * globalMul * waterMul;
    }

    protected Vector2 ResolveBuildingCollision(Vector2 desired)
        => BuildingCollision.Resolve(spatial, desired, obstacleRadius, _obstacleBuffer);

    protected void ApplyFacing(float moveX)
    {
        if (sr != null && Mathf.Abs(moveX) > 0.001f) sr.flipX = moveX < 0f;
    }

    protected void SetWalking(bool walking) => anim?.SetBool(HashWalk, walking);

    /// <summary>Retreat / orbit behavior used during a boss encounter so non-boss enemies form
    /// a loose ring around the player rather than continuing to crowd in.</summary>
    void MoveRetreatOrbit(float dt, Vector2 center, float orbitRadius)
    {
        Vector2 pos = Position;
        Vector2 fromCenter = pos - center;
        float r = fromCenter.magnitude;
        Vector2 radial  = r > 0.001f ? fromCenter / r : Vector2.right;
        Vector2 tangent = new Vector2(-radial.y, radial.x);

        // Pull toward the ring + slow drift around it. radialPull > 0 means we're inside the ring
        // and need to push outward; < 0 means we're past it and need to come back in.
        float ringErr = orbitRadius - r;
        Vector2 ringPull = radial * Mathf.Sign(ringErr) * Mathf.Min(1f, Mathf.Abs(ringErr) * 0.5f);
        Vector2 moveDir = (ringPull + tangent * 0.3f).normalized;

        float effSpeed = ComputeEffectiveSpeed(pos);
        Vector2 desired  = pos + moveDir * effSpeed * dt;
        Vector2 resolved = ResolveBuildingCollision(desired);

        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(moveDir.x);
        SetWalking(true);
    }

    protected virtual void TryAttack()
    {
        // Mooks don't attack during a boss encounter — they're just orbiting at distance.
        // Boss summons (ignoreBossRetreat = true) DO attack normally.
        var encounter = BossEncounter.Instance;
        if (encounter != null && encounter.NonBossRetreat && !ignoreBossRetreat) return;

        float distSqr = ((Vector2)playerTransform.position - Position).sqrMagnitude;
        if (distSqr <= attackRange * attackRange && Time.time >= nextAttackTime)
        {
            nextAttackTime = Time.time + attackCooldown;
            PerformAttack();
        }
    }

    protected virtual void PerformAttack()
    {
        anim?.SetTrigger(HashAttack);
        playerStats?.TakeDamage(ScaleDamage(damage), Position);
    }

    /// <summary>Apply the EnemyManager's player-level damage multiplier to a base damage value.
    /// Subclasses with their own damage numbers (Archer arrow, HorseMan dash) should route their
    /// values through this so milestone scaling affects them too.</summary>
    protected float ScaleDamage(float baseAmount)
    {
        return baseAmount * (enemyManager != null ? enemyManager.GlobalDamageMul : 1f);
    }
}
