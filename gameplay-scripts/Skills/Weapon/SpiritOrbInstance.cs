using UnityEngine;

/// <summary>
/// One spirit orb. Cycles: Orbit player → fly out and strike a target → return → orbit. State
/// machine kept inside this component so SpiritOrbSkill just ticks the angle.
///
/// Target picking is delegated to the owning <see cref="SpiritOrbSkill"/> so multiple orbs do
/// not all converge on the single nearest enemy. The orb claims a target on the way out and
/// releases it on hit, miss (target died), or when the orb itself is disabled.
/// </summary>
public class SpiritOrbInstance : MonoBehaviour
{
    enum Mode { Orbit, Attack, Return }

    Mode _mode = Mode.Orbit;
    Transform _player;
    SpiritOrbSkill _skill;
    EnemyBase _target;

    public float angleOffsetDeg;
    public float orbitRadius      = 1.8f;
    public float flySpeed         = 7f;
    public float damage           = 18f;
    public float hitRadius        = 0.50f;
    public float attackInterval   = 2f;
    public float searchRange      = 8f;
    float _nextAttackTime;

    public void Bind(Transform player, SpiritOrbSkill skill)
    {
        _player = player;
        _skill  = skill;
    }

    public void Tick(float dt, float orbitAngleDeg)
    {
        if (_player == null) return;

        switch (_mode)
        {
            case Mode.Orbit:
                PlaceOnOrbit(orbitAngleDeg);
                if (Time.time >= _nextAttackTime)
                {
                    _target = _skill != null
                        ? _skill.AcquireTarget(_player.position, searchRange)
                        : TargetingUtility.FindNearestEnemy(_player.position, searchRange);
                    if (_target != null) _mode = Mode.Attack;
                    else _nextAttackTime = Time.time + 0.4f;
                }
                break;

            case Mode.Attack:
                if (_target == null || _target.IsDead)
                {
                    ReleaseTargetIfHeld();
                    _mode = Mode.Return;
                    break;
                }
                Vector2 toTarget = (Vector2)_target.Position - (Vector2)transform.position;
                float dist = toTarget.magnitude;
                if (dist <= hitRadius)
                {
                    _target.TakeDamage(damage);
                    KnockbackUtility.ApplyRadial(_target, transform.position, 1.5f);
                    ReleaseTargetIfHeld();
                    _mode = Mode.Return;
                    _nextAttackTime = Time.time + attackInterval;
                }
                else
                {
                    Vector2 step = (toTarget / Mathf.Max(0.0001f, dist)) * flySpeed * dt;
                    transform.position = (Vector2)transform.position + step;
                }
                break;

            case Mode.Return:
                Vector2 ringPos = ComputeOrbitPos(orbitAngleDeg);
                Vector2 toRing  = ringPos - (Vector2)transform.position;
                float ringDist  = toRing.magnitude;
                if (ringDist <= 0.15f)
                {
                    transform.position = ringPos;
                    _mode = Mode.Orbit;
                }
                else
                {
                    Vector2 step = (toRing / Mathf.Max(0.0001f, ringDist)) * flySpeed * dt;
                    transform.position = (Vector2)transform.position + step;
                }
                break;
        }
    }

    void PlaceOnOrbit(float orbitAngleDeg)
    {
        transform.position = ComputeOrbitPos(orbitAngleDeg);
    }

    void ReleaseTargetIfHeld()
    {
        if (_target != null) _skill?.ReleaseTarget(_target);
        _target = null;
    }

    void OnDisable()
    {
        // Pool / destroy / scene unload — make sure the skill's claim set doesn't leak this
        // orb's reservation.
        ReleaseTargetIfHeld();
    }

    Vector2 ComputeOrbitPos(float orbitAngleDeg)
    {
        float total = (orbitAngleDeg + angleOffsetDeg) * Mathf.Deg2Rad;
        Vector2 origin = _player.position;
        return origin + new Vector2(Mathf.Cos(total), Mathf.Sin(total)) * orbitRadius;
    }
}
