using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single manager that drives "tower spawns soldiers" across every village. One throttled Update
/// loop iterates the 3×3 grid of village centers around the player, checks each for activation
/// range + tower-alive + cooldown, and spawns enemies from the EnemyManager pool at the tower's
/// position. No per-tower MonoBehaviours, no Instantiate during gameplay.
///
/// Timing rule: enemy types are picked only from those WaveManager currently has unlocked, so
/// the tower never spawns archers/horsemen earlier than the normal wave system.
/// </summary>
public class VillageSpawnManager : MonoBehaviour
{
    public static VillageSpawnManager Instance { get; private set; }

    [Header("Detection")]
    [Tooltip("Outer range (world units) at which the tower wakes up and starts spawning soldiers / lobbing arrows. Player can lurk just outside this and not get attacked.")]
    [SerializeField] float _activationRange   = 28f;
    [Tooltip("Inner range. Once the player crosses this, the tower 'enrages': faster cooldowns, denser arrow volleys, shorter fire telegraphs, faster soldier spawns. Should be tighter than _activationRange so there's a transition zone the player can feel.")]
    [SerializeField] float _dangerRange       = 14f;
    [Tooltip("Multiplier applied to per-village soldier spawn interval when the player is in the danger zone. <1 = soldiers come out faster.")]
    [Range(0.2f, 1f)] [SerializeField] float _dangerSpawnIntervalMul = 0.55f;
    [Tooltip("Seconds between detection sweeps. Higher = cheaper but less responsive.")]
    [SerializeField] float _detectionInterval = 0.5f;

    [Header("Spawn cadence (per village)")]
    [Tooltip("Seconds between spawns from a single tower at run start. Eased toward _minSpawnInterval over _spawnRampTime seconds of WaveManager elapsed time.")]
    [SerializeField] float _initialSpawnInterval = 6.0f;
    [SerializeField] float _minSpawnInterval     = 2.0f;
    [SerializeField] float _spawnRampTime        = 240f;
    [Tooltip("Hard cap on how many tower-spawned enemies can be alive at once across all villages. Prevents flooding the screen.")]
    [SerializeField] int   _maxAliveFromTowers   = 8;
    [Tooltip("Random jitter (world units) around the tower position when an enemy spawns.")]
    [SerializeField] float _spawnJitter          = 0.4f;
    [Tooltip("Vertical offset applied to the spawn point so enemies appear to emerge from under the tower's bottom anchor.")]
    [SerializeField] float _spawnYOffset         = -0.2f;

    [Header("Tower-only enemy weights (filtered by WaveManager unlock timing)")]
    [SerializeField] float _soldierWeight  = 6f;
    [SerializeField] float _archerWeight   = 1f;
    [SerializeField] float _horseManWeight = 1f;

    [Header("Tower attacks")]
    [Tooltip("If false, towers don't shoot at the player (only spawn soldiers).")]
    [SerializeField] bool _enableTowerAttacks = true;
    [Tooltip("Run scaling: tower attacks only become active after WaveManager elapsed time crosses this many seconds. Lets early game stay manageable while soldier spawns ramp up first.")]
    [SerializeField] float _attacksStartTime = 60f;
    [SerializeField] ArrowTowerAttack _arrowAttackPrototype = new ArrowTowerAttack();
    [SerializeField] FireTowerAttack  _fireAttackPrototype  = new FireTowerAttack();
    [Tooltip("Hard cap on how many fire impacts can be on the ground at the same time across all villages.")]
    [SerializeField] int _maxActiveFireImpacts = 4;
    [Tooltip("Initial pool size for fire impact markers.")]
    [SerializeField] int _fireImpactPoolSize = 6;

    Transform     _player;
    PlayerStats   _playerStats;
    EnemyManager  _enemyManager;
    float         _nextDetectionTime;

    // Per-village TowerAttack instance (each clone has its own cooldown timer).
    readonly Dictionary<long, TowerAttack> _attackPerVillage = new Dictionary<long, TowerAttack>(8);

    // Fire impact pool + parent transform. Active list is recounted each detection sweep so the
    // cap is always self-correcting (a stuck counter would silently jam the cap forever).
    DynamicPool<FireImpact> _fireImpactPool;
    Transform _fireImpactRoot;
    readonly List<FireImpact> _activeFireImpacts = new List<FireImpact>(8);

