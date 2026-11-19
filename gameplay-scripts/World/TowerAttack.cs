using UnityEngine;

/// <summary>
/// Base class for village tower attacks. Plain C# (not MonoBehaviour) so the manager owns it
/// directly — no extra GameObjects per tower, no extra Update loops. The VillageSpawnManager
/// holds one instance per active village and calls TryFire each detection sweep.
///
/// Subclass contract:
/// - Override OnFire(towerPos, playerPos) to do the actual spawn/damage work.
/// - Return true to consume cooldown, false to leave it untouched (e.g. couldn't pool a slot).
/// </summary>
[System.Serializable]
public abstract class TowerAttack
{
    [Tooltip("Seconds between attacks from this tower.")]
    public float cooldown = 4f;
    [Tooltip("Range (world units) at which the tower starts attacking the player.")]
    public float range    = 16f;
    [Tooltip("Multiplier applied to cooldown when the player is inside the tower's close-danger zone. <1 = tower fires faster when the player is up close. Subclasses may also boost intensity (extra arrows, wider spread).")]
    [Range(0.1f, 1f)] public float dangerCooldownMul = 0.5f;

    [System.NonSerialized] float _nextFireTime;

    public void TryFire(VillageSpawnManager mgr, Vector2 towerPos, Vector2 playerPos, float now, bool danger)
    {
        if (now < _nextFireTime) return;
        if ((playerPos - towerPos).sqrMagnitude > range * range) return;
        if (OnFire(mgr, towerPos, playerPos, danger))
        {
            float cd = danger ? cooldown * Mathf.Max(0.1f, dangerCooldownMul) : cooldown;
            _nextFireTime = now + cd;
        }
    }

    protected abstract bool OnFire(VillageSpawnManager mgr, Vector2 towerPos, Vector2 playerPos, bool danger);

    /// <summary>Returns a fresh instance with the same tunables but its own cooldown timer.
    /// The manager calls this to give each village its own attack state.</summary>
    public abstract TowerAttack Clone();
}

/// <summary>
/// Arrow tower: fires `_arrowsPerVolley` pooled arrows toward the player with a small spread so
/// the volley isn't perfectly predictable. Reuses EnemyManager.SpawnArrow (the same pool used by
/// EnemyArchers), so no new projectile pool is needed.
/// </summary>
[System.Serializable]
public class ArrowTowerAttack : TowerAttack
{
    [Header("Arrow")]
    public float arrowSpeed       = 9f;
    public float arrowDamage      = 7f;
    public float arrowMaxDistance = 20f;
    public float arrowHitRadius   = 0.45f;

    [Header("Spread / Volley")]
    [Tooltip("Half-angle of the spread cone in degrees. Each arrow is jittered within ±this.")]
    public float spreadDegrees    = 9f;
    [Tooltip("Number of arrows per volley. Keep at 1 early game so the player can dodge cleanly.")]
    public int   arrowsPerVolley  = 1;

    [Header("Danger ramp (when player is in tower's close zone)")]
    [Tooltip("Extra arrows added to a volley when the player is inside the tower's danger zone.")]
    public int   dangerExtraArrows = 1;
    [Tooltip("Multiplier applied to spread when in danger. >1 = wider, harder-to-predict cone. Stays dodgeable.")]
    public float dangerSpreadMul   = 1.6f;

    protected override bool OnFire(VillageSpawnManager mgr, Vector2 towerPos, Vector2 playerPos, bool danger)
    {
        var em = EnemyManager.Instance;
        if (em == null) return false;

        Vector2 toPlayer = playerPos - towerPos;
        if (toPlayer.sqrMagnitude < 0.0001f) return false;
        float baseAngle = Mathf.Atan2(toPlayer.y, toPlayer.x);

        int   n      = Mathf.Max(1, arrowsPerVolley + (danger ? Mathf.Max(0, dangerExtraArrows) : 0));
        float spread = danger ? spreadDegrees * Mathf.Max(0.1f, dangerSpreadMul) : spreadDegrees;
        for (int i = 0; i < n; i++)
        {
            float jitter = Random.Range(-spread, spread) * Mathf.Deg2Rad;
            float a = baseAngle + jitter;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            em.SpawnArrow(towerPos, dir, arrowSpeed, arrowDamage, arrowHitRadius, arrowMaxDistance);
        }
        return true;
    }

    public override TowerAttack Clone() => new ArrowTowerAttack
    {
        cooldown = cooldown, range = range, dangerCooldownMul = dangerCooldownMul,
        arrowSpeed = arrowSpeed, arrowDamage = arrowDamage,
        arrowMaxDistance = arrowMaxDistance, arrowHitRadius = arrowHitRadius,
        spreadDegrees = spreadDegrees, arrowsPerVolley = arrowsPerVolley,
        dangerExtraArrows = dangerExtraArrows, dangerSpreadMul = dangerSpreadMul,
    };
}

/// <summary>
/// Fire tower: launches a fireball into the sky that lands near the player after a delay.
/// The "fire" is a pooled FireImpact MonoBehaviour that shows an orange ground telegraph for
/// `_telegraphDuration` seconds, then deals AoE damage in `_radius` and fades. Player has the
/// telegraph window to walk out of the danger circle.
/// </summary>
[System.Serializable]
public class FireTowerAttack : TowerAttack
{
    [Header("Impact")]
    public float impactRadius   = 1.8f;
    public float impactDamage   = 14f;

    [Header("Targeting")]
    [Tooltip("Seconds the orange telegraph circle sits on the ground before the fire actually lands. Player's dodge window.")]
    public float telegraphDuration = 1.0f;
    [Tooltip("Seconds after impact before the marker fades and returns to the pool.")]
    public float postImpactDuration = 0.35f;
    [Tooltip("Random offset (world units) added to the predicted landing point so volleys aren't perfectly accurate.")]
    public float landJitter        = 0.6f;

    [Header("Danger ramp (when player is in tower's close zone)")]
    [Tooltip("Multiplier on telegraphDuration when player is in danger zone. <1 = shorter telegraph window, harder to dodge but still readable.")]
    [Range(0.25f, 1f)] public float dangerTelegraphMul = 0.7f;

    protected override bool OnFire(VillageSpawnManager mgr, Vector2 towerPos, Vector2 playerPos, bool danger)
    {
        if (mgr == null) return false;
        Vector2 jitter = Random.insideUnitCircle * landJitter;
        Vector2 land   = playerPos + jitter;
        float telegraph = danger ? telegraphDuration * Mathf.Max(0.25f, dangerTelegraphMul) : telegraphDuration;
        return mgr.SpawnFireImpact(land, impactRadius, impactDamage, telegraph, postImpactDuration);
    }

    public override TowerAttack Clone() => new FireTowerAttack
    {
        cooldown = cooldown, range = range, dangerCooldownMul = dangerCooldownMul,
        impactRadius = impactRadius, impactDamage = impactDamage,
        telegraphDuration = telegraphDuration, postImpactDuration = postImpactDuration,
        landJitter = landJitter, dangerTelegraphMul = dangerTelegraphMul,
    };
}
