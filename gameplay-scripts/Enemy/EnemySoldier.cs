using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Soldier-specific horde behavior. Front-line soldiers push the player directly; back-line
/// soldiers detect that they're blocked by other enemies in the forward cone and flank to
/// the side instead of compressing the front line. Archers / Horseman / Boss override
/// MoveTowardPlayer themselves and are NOT affected by this — they keep their own behavior.
///
/// Performance:
///   * One spatial-hash query for separation (via base.ComputeSeparation) + one for the
///     forward-blocker cone. Allocation-free after first call (per-instance _flankBuf).
///   * A stable per-spawn `_sideBias` (-1 / +1) avoids jitter when forward-blocker count
///     fluctuates around the threshold — the soldier commits to one flank direction.
/// </summary>
public class EnemySoldier : EnemyBase
{
    [Header("Soldier — flank flow")]
    [Tooltip("How many enemies in the forward cone before this soldier counts as 'blocked' and switches from pushing to flanking sideways.")]
    [SerializeField] int _flankBlockedThreshold = 2;
    [Tooltip("Cosine of the half-cone for the 'in front' check. 0.5 ≈ ±60° cone toward the player. Higher = narrower (only directly-ahead enemies count as blockers).")]
    [Range(0f, 1f)] [SerializeField] float _flankConeDot = 0.45f;
    [Tooltip("Radius (world units) within which to look for forward blockers. Larger = back-line detected from farther back.")]
    [SerializeField] float _flankConeRadius = 2.0f;
    [Tooltip("Side-flow weight when blocked. Higher = sharper flank. Lower = soft drift.")]
    [SerializeField] float _flankSideWeight = 1.3f;
    [Tooltip("Multiplier on chase pressure when blocked. <1 = back-line eases up so it doesn't compress into the front-line.")]
    [Range(0.1f, 1f)] [SerializeField] float _flankChaseDamp = 0.5f;

    // Stable per-spawn left/right pick. Set on Init from random, re-rolled per pool spawn so
    // recycled soldiers don't reuse the previous spawn's bias and the army doesn't all flank
    // the same direction.
    int _sideBias = 1;

    // Reused for the forward-cone spatial-hash query. Pre-sized so a typical horde tick
    // doesn't allocate.
    readonly List<ISpatialEntity> _flankBuf = new List<ISpatialEntity>(12);

    protected override void Awake()
    {
        base.Awake();
        enemyType      = EnemyType.Soldier;
        maxHp          = 30f;
        moveSpeed      = 2f;
        damage         = 8f;
        attackRange    = 0.8f;
        attackCooldown = 1.2f;
        xpValue        = 10f;
    }

    public override void Init(Transform player, PlayerStats stats, SpatialHashManager spatialMgr, EnemyManager mgr)
    {
        base.Init(player, stats, spatialMgr, mgr);
        // Stable per-spawn flank bias. Re-rolled here (not in Awake) so a pooled soldier gets a
        // fresh side each time it leaves the pool.
        _sideBias = Random.value < 0.5f ? -1 : 1;
    }

    /// <summary>
    /// Soldier movement: chase + separation + side-flow + crowd slowdown, with a "front vs back"
    /// gate driven by how many enemies sit in the forward cone toward the player.
    /// </summary>
    protected override void MoveTowardPlayer(float dt)
    {
        // Boss encounters: mooks orbit the arena edge — base handles that path. Boss SUMMONS
        // (Dorado's soldier-circle, ignoreBossRetreat=true) proceed through the flank logic so
        // they actively pressure the player.
        var encounter = BossEncounter.Instance;
        if (encounter != null && encounter.NonBossRetreat && !ignoreBossRetreat) { base.MoveTowardPlayer(dt); return; }
        if (spatial == null || playerTransform == null)    { base.MoveTowardPlayer(dt); return; }

        Vector2 pos       = Position;
        // Same target-offset spreading the base behavior uses — soldiers don't all aim at the
        // exact player center, so the swarm spreads naturally even before flank logic.
        Vector2 targetPos = (Vector2)playerTransform.position + _targetOffset;
        Vector2 toTarget  = targetPos - pos;
        Vector2 dirChase  = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;

        // Separation: standard inter-enemy push from base. Returns neighbor count too.
        Vector2 separation = ComputeSeparation(pos, out int neighborCount);

        // Forward-blocker count: how many enemies sit in the cone toward the player. Front
        // soldiers see ~0 and push hard; back soldiers see several and flank instead.
        int  forwardBlockers = CountForwardBlockers(pos, dirChase);
        bool isBlocked       = forwardBlockers >= _flankBlockedThreshold;

        // Side-flow: stable per-spawn _sideBias keeps the soldier committed to one flank so a
        // back-line soldier on the cusp of the threshold doesn't oscillate left/right each tick.
        // When NOT blocked, fall back to the base soft-horde swirl scaled by local crowding.
        Vector2 perp     = new Vector2(-dirChase.y, dirChase.x);
        float   sideAmt  = isBlocked
            ? _flankSideWeight
            : sideFlowWeight * Mathf.Min(1f, neighborCount * 0.18f);
        Vector2 sideFlow = perp * (_sideBias * sideAmt);

        // Damp chase pressure when blocked so the back line stops shoving the front line into
        // the player. Front line keeps full pressure (chaseW = 1).
        float chaseW = isBlocked ? _flankChaseDamp : 1f;

        Vector2 dir     = dirChase * chaseW + separation + sideFlow;
        Vector2 moveDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : dirChase;

        // Crowd slowdown stays — back-line in a packed cell slows further so two soldiers can't
        // both punch through the front by sheer momentum.
        float crowdSlow = 1f - Mathf.Min(maxCrowdSlowdown, neighborCount * crowdSlowdownStrength);
        float effSpeed  = ComputeEffectiveSpeed(pos) * crowdSlow;

        Vector2 move     = moveDir * effSpeed * dt;
        Vector2 desired  = pos + move;
        Vector2 resolved = ResolveBuildingCollision(desired);

        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
        ApplyFacing(move.x);
        SetWalking(true);
    }

    /// <summary>
    /// One spatial-hash query inside _flankConeRadius, then a per-result dot-product test
    /// against the chase direction. Counts only live enemies in the forward cone. Skips
    /// self and dead entries. Allocation-free after first call.
    /// </summary>
    int CountForwardBlockers(Vector2 pos, Vector2 dirChase)
    {
        spatial.Query(SpatialCategory.Enemy, pos, _flankConeRadius, _flankBuf);
        int count = 0;
        for (int i = 0; i < _flankBuf.Count; i++)
        {
            var ent = _flankBuf[i];
            if (ent == null || ReferenceEquals(ent, this)) continue;
            if (ent is EnemyBase eb && eb.IsDead) continue;

            Vector2 to = ent.Position - pos;
            float distSqr = to.sqrMagnitude;
            if (distSqr <= 0.0001f) continue;
            // dot of the unit vectors (to / |to|) and dirChase (already unit).
            float dot = (to.x * dirChase.x + to.y * dirChase.y) / Mathf.Sqrt(distSqr);
            if (dot >= _flankConeDot) count++;
        }
        return count;
    }
}
