using System;
using System.Collections.Generic;
using UnityEngine;

public class AxeProjectile : MonoBehaviour
{
    enum Phase : byte { Outbound, Returning }

    Transform _player;
    Vector2   _dir;
    float     _speed;
    float     _damage;
    float     _hitRadius;
    float     _maxDistance;
    int       _bouncesLeft;
    float     _bounceRange;
    Vector2   _origin;
    Phase     _phase;
    float     _spinSpeed = 720f;
    Action    _onReturned;
    float     _maxReturnDistance;   // if distance-to-player exceeds this during return, give up and recycle
    bool      _recycled;            // idempotence guard for ReturnToPool

    DynamicPool<AxeProjectile> _pool;
    SpriteRenderer _sr;

    // Hard wall-clock cap on the returning phase. If the player out-runs the axe (high move-speed
    // buffs, dash, etc.) the axe must never linger trailing behind — it disappears and recycles
    // even if it can't catch up. Prevents the "axe stacks behind player and stays visible" bug.
    const float ReturningMaxSeconds = 2.0f;
    float _returningSince;

    readonly HashSet<EnemyBase>    _hitThisLeg         = new HashSet<EnemyBase>();
    readonly HashSet<Destructible> _buildingsHitThisLeg = new HashSet<Destructible>();
    readonly List<ISpatialEntity>  _hashBuf             = new List<ISpatialEntity>(16);
    readonly List<ISpatialEntity>  _buildingBuf         = new List<ISpatialEntity>(4);
    readonly List<EnemyBase>       _cand                = new List<EnemyBase>(32);

    public void Configure(DynamicPool<AxeProjectile> pool, SpriteRenderer sr, Action onReturned = null)
    {
        _pool       = pool;
        _sr         = sr;
        _onReturned = onReturned;
    }

    public void Launch(Transform player, Vector2 origin, Vector2 dir,
                       float speed, float damage, float hitRadius,
                       float maxDistance, int bounces, float bounceRange,
                       float visualScale, float maxReturnDistance)
    {
        _player            = player;
        _origin            = origin;
        transform.position = origin;
        transform.localScale = Vector3.one * visualScale;
        _dir          = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector2.right;
        _speed        = speed;
        _damage       = damage;
        _hitRadius    = hitRadius;
        _maxDistance  = maxDistance;
        _bouncesLeft  = bounces;
        _bounceRange  = bounceRange;
        _phase        = Phase.Outbound;
        _maxReturnDistance = maxReturnDistance;
        _recycled     = false;
        _returningSince = 0f;
        _hitThisLeg.Clear();
        _buildingsHitThisLeg.Clear();
        if (_sr != null)
        {
            _sr.color   = SkillColors.Axe;
            _sr.enabled = true;
        }
    }

    void Update()
    {
        transform.Rotate(0f, 0f, _spinSpeed * Time.deltaTime);

        if (_phase == Phase.Outbound)
        {
            transform.position += (Vector3)(_dir * _speed * Time.deltaTime);
            float traveled = Vector2.Distance((Vector2)transform.position, _origin);
            if (traveled >= _maxDistance)
            {
                _phase = Phase.Returning;
                _hitThisLeg.Clear();
                _buildingsHitThisLeg.Clear();
            }
        }
        else // Returning
        {
            if (_player == null) { ReturnToPool(); return; }
            _returningSince += Time.deltaTime;
            // Wall-clock timeout: even if the player out-runs the axe, never let it trail forever.
            if (_returningSince >= ReturningMaxSeconds) { ReturnToPool(); return; }
            Vector2 toPlayer = (Vector2)_player.position - (Vector2)transform.position;
            float dist = toPlayer.magnitude;
            // Hard cap: if the axe ends up too far from the player (player fled, pushed off-screen,
            // etc.), recycle immediately instead of tracking forever.
            if (_maxReturnDistance > 0f && dist > _maxReturnDistance) { ReturnToPool(); return; }
            if (dist <= 0.4f) { ReturnToPool(); return; }
            _dir = toPlayer / dist;
            transform.position += (Vector3)(_dir * _speed * Time.deltaTime);
        }

        TryHit();
        TryHitBuildings();
    }

    void TryHit()
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return;
        _hashBuf.Clear();
        hash.Query(SpatialCategory.Enemy, transform.position, _hitRadius, _hashBuf);
        float dmg = _phase == Phase.Returning ? _damage * 2f : _damage;

        for (int i = 0; i < _hashBuf.Count; i++)
        {
            if (_hashBuf[i] is EnemyBase e && !e.IsDead && !_hitThisLeg.Contains(e))
            {
                e.TakeDamage(dmg);
                _hitThisLeg.Add(e);

                if (_phase == Phase.Outbound && _bouncesLeft > 0)
                {
                    var next = FindNextBounce(transform.position);
                    if (next != null)
                    {
                        _bouncesLeft--;
                        _dir   = ((Vector2)next.Position - (Vector2)transform.position).normalized;
                        _origin = transform.position; // reset distance baseline for new leg
                    }
                }
            }
        }
    }

    void TryHitBuildings()
    {
        var hash = SpatialHashManager.Instance;
        if (hash == null) return;

        _buildingBuf.Clear();
        hash.Query(SpatialCategory.Building, transform.position, _hitRadius + 1.5f, _buildingBuf);
        if (_buildingBuf.Count == 0) return;

        float   dmg = _phase == Phase.Returning ? _damage * 2f : _damage;
        Vector2 pos = transform.position;

        for (int i = 0; i < _buildingBuf.Count; i++)
        {
            if (!(_buildingBuf[i] is Destructible d) || d.IsDead) continue;
            if (_buildingsHitThisLeg.Contains(d)) continue;

            if (!BuildingCollision.CircleOverlapsBox(d.Position, d.ObstacleHalfExtents,
                                                     pos, _hitRadius, out Vector2 closest, out _))
                continue;

            d.TakeDamage(dmg);
            _buildingsHitThisLeg.Add(d);

            if (_phase == Phase.Outbound && _bouncesLeft > 0)
            {
                // Bounce off the house — only available when the axe has the bounce skill.
                Vector2 diff   = pos - closest;
                Vector2 normal = diff.sqrMagnitude > 0.0001f ? diff.normalized : -_dir;
                _dir    = Vector2.Reflect(_dir, normal).normalized;
                _origin = pos;
                _bouncesLeft--;
            }
            else if (_phase == Phase.Outbound)
            {
                _phase = Phase.Returning;
                _hitThisLeg.Clear();
                _buildingsHitThisLeg.Clear();
            }
            else
            {
                ReturnToPool();
            }
            return;
        }
    }

    void ReturnToPool()
    {
        if (_recycled) return;
        _recycled = true;
        // Belt-and-suspenders: hide the sprite immediately so there is no chance of a one-frame
        // visible artifact between recycling and the pool's SetActive(false).
        if (_sr != null) _sr.enabled = false;
        _onReturned?.Invoke();
        _pool?.Return(this);
    }

    EnemyBase FindNextBounce(Vector2 origin)
    {
        TargetingUtility.FillEnemiesInRadius(origin, _bounceRange, _cand);
        EnemyBase best = null; float bestSqr = float.MaxValue;
        for (int i = 0; i < _cand.Count; i++)
        {
            var e = _cand[i];
            if (_hitThisLeg.Contains(e)) continue;
            float sqr = ((Vector2)e.Position - origin).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = e; }
        }
        return best;
    }
}