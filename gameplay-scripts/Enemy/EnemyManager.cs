using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [Serializable]
    public class EnemyPoolConfig
    {
        public EnemyType type;
        public EnemyBase prefab;
        public int initialPoolSize = 50;
    }

    [Header("Enemy pools — wire one config per spawnable enemy type.")]
    [SerializeField] EnemyPoolConfig[] _enemyConfigs = Array.Empty<EnemyPoolConfig>();
    [SerializeField] int _maxActiveEnemies = 200;

    [Header("Despawn")]
    [Tooltip("Distance from the player at which an active enemy gets recycled silently (no XP). Set high enough that on-screen enemies are never recycled.")]
    [SerializeField] float _recycleDistance = 45f;

    [Header("Performance")]
    [Tooltip("Max enemies to tick movement per frame. Status effects (burn/stun/slow) always update. Reduces frame spikes at high counts.")]
    [SerializeField] int _movementTickBudget = 40;

    [Header("Animation LOD")]
    [Tooltip("Distance from player below which enemies run full-speed Animator. (Near bucket)")]
    [SerializeField] float _lodNearDistance  = 6f;
    [Tooltip("Distance from player below which enemies run reduced-speed Animator. (Mid bucket) Beyond this, Animator runs at the Far speed if still on-screen, or is disabled entirely off-screen.")]
    [SerializeField] float _lodMidDistance   = 12f;
    [Tooltip("Animator.speed multiplier for Mid-distance enemies.")]
    [Range(0f, 1f)] [SerializeField] float _midAnimSpeed = 0.5f;
    [Tooltip("Animator.speed multiplier for Far enemies (still on-screen). 0 freezes the frame, 0.15 keeps a slow shamble.")]
    [Range(0f, 1f)] [SerializeField] float _farAnimSpeed = 0.15f;
    [Tooltip("Extra padding (world units) added to the camera rect before an off-screen enemy is treated as Off LOD. Avoids popping at the edge.")]
    [SerializeField] float _offScreenPadding = 1.5f;

    [Header("Speed ramp")]
    [SerializeField] float _speedRampPerMinute = 0.05f;  // +5% per elapsed minute
    [SerializeField] float _speedRampMaxMul    = 2.5f;   // hard cap so enemies stay catchable

    [Header("Archer Arrow Pool")]
    [SerializeField] int _arrowInitialPool = 24;

    [Header("Player-level milestone scaling (normal enemies only)")]
    [Tooltip("Player levels at multiples of this number trigger a scaling step on Soldier/Knight/Archer/HorseMan. Boss has its own separate scaling on BossEncounter.")]
    [SerializeField] int   _normalScalingInterval     = 10;
    [SerializeField] float _hpMulPerMilestone         = 0.20f;   // +20% HP per milestone
    [SerializeField] float _damageMulPerMilestone     = 0.15f;   // +15% damage
    [SerializeField] float _xpMulPerMilestone         = 0.10f;   // +10% XP reward
    [Tooltip("Hard cap on cumulative scaling so end-game enemies stay tractable.")]
    [SerializeField] float _scalingMaxMul             = 4.0f;

    /// <summary>Global multiplier applied on top of each enemy's base moveSpeed. Ramps over time.</summary>
    public float GlobalSpeedMul  { get; private set; } = 1f;
    /// <summary>Multiplier on base maxHp for new spawns. Bumped at every player-level milestone (10/20/30...).</summary>
    public float GlobalHpMul     { get; private set; } = 1f;
    /// <summary>Multiplier on enemy damage (melee + projectile) for new spawns and live attacks.</summary>
    public float GlobalDamageMul { get; private set; } = 1f;
    /// <summary>Multiplier on XP orb value when a normal enemy dies.</summary>
    public float GlobalXpMul     { get; private set; } = 1f;

    Transform _playerTransform;
    PlayerStats _playerStats;
    SpatialHashManager _spatial;

    readonly Dictionary<EnemyType, ObjectPool<EnemyBase>> _pools = new Dictionary<EnemyType, ObjectPool<EnemyBase>>(8);
    readonly List<EnemyBase> _activeEnemies = new List<EnemyBase>();
    readonly List<EnemyBase> _toRemove      = new List<EnemyBase>();
    readonly List<EnemyBase> _toRecycle     = new List<EnemyBase>();
    int _movementTickOffset;

    Transform _arrowParent;
    DynamicPool<EnemyArrowProjectile> _arrowPool;

    public int ActiveCount => _activeEnemies.Count;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (_subscribedStats != null) _subscribedStats.OnLevelUp -= OnPlayerLevelUp;
        _subscribedStats = null;
        if (Instance == this) Instance = null;
    }

    public void Init(Transform player, PlayerStats stats, SpatialHashManager spatial)
    {
        _playerTransform = player;
        _playerStats     = stats;
        _spatial         = spatial;

        // Subscribe to player-level milestones for normal-enemy scaling. Re-subscribing
        // safely if Init runs more than once (scene reload / soft reset).
        if (_subscribedStats != null) _subscribedStats.OnLevelUp -= OnPlayerLevelUp;
        _subscribedStats = stats;
        if (stats != null) stats.OnLevelUp += OnPlayerLevelUp;

        BuildEnemyPools();
        BuildArrowPool();
    }

    /// <summary>Called by PlayerStats.OnLevelUp. Bumps global scaling multipliers only at every
    /// _normalScalingInterval milestone (default: 10/20/30/40...). New enemy spawns pick up the
    /// new values via EnemyBase.Init; already-active enemies keep their current stats.</summary>
    void OnPlayerLevelUp(int level)
    {
        if (_normalScalingInterval <= 0) return;
        if (level <= 0 || level % _normalScalingInterval != 0) return;

        int milestoneCount = level / _normalScalingInterval;
        GlobalHpMul     = Mathf.Min(_scalingMaxMul, 1f + milestoneCount * _hpMulPerMilestone);
        GlobalDamageMul = Mathf.Min(_scalingMaxMul, 1f + milestoneCount * _damageMulPerMilestone);
        GlobalXpMul     = Mathf.Min(_scalingMaxMul, 1f + milestoneCount * _xpMulPerMilestone);

        Debug.LogFormat("[EnemyScaling] Player reached lv{0}, normal enemies hardened: HP×{1:F2} DMG×{2:F2} XP×{3:F2}",
            level, GlobalHpMul, GlobalDamageMul, GlobalXpMul);
    }

    PlayerStats _subscribedStats;

    void BuildEnemyPools()
    {
        for (int i = 0; i < _enemyConfigs.Length; i++)
        {
            var c = _enemyConfigs[i];
            if (c == null || c.prefab == null) continue;
            if (_pools.ContainsKey(c.type)) continue;     // first wins; ignore duplicates
            _pools[c.type] = new ObjectPool<EnemyBase>(c.prefab, Mathf.Max(0, c.initialPoolSize), transform);
        }
    }

    void BuildArrowPool()
    {
        if (_arrowParent == null)
        {
            var go = new GameObject("[EnemyArrows]");
            go.transform.SetParent(transform, false);
            _arrowParent = go.transform;
        }
        _arrowPool = new DynamicPool<EnemyArrowProjectile>(CreateArrowInstance, _arrowInitialPool);
    }

    EnemyArrowProjectile CreateArrowInstance()
    {
        var go = new GameObject("EnemyArrow");
        go.transform.SetParent(_arrowParent, false);
        go.AddComponent<SpriteRenderer>();
        var arrow = go.AddComponent<EnemyArrowProjectile>();
        go.SetActive(false);
        return arrow;
    }

    void Update()
    {
        ProcessRemovals();
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        UpdateGlobalSpeed();
        TickEnemies();
    }

    void UpdateGlobalSpeed()
    {
        if (WaveManager.Instance == null) return;
        int minutes = Mathf.FloorToInt(WaveManager.Instance.ElapsedTime / 60f);
        GlobalSpeedMul = Mathf.Min(_speedRampMaxMul, 1f + minutes * _speedRampPerMinute);
    }

    // All enemies update status effects every frame (burns, stuns, slows).
    // Movement (spatial queries, collisions) is throttled to _movementTickBudget per frame to
    // avoid lag spikes at high counts. Distant enemies are recycled silently.
    //
    // Animation LOD is folded into this same loop: we already pay for distance-to-player here,
    // so per-enemy LOD assignment is just a couple of compares and (rarely) one Animator write
    // when the bucket transitions. Camera bounds are sampled ONCE per frame, never per enemy.
    void TickEnemies()
    {
        int n = _activeEnemies.Count;
        if (n == 0) return;

        float dt = Time.deltaTime;
        bool checkRecycle = _playerTransform != null && _recycleDistance > 0f;
        Vector2 playerPos = _playerTransform != null ? (Vector2)_playerTransform.position : Vector2.zero;
        float recycleSqr = _recycleDistance * _recycleDistance;

        int movementBudget = Mathf.Min(_movementTickBudget, n);
        _movementTickOffset %= n;

        // Per-frame camera-bounds snapshot for off-screen LOD culling. One Camera.main lookup +
        // 4 floats; the per-enemy hot path is just 4 comparisons against these.
        var cam = Camera.main;
        bool   haveCamera = cam != null && cam.orthographic;
        float  xMin = 0f, xMax = 0f, yMin = 0f, yMax = 0f;
        if (haveCamera)
        {
            Vector3 cp    = cam.transform.position;
            float   halfH = cam.orthographicSize + _offScreenPadding;
            float   halfW = halfH * cam.aspect;
            xMin = cp.x - halfW; xMax = cp.x + halfW;
            yMin = cp.y - halfH; yMax = cp.y + halfH;
        }
        float lodNearSqr = _lodNearDistance * _lodNearDistance;
        float lodMidSqr  = _lodMidDistance  * _lodMidDistance;

        for (int i = 0; i < n; i++)
        {
            EnemyBase e = _activeEnemies[i];
            if (e == null || e.IsDead) continue;

            Vector2 ep = (Vector2)e.transform.position;

            if (checkRecycle)
            {
                float dxr = ep.x - playerPos.x;
                float dyr = ep.y - playerPos.y;
                if (dxr * dxr + dyr * dyr > recycleSqr)
                {
                    _toRecycle.Add(e);
                    continue;
                }
            }

            // Single distance-to-player computation drives both movement throttling and LOD.
            float distToPlayerSqr = float.MaxValue;
            if (_playerTransform != null)
            {
                float dx = ep.x - playerPos.x;
                float dy = ep.y - playerPos.y;
                distToPlayerSqr = dx * dx + dy * dy;
            }

            // Animation LOD. Boss / exempt enemies stay at their controller defaults.
            if (!e.IsLodExempt)
            {
                EnemyBase.AnimLOD lod;
                if      (distToPlayerSqr <= lodNearSqr) lod = EnemyBase.AnimLOD.Near;
                else if (distToPlayerSqr <= lodMidSqr)  lod = EnemyBase.AnimLOD.Mid;
                else                                    lod = EnemyBase.AnimLOD.Far;

                // Off-screen overrides Far/Mid only — Near always animates so anything close to
                // the player is fully alive even if the camera is panned aside (shouldn't happen
                // but cheap to guarantee).
                if (lod != EnemyBase.AnimLOD.Near && haveCamera)
                {
                    if (ep.x < xMin || ep.x > xMax || ep.y < yMin || ep.y > yMax)
                        lod = EnemyBase.AnimLOD.Off;
                }

                e.ApplyAnimLOD(lod, _midAnimSpeed, _farAnimSpeed);
            }

            // Movement budget: closer enemies always tick movement; the rest share a rotating
            // budget so a distant horde can't spike the frame.
            bool doFullTick = false;
            if (movementBudget > 0)
            {
                doFullTick = distToPlayerSqr < 400f
                          || ((_movementTickOffset + i) % n < movementBudget);
            }

            if (doFullTick)
                e.Tick(dt);
            else
                e.TickStatusEffectsOnly(dt);
        }

        _movementTickOffset = (_movementTickOffset + movementBudget) % Mathf.Max(1, n);
    }

    void ProcessRemovals()
    {
        for (int i = 0; i < _toRecycle.Count; i++) ReleaseToPool(_toRecycle[i], remove: true);
        _toRecycle.Clear();

        for (int i = 0; i < _toRemove.Count; i++)  ReleaseToPool(_toRemove[i],  remove: true);
        _toRemove.Clear();
    }

    void ReleaseToPool(EnemyBase e, bool remove)
    {
        if (e == null) return;
        if (remove) _activeEnemies.Remove(e);
        e.gameObject.SetActive(false);
        if (_pools.TryGetValue(e.enemyType, out var pool)) pool.Return(e);
    }

    public EnemyBase SpawnEnemy(EnemyType type, Vector2 position)
    {
        if (_activeEnemies.Count >= _maxActiveEnemies) return null;
        if (!_pools.TryGetValue(type, out var pool)) return null;

        EnemyBase enemy = pool.Get(Vector3.zero, Quaternion.identity);
        if (enemy == null) return null;
        enemy.transform.position = position;
        enemy.Init(_playerTransform, _playerStats, _spatial, this);
        _activeEnemies.Add(enemy);
        return enemy;
    }

    public bool HasPoolFor(EnemyType type) => _pools.ContainsKey(type);

    /// <summary>Returns the prefab configured for this enemy type, or null if none. Used by
    /// the BossEncounter to build a soldier-skinned boss without registering it in the pool.</summary>
    public EnemyBase GetPrefabFor(EnemyType type)
    {
        for (int i = 0; i < _enemyConfigs.Length; i++)
        {
            var c = _enemyConfigs[i];
            if (c != null && c.type == type) return c.prefab;
        }
        return null;
    }

    /// <summary>Add a non-pooled enemy (e.g. a boss) to the tick list. Bosses bypass _maxActiveEnemies
    /// so they're never refused. ReturnEnemy still works for cleanup; ReleaseToPool just deactivates
    /// since the boss has no pool entry.</summary>
    public void RegisterStandalone(EnemyBase enemy)
    {
        if (enemy == null || _activeEnemies.Contains(enemy)) return;
        _activeEnemies.Add(enemy);
    }

    public void ReturnEnemy(EnemyBase enemy) => _toRemove.Add(enemy);

    public List<EnemyBase> GetActiveEnemies() => _activeEnemies;

    // --- Arrow projectile pooling (used by EnemyArcher) ---

    public void SpawnArrow(Vector2 origin, Vector2 dir, float speed, float damage, float hitRadius, float maxDistance)
    {
        if (_playerTransform == null || _playerStats == null || _arrowPool == null) return;
        EnemyArrowProjectile arrow = _arrowPool.Get(origin, Quaternion.identity);
        if (arrow == null) return;
        arrow.Launch(this, _playerTransform, _playerStats, origin, dir, speed, damage, hitRadius, maxDistance);
    }

    public void ReturnArrow(EnemyArrowProjectile arrow) => _arrowPool?.Return(arrow);
}