    readonly List<Vector2Int>          _nearbyCenters         = new List<Vector2Int>(9);
    readonly Dictionary<long, float>   _nextSpawnTimePerVillage = new Dictionary<long, float>(8);
    readonly HashSet<EnemyBase>        _towerSpawned          = new HashSet<EnemyBase>();
    readonly List<EnemyBase>           _towerSpawnedRemoveBuffer = new List<EnemyBase>(16);
    int                                _aliveFromTowers;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Init(Transform player, PlayerStats playerStats, EnemyManager enemyManager)
    {
        _player       = player;
        _playerStats  = playerStats;
        _enemyManager = enemyManager;

        BuildFireImpactPool();
    }

    void BuildFireImpactPool()
    {
        if (_fireImpactPool != null) return;
        if (_fireImpactRoot == null)
        {
            var go = new GameObject("[FireImpacts]");
            go.transform.SetParent(transform, false);
            _fireImpactRoot = go.transform;
        }
        _fireImpactPool = new DynamicPool<FireImpact>(CreateFireImpact, _fireImpactPoolSize);
    }

    FireImpact CreateFireImpact()
    {
        var go = new GameObject("FireImpact");
        go.transform.SetParent(_fireImpactRoot, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = PlaceholderSprites.GetCircle(64);
        sr.sortingOrder = 6;
        var fi = go.AddComponent<FireImpact>();
        fi.Configure(_fireImpactPool, sr, _playerStats, _player);
        go.SetActive(false);
        return fi;
    }

    /// <summary>Called by FireTowerAttack. Pulls a FireImpact from the pool, respecting the
    /// global cap so simultaneous fire towers don't blanket the screen.</summary>
    public bool SpawnFireImpact(Vector2 landPos, float radius, float damage, float telegraphDur, float postImpact)
    {
        if (_fireImpactPool == null) return false;
        PruneActiveFireImpacts();
        if (_activeFireImpacts.Count >= _maxActiveFireImpacts) return false;

        FireImpact fi = _fireImpactPool.Get(landPos, Quaternion.identity);
        if (fi == null) return false;
        fi.Launch(landPos, radius, damage, telegraphDur, postImpact);
        _activeFireImpacts.Add(fi);
        return true;
    }

    /// <summary>Drops entries that have returned to the pool (gameObject inactive). Called before
    /// each cap check so `_activeFireImpacts.Count` is the true active count.</summary>
    void PruneActiveFireImpacts()
    {
        for (int i = _activeFireImpacts.Count - 1; i >= 0; i--)
        {
            var fi = _activeFireImpacts[i];
            if (fi == null || !fi.gameObject.activeSelf)
                _activeFireImpacts.RemoveAt(i);
        }
    }

    void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        if (_player == null || _enemyManager == null) return;
        if (Time.time < _nextDetectionTime) return;
        _nextDetectionTime = Time.time + _detectionInterval;

        var world = WorldManager.Instance;
        if (world == null || world.VillageRadius <= 0) return;

        RefreshAliveCount();
        if (_aliveFromTowers >= _maxAliveFromTowers) return;

        world.GetNearbyVillageCenters(_player.position, _nearbyCenters);
        if (_nearbyCenters.Count == 0) return;

        Vector2 playerPos     = _player.position;
        float   activationSqr = _activationRange * _activationRange;
        float   dangerSqr     = _dangerRange     * _dangerRange;
        float   tile          = world.TileSize;
        float   interval      = ComputeSpawnInterval();
        float   dangerInterval = interval * Mathf.Max(0.1f, _dangerSpawnIntervalMul);

        bool attacksUnlocked = _enableTowerAttacks
            && (WaveManager.Instance == null || WaveManager.Instance.ElapsedTime >= _attacksStartTime);
        float now = Time.time;

        for (int i = 0; i < _nearbyCenters.Count; i++)
        {
            Vector2Int centerTile = _nearbyCenters[i];
            Vector2 centerPos = new Vector2((centerTile.x + 0.5f) * tile, (centerTile.y + 0.5f) * tile);

            // Activation: player must be within range of the tower, otherwise it sleeps.
            float distToTowerSqr = (centerPos - playerPos).sqrMagnitude;
            if (distToTowerSqr > activationSqr) continue;
            // Tower destroyed by player → no more spawning OR attacks from this village.
            if (world.IsTowerDestroyed(centerTile.x, centerTile.y)) continue;

            // Inside the danger zone the tower enrages: faster soldier spawn cadence, faster
            // tower attacks, denser arrow volleys / shorter fire telegraphs.
            bool inDanger = distToTowerSqr <= dangerSqr;
            float effectiveInterval = inDanger ? dangerInterval : interval;

            long key = PackKey(centerTile.x, centerTile.y);

            // --- Soldier spawn pass (capped globally) ---
            if (_aliveFromTowers < _maxAliveFromTowers)
            {
                _nextSpawnTimePerVillage.TryGetValue(key, out float nextTime);
                if (now >= nextTime)
                {
                    EnemyType type = PickEnemyTypeForTower();
                    if (_enemyManager.HasPoolFor(type))
                    {
                        Vector2 jitter = Random.insideUnitCircle * _spawnJitter;
                        Vector2 spawn  = new Vector2(centerPos.x + jitter.x, centerPos.y + _spawnYOffset + jitter.y);
                        EnemyBase spawned = _enemyManager.SpawnEnemy(type, spawn);
                        if (spawned != null)
                        {
                            _towerSpawned.Add(spawned);
                            _aliveFromTowers++;
                        }
                        _nextSpawnTimePerVillage[key] = now + effectiveInterval;
                    }
                }
            }

            // --- Attack pass (per-village cooldown, only after _attacksStartTime elapsed) ---
            if (attacksUnlocked)
            {
                TowerAttack atk = GetOrCreateAttackForVillage(key, centerTile);
                atk?.TryFire(this, centerPos, playerPos, now, inDanger);
            }
        }
    }

