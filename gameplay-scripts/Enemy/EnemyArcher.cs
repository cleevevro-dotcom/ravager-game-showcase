using UnityEngine;

public class EnemyArcher : EnemyBase
{
    [Header("Archer Ranged")]
    [SerializeField] float _minRange      = 4.5f;
    [SerializeField] float _arrowSpeed    = 9f;
    [SerializeField] float _arrowDamage   = 6f;
    [SerializeField] float _arrowMaxDist  = 15f;
    [SerializeField] float _arrowHitRadius = 0.45f;

    protected override void Awake()
    {
        base.Awake();
        enemyType       = EnemyType.Archer;
        maxHp           = 22f;
        moveSpeed       = 2.2f;
        damage          = 6f;            // unused; arrow carries its own damage
        attackRange     = 7.5f;          // max firing distance
        attackCooldown  = 3.5f;
        xpValue         = 14f;
        separationRadius = 1f;
        obstacleRadius   = 0.35f;
    }

    // Kite: stand in the band [minRange, attackRange]; back away if too close, approach if too far.
    protected override void MoveTowardPlayer(float dt)
    {
        Vector2 pos       = Position;
        Vector2 toPlayer  = (Vector2)playerTransform.position - pos;
        float   distSqr   = toPlayer.sqrMagnitude;
        Vector2 dirToward = distSqr > 0.0001f ? toPlayer / Mathf.Sqrt(distSqr) : Vector2.right;

        Vector2 desiredDir;
        bool walking;
        if (distSqr < _minRange * _minRange)
        {
            desiredDir = -dirToward;             // too close → kite away
            walking    = true;
        }
        else if (distSqr > attackRange * attackRange)
        {
            desiredDir = dirToward;              // too far → close in
            walking    = true;
        }
        else
        {
            desiredDir = Vector2.zero;           // in the firing band → hold ground
            walking    = false;
        }

        Vector2 sep = ComputeSeparation(pos);
        Vector2 dir = desiredDir + sep;
        if (dir.sqrMagnitude < 0.0001f)
        {
            ApplyFacing(dirToward.x);            // still face the player when not moving
            SetWalking(false);
            return;
        }

        Vector2 moveDir  = dir.normalized;
        float   effSpeed = ComputeEffectiveSpeed(pos);
        Vector2 move     = moveDir * effSpeed * dt;
        Vector2 desired  = pos + move;
        Vector2 resolved = ResolveBuildingCollision(desired);

        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(walking ? move.x : dirToward.x);
        SetWalking(walking);
    }

    protected override void TryAttack()
    {
        if (Time.time < nextAttackTime) return;
        Vector2 toPlayer = (Vector2)playerTransform.position - Position;
        float   distSqr  = toPlayer.sqrMagnitude;
        if (distSqr < _minRange * _minRange) return;
        if (distSqr > attackRange * attackRange) return;

        nextAttackTime = Time.time + attackCooldown;
        FireArrow(toPlayer);
    }

    void FireArrow(Vector2 toPlayer)
    {
        var mgr = enemyManager;
        if (mgr == null) return;
        Vector2 dir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector2.right;
        // Bake the milestone scaling into the arrow's damage at fire time — the arrow itself
        // doesn't know about the EnemyManager once it's launched.
        mgr.SpawnArrow(Position, dir, _arrowSpeed, ScaleDamage(_arrowDamage), _arrowHitRadius, _arrowMaxDist);
        ApplyFacing(dir.x);
    }
}
