using UnityEngine;

public class WaveManager : MonoBehaviour
{
    public static WaveManager Instance { get; private set; }

    [Header("Spawn cadence")]
    [SerializeField] float _initialSpawnInterval = 2f;
    [SerializeField] float _minSpawnInterval     = 0.25f;
    [SerializeField] float _spawnRampTime        = 120f;
    [SerializeField] float _spawnRadius          = 16f;

    [Header("Enemy schedule (seconds)")]
    [Tooltip("Time after which Archers start mixing into spawns alongside Knights.")]
    [SerializeField] float _archerStartTime   = 120f;   // 2 min
    [Tooltip("Time after which HorseMen start mixing into spawns.")]
    [SerializeField] float _horseManStartTime = 0f;   // 6 min

    [Header("Enemy mix weights (relative; renormalized internally)")]
    [SerializeField] float _knightWeight   = 6f;
    [SerializeField] float _archerWeight   = 3f;
    [SerializeField] float _horseManWeight = 1f;

    EnemyManager _enemyManager;
    float _nextSpawnTime;
    float _elapsedTime;

    public float ElapsedTime => _elapsedTime;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Init(EnemyManager enemyManager)
    {
        _enemyManager  = enemyManager;
        _nextSpawnTime = 2f;
    }

    void Update()
    {
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        _elapsedTime += Time.deltaTime;

        if (Time.time < _nextSpawnTime) return;
        SpawnEnemy();
        float t        = Mathf.Clamp01(_elapsedTime / _spawnRampTime);
        float interval = Mathf.Lerp(_initialSpawnInterval, _minSpawnInterval, t);
        _nextSpawnTime = Time.time + interval;
    }

    void SpawnEnemy()
    {
        if (_enemyManager == null) return;
        if (!TryFindSpawnPoint(out Vector2 pos)) return;

        EnemyType type = PickEnemyType();
        _enemyManager.SpawnEnemy(type, pos);
    }

    Camera _camCached;
    Transform _camTransformCached;

    bool TryFindSpawnPoint(out Vector2 pos)
    {
        // Camera.main does a tagged-lookup; cache the reference once and reuse so spawn
        // bursts in late game don't pay the lookup cost on every call. Re-acquire if the
        // cached camera was destroyed (scene reload).
        if (_camCached == null)
        {
            _camCached = Camera.main;
            _camTransformCached = _camCached != null ? _camCached.transform : null;
        }
        Vector2 origin = _camTransformCached != null ? (Vector2)_camTransformCached.position : Vector2.zero;

        for (int i = 0; i < 6; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            pos = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _spawnRadius;
            if (WorldManager.Instance == null || !WorldManager.Instance.IsWaterAt(pos)) return true;
        }
        pos = default;
        return false;
    }

    // Roulette-wheel pick across the currently unlocked enemy types. Falls back to Soldier if
    // a type's pool isn't wired so spawning never silently no-ops.
    EnemyType PickEnemyType()
    {
        float wKnight   = HasPool(EnemyType.Soldier)  ? Mathf.Max(0f, _knightWeight)   : 0f;
        float wArcher   = (_elapsedTime >= _archerStartTime  && HasPool(EnemyType.Archer))   ? Mathf.Max(0f, _archerWeight)   : 0f;
        float wHorseMan = (_elapsedTime >= _horseManStartTime && HasPool(EnemyType.HorseMan)) ? Mathf.Max(0f, _horseManWeight) : 0f;

        float total = wKnight + wArcher + wHorseMan;
        if (total <= 0f) return EnemyType.Soldier;

        float roll = Random.value * total;
        if (roll < wKnight) return EnemyType.Soldier;
        if (roll < wKnight + wArcher) return EnemyType.Archer;
        return EnemyType.HorseMan;
    }

    bool HasPool(EnemyType type) => _enemyManager != null && _enemyManager.HasPoolFor(type);

    /// <summary>True if the given enemy type's unlock time has elapsed. Other systems (the village
    /// tower spawner) call this so they pick from the same time-gated set without forking the
    /// timing logic.</summary>
    public bool IsEnemyUnlocked(EnemyType type)
    {
        switch (type)
        {
            case EnemyType.Soldier:  return true;                              // always available
            case EnemyType.Knight:   return true;                              // reserved alias of Soldier
            case EnemyType.Archer:   return _elapsedTime >= _archerStartTime;
            case EnemyType.HorseMan: return _elapsedTime >= _horseManStartTime;
            default:                 return false;
        }
    }
}
