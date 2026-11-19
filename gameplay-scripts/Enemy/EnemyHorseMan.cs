using UnityEngine;

public class EnemyHorseMan : EnemyBase
{
    enum DashPhase : byte { Approach, Telegraph, Dash, Recover }

    [Header("Charge Dash")]
    [SerializeField] float _triggerRange   = 5f;     // start telegraph when player within this
    [SerializeField] float _telegraphTime  = 0.7f;   // wind-up window — dodge here
    [SerializeField] float _dashTime       = 0.5f;
    [SerializeField] float _dashSpeed      = 11f;
    [SerializeField] float _recoverTime    = 0.9f;
    [SerializeField] float _dashHitRadius  = 1.0f;
    [SerializeField] float _dashDamage     = 18f;
    [SerializeField] Color _telegraphTint  = new Color(1f, 0.55f, 0.55f, 1f);

    [Header("Overshoot")]
    [Tooltip("Extra distance past the player's current position the horse will aim for when dashing.")]
    [SerializeField] float _overshootDistance = 1.5f;

    DashPhase _phase;
    float     _phaseTimer;
    Vector2   _dashDir;
    Vector2   _dashTarget;
    bool      _dashHitConsumed;

    // Suppress base-class status-tint arbitration during telegraph/dash so the dramatic red tint
    // and the post-dash reset don't fight with burn/slow/plague tints.
    protected override bool IsExternallyTinted
        => _phase == DashPhase.Telegraph || _phase == DashPhase.Dash;

    protected override void Awake()
    {
        base.Awake();
        enemyType        = EnemyType.HorseMan;
        maxHp            = 70f;
        moveSpeed        = 5f;
        damage           = 20f;       // unused; dash carries damage
        attackRange      = 1.5f;      // unused
        attackCooldown   = 1.5f;
        xpValue          = 28f;
        separationRadius = 1.5f;
        obstacleRadius   = 0.55f;
    }

    public override void Init(Transform player, PlayerStats stats, SpatialHashManager spatialMgr, EnemyManager mgr)
    {
        base.Init(player, stats, spatialMgr, mgr);
        _phase            = DashPhase.Approach;
        _phaseTimer       = 0f;
        _dashDir          = Vector2.right;
        _dashHitConsumed  = false;
        _dashTarget       = (Vector2)transform.position;
        // Base.Init already restores sr.color to the captured base color, so nothing else needed.
    }

    // Charge FSM lives entirely in MoveTowardPlayer so the base Tick still drives status effects + knockback.
    protected override void MoveTowardPlayer(float dt)
    {
        switch (_phase)
        {
            case DashPhase.Approach:  TickApproach(dt);  break;
            case DashPhase.Telegraph: TickTelegraph(dt); break;
            case DashPhase.Dash:      TickDash(dt);      break;
            case DashPhase.Recover:   TickRecover(dt);   break;
        }
    }

    // HorseMan's only damage source is the dash itself. Disable melee attack.
    protected override void TryAttack() { }

    void TickApproach(float dt)
    {
        Vector2 pos      = Position;
        Vector2 toPlayer = (Vector2)playerTransform.position - pos;
        float   distSqr  = toPlayer.sqrMagnitude;

        if (distSqr <= _triggerRange * _triggerRange)
        {
            EnterTelegraph(toPlayer);
            return;
        }

        Vector2 dirToPlayer = distSqr > 0.0001f ? toPlayer / Mathf.Sqrt(distSqr) : Vector2.right;
        Vector2 sep         = ComputeSeparation(pos);
        Vector2 dir         = dirToPlayer + sep;
        Vector2 moveDir     = dir.sqrMagnitude > 0.0001f ? dir.normalized : dirToPlayer;

        float   effSpeed = ComputeEffectiveSpeed(pos);
        Vector2 move     = moveDir * effSpeed * dt;
        Vector2 desired  = pos + move;
        Vector2 resolved = ResolveBuildingCollision(desired);

        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(move.x);
        SetWalking(true);
    }

    void EnterTelegraph(Vector2 toPlayer)
    {
        _phase           = DashPhase.Telegraph;
        _phaseTimer      = _telegraphTime;
        _dashDir         = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
        _dashHitConsumed = false;
        SetWalking(false);
        ApplyFacing(_dashDir.x);

        // Compute dash target so the dash aims slightly past the player's current position.
        float playerDist = Mathf.Sqrt(toPlayer.sqrMagnitude);
        _dashTarget = (Vector2)transform.position + _dashDir * (playerDist + _overshootDistance);

        if (sr != null) sr.color = _telegraphTint;
    }

    void TickTelegraph(float dt)
    {
        _phaseTimer -= dt;
        if (_phaseTimer <= 0f) EnterDash();
    }

    void EnterDash()
    {
        _phase      = DashPhase.Dash;
        _phaseTimer = _dashTime;
        // Defer to status arbitration once we leave Telegraph — the base color will be re-applied
        // automatically next status tick when no status is active.
    }

    void TickDash(float dt)
    {
        Vector2 pos      = Position;
        float   effSpeed = _dashSpeed * (enemyManager != null ? enemyManager.GlobalSpeedMul : 1f) * SlowFactor;

        // Move toward computed dash target — this creates the overshoot behavior.
        Vector2 newPos   = Vector2.MoveTowards(pos, _dashTarget, effSpeed * dt);
        Vector2 desired  = newPos;
        Vector2 resolved = ResolveBuildingCollision(desired);

        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(_dashDir.x);
        SetWalking(true);

        // One-shot collision damage during dash. Routed through ScaleDamage so player-level
        // milestone scaling applies to the dash hit too.
        if (!_dashHitConsumed && playerTransform != null && playerStats != null)
        {
            Vector2 toPlayer = (Vector2)playerTransform.position - (Vector2)transform.position;
            if (toPlayer.sqrMagnitude <= _dashHitRadius * _dashHitRadius)
            {
                // HorseMan dash hits hard — pass intensityMul=1.5 so the camera shake is a bit
                // stronger than a normal melee hit, sells the impact.
                playerStats.TakeDamage(ScaleDamage(_dashDamage), Position, 1.5f);
                _dashHitConsumed = true;
            }
        }

        // End dash on timeout, when we reached target, or when we slammed into a building (resolved snapped us back).
        _phaseTimer -= dt;
        bool reachedTarget = (resolved - _dashTarget).sqrMagnitude <= 0.0001f;
        bool blocked = (resolved - desired).sqrMagnitude > 0.0001f;
        if (_phaseTimer <= 0f || reachedTarget || blocked) EnterRecover();
    }

    void EnterRecover()
    {
        _phase      = DashPhase.Recover;
        _phaseTimer = _recoverTime;
        SetWalking(false);
    }

    void TickRecover(float dt)
    {
        _phaseTimer -= dt;
        if (_phaseTimer <= 0f) _phase = DashPhase.Approach;
    }

    protected override void Die()
    {
        // Base color reset is handled by the base class (and by Init for the next pool spawn).
        base.Die();
    }
}