    /// <summary>Returns a per-village TowerAttack instance, deterministically picking either an
    /// arrow tower or a fire tower based on the tile's parity. The first time a village is seen,
    /// we Clone the prototype so each village owns its own cooldown state.</summary>
    TowerAttack GetOrCreateAttackForVillage(long key, Vector2Int centerTile)
    {
        if (_attackPerVillage.TryGetValue(key, out var existing)) return existing;

        // Deterministic pick: low bit of (cx XOR cy) selects arrow vs fire. Same village always
        // gets the same attack type across reloads — feels intentional, not random.
        bool useArrow = ((centerTile.x ^ centerTile.y) & 1) == 0;
        TowerAttack attack = useArrow
            ? _arrowAttackPrototype.Clone()
            : _fireAttackPrototype.Clone();
        _attackPerVillage[key] = attack;
        return attack;
    }

    /// <summary>Picks an unlocked enemy type using tower-biased weights (Soldier-heavy). Falls
    /// back to Soldier if WaveManager is unavailable.</summary>
    EnemyType PickEnemyTypeForTower()
    {
        var wm = WaveManager.Instance;
        bool soldierOK  = wm == null || wm.IsEnemyUnlocked(EnemyType.Soldier);
        bool archerOK   = wm == null || wm.IsEnemyUnlocked(EnemyType.Archer);
        bool horseManOK = wm == null || wm.IsEnemyUnlocked(EnemyType.HorseMan);

        float wSoldier  = soldierOK  ? Mathf.Max(0f, _soldierWeight)  : 0f;
        float wArcher   = archerOK   ? Mathf.Max(0f, _archerWeight)   : 0f;
        float wHorseMan = horseManOK ? Mathf.Max(0f, _horseManWeight) : 0f;
        float total = wSoldier + wArcher + wHorseMan;
        if (total <= 0f) return EnemyType.Soldier;

        float roll = Random.value * total;
        if (roll < wSoldier) return EnemyType.Soldier;
        if (roll < wSoldier + wArcher) return EnemyType.Archer;
        return EnemyType.HorseMan;
    }

    /// <summary>Eases the spawn interval from initial to min over _spawnRampTime seconds of
    /// WaveManager elapsed time. Early game = slow trickle, late game = steady pressure.</summary>
    float ComputeSpawnInterval()
    {
        var wm = WaveManager.Instance;
        if (wm == null) return _initialSpawnInterval;
        float t = Mathf.Clamp01(wm.ElapsedTime / Mathf.Max(0.01f, _spawnRampTime));
        return Mathf.Lerp(_initialSpawnInterval, _minSpawnInterval, t);
    }

    /// <summary>Walk the tower-spawned set, prune entries that died or got recycled by the pool,
    /// and recompute the alive count. O(set size) — set is bounded by _maxAliveFromTowers, so
    /// this is cheap even at every detection sweep.</summary>
    void RefreshAliveCount()
    {
        _aliveFromTowers = 0;
        _towerSpawnedRemoveBuffer.Clear();
        foreach (var e in _towerSpawned)
        {
            if (e == null || e.IsDead || !e.gameObject.activeSelf)
                _towerSpawnedRemoveBuffer.Add(e);
            else
                _aliveFromTowers++;
        }
        for (int i = 0; i < _towerSpawnedRemoveBuffer.Count; i++)
            _towerSpawned.Remove(_towerSpawnedRemoveBuffer[i]);
    }

    static long PackKey(int x, int y)
    {
        unchecked { return ((long)(uint)x << 32) | (uint)y; }
    }
}
