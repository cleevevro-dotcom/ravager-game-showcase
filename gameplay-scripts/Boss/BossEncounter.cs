using UnityEngine;

/// <summary>
/// Director for boss encounters. ENDLESS schedule: bosses spawn at N × <see cref="_bossSpawnInterval"/>
/// seconds (default 360 s ⇒ 6 / 12 / 18 / 24 / 30 / … minutes — never stops). Each successive boss is
/// harder than the previous one (higher HP, more damage, faster cooldowns), clamped so cooldowns never
/// reach zero. If a boss is still alive when the next scheduled time passes, the next spawn is held
/// until that boss dies, then continues the schedule cleanly. On boss death, a burst of pooled XP orbs
/// is dropped around the corpse.
///
/// The encounter freezes player input briefly on spawn and pushes all live mooks to retreat-and-orbit
/// so the boss arena reads cleanly. Boss visuals are read from the EnemyManager's Soldier prefab so
/// the boss carries the same animator the rest of the knights use, just bigger.
/// </summary>
[DefaultExecutionOrder(-30)]
public class BossEncounter : MonoBehaviour
{
    public static BossEncounter Instance { get; private set; }

    [Header("Schedule (endless — boss N spawns at N × interval)")]
    [Tooltip("Seconds between scheduled boss spawns. Boss #1 at 1×interval, boss #2 at 2×interval, and so on. Endless: no upper limit.")]
    [SerializeField] float _bossSpawnInterval = 360f;
    [Tooltip("Optional one-time delay added to the very first spawn (seconds). 0 = first boss at exactly _bossSpawnInterval.")]
    [SerializeField] float _firstSpawnExtraDelay = 0f;

    [Header("Spawn placement")]
    [Tooltip("How far from the player the boss should appear.")]
    [SerializeField] float _spawnDistance = 20f;

    [Header("Intro")]
    [SerializeField] float _introFreezeDuration = 1.0f;
    [Tooltip("Distance non-boss enemies orbit at during the encounter.")]
    [SerializeField] float _retreatRadius = 25f;

    [Header("Boss visuals (optional override)")]
    [Tooltip("Soldier-style prefab used as the boss skin. If left empty, falls back to the EnemyManager's Soldier pool prefab.")]
    [SerializeField] EnemyBase _bossSkinPrefab;

    [Header("Player-level milestone scaling (boss only)")]
    [Tooltip("Boss scaling triggers at every multiple of this player level (default 20: lv20/40/60). Independent of the normal-enemy scaling on EnemyManager.")]
    [SerializeField] int   _bossScalingInterval     = 20;
    [SerializeField] float _bossHpMulPerMilestone   = 0.30f;   // +30% HP per milestone
    [SerializeField] float _bossDamageMulPerMilestone = 0.20f; // +20% damage
    [SerializeField] float _bossCooldownReductionPerMilestone = 0.10f; // -10% cooldown
    [SerializeField] float _bossCooldownFloorMul    = 0.45f;   // can't drop below 45% of base
    [SerializeField] float _bossScalingMaxMul       = 5.0f;    // hard cap on HP/damage stacking

    [Header("Per-spawn-index scaling (composes on top of player-level scaling)")]
    [Tooltip("HP multiplier added per spawn index. Boss #2 is 1+1*x stronger than boss #1, etc.")]
    [SerializeField] float _bossSpawnHpMulPerIndex       = 0.35f;
    [Tooltip("Damage multiplier added per spawn index.")]
    [SerializeField] float _bossSpawnDamageMulPerIndex   = 0.20f;
    [Tooltip("Cooldown reduction per spawn index. Cooldown floor below applies after composition.")]
    [SerializeField] float _bossSpawnCdReductionPerIndex = 0.10f;
    [Tooltip("Hard floor: composed cooldown multiplier never drops below this. Boss attacks must remain readable and dodgeable.")]
    [SerializeField] float _bossSpawnCooldownFloorMul    = 0.45f;
    [Tooltip("Hard cap on composed HP multiplier so endless scaling stays survivable.")]
    [SerializeField] float _bossSpawnHpMaxMul            = 8.0f;
    [Tooltip("Hard cap on composed damage multiplier so endless scaling stays survivable.")]
    [SerializeField] float _bossSpawnDamageMaxMul        = 6.0f;

    [Header("Time-based live scaling (applies to the ACTIVE boss every N seconds of run time)")]
    [Tooltip("Every N seconds of WaveManager.ElapsedTime the live boss gets a damage bump and a cooldown reduction. HP is NOT touched — HP stays at its spawn value.")]
    [SerializeField] float _bossTimeStepSeconds            = 120f;
    [Tooltip("Damage multiplier added per time step. e.g. 0.20 = +20% damage per 120s.")]
    [SerializeField] float _bossTimeDamageMulPerStep       = 0.20f;
    [Tooltip("Cooldown reduction per time step. e.g. 0.08 = -8% cooldown per 120s. Composes with spawn cooldown.")]
    [SerializeField] float _bossTimeCooldownReductionPerStep = 0.08f;
    [Tooltip("Hard cap on the time-step damage multiplier so endless runs stay survivable.")]
    [SerializeField] float _bossTimeDamageMaxMul           = 6f;
    [Tooltip("Hard floor on the composed cooldown multiplier (spawn × time-step). Boss attacks must remain readable.")]
    [SerializeField] float _bossTimeCooldownFloorMul       = 0.30f;

    [Header("XP burst on boss death (uses XpManager pool — no Instantiate)")]
    [SerializeField] int   _bossXpBurstOrbCount   = 12;
    [SerializeField] float _bossXpBurstRadius     = 2.5f;
    [Tooltip("XP per orb. If <= 0, total XP comes from boss.xpValue and is split across the orbs.")]
    [SerializeField] float _bossXpBurstPerOrbXp   = -1f;
    [Tooltip("Multiplies total XP by (1 + bossIndex * bonus) so later bosses drop more.")]
    [SerializeField] bool  _bossXpBurstScaleByIndex = true;
    [SerializeField] float _bossXpBurstIndexBonus   = 0.25f;

    /// <summary>Player-level milestone HP multiplier on the boss (1.0 = no change). Composed with per-spawn-index HP at spawn.</summary>
    public float BossHpMul       { get; private set; } = 1f;
    /// <summary>Player-level milestone damage multiplier (1.0 = no change). Composed with per-spawn-index damage at spawn.</summary>
    public float BossDamageMul   { get; private set; } = 1f;
    /// <summary>Player-level milestone cooldown multiplier (1.0 = no change; lower = faster). Composed with per-spawn-index cooldown at spawn AND read live by the boss controller.</summary>
    public float BossCooldownMul { get; private set; } = 1f;

    /// <summary>Spawn-time cooldown multiplier baked into the active boss — composes the player-level
    /// milestones with the per-spawn-index reduction. This is the BASE value; the live cooldown
    /// (<see cref="ActiveBossCooldownMul"/>) further composes the time-step reduction on top of it
    /// each frame so it lowers as the run goes on.</summary>
    float _spawnCooldownMul = 1f;

    /// <summary>Spawn-time damage multiplier baked into the active boss. Per-spawn (Boss #2/#3)
    /// damage already multiplies the boss's contact <c>damage</c> field, but spin/slash use
    /// constants — we re-apply this multiplier live so those attacks also scale per-spawn.</summary>
    float _spawnDamageMul   = 1f;

    /// <summary>0-based index of the currently active boss in the schedule, or -1 if no boss is active.</summary>
    public int   ActiveBossIndex       { get; private set; } = -1;

    /// <summary>Number of completed time-steps (every <see cref="_bossTimeStepSeconds"/>) since run start.
    /// Used by both the live cooldown and the live damage multiplier so they advance together.</summary>
    int CurrentTimeStepCount
    {
        get
        {
            float interval = Mathf.Max(1f, _bossTimeStepSeconds);
            float elapsed  = WaveManager.Instance != null ? WaveManager.Instance.ElapsedTime : 0f;
            if (elapsed <= 0f) return 0;
            return Mathf.FloorToInt(elapsed / interval);
        }
    }

    /// <summary>Live damage multiplier applied to the ACTIVE boss's attacks. Grows by
    /// <see cref="_bossTimeDamageMulPerStep"/> every <see cref="_bossTimeStepSeconds"/> seconds of
    /// run time, capped at <see cref="_bossTimeDamageMaxMul"/>. HP is intentionally NOT scaled here —
    /// only outgoing damage. Boss controllers multiply their per-tick / per-strike damage by this.</summary>
    public float ActiveBossDamageMul
    {
        get
        {
            if (!IsActive) return 1f;
            float timeMul = 1f + CurrentTimeStepCount * _bossTimeDamageMulPerStep;
            timeMul = Mathf.Min(_bossTimeDamageMaxMul, timeMul);
            // Compose with the spawn-baked damage multiplier so later boss spawns also hit
            // proportionally harder on spin / slash (which use constants).
            return _spawnDamageMul * timeMul;
        }
    }

    /// <summary>Live cooldown multiplier read by the boss at attack-scheduling time. Composes the
    /// spawn-time cooldown (player-level × per-spawn-index) with the per-step time reduction so
    /// every <see cref="_bossTimeStepSeconds"/> the boss casts faster. Clamped to
    /// <see cref="_bossTimeCooldownFloorMul"/> so attacks always remain readable.</summary>
    public float ActiveBossCooldownMul
    {
        get
        {
            if (!IsActive) return 1f;
            float timeMul = 1f - CurrentTimeStepCount * _bossTimeCooldownReductionPerStep;
            float composed = _spawnCooldownMul * timeMul;
            return Mathf.Max(_bossTimeCooldownFloorMul, composed);
        }
    }

    Transform           _playerTransform;
    PlayerStats         _playerStats;
    SpatialHashManager  _spatial;
    EnemyManager        _enemyManager;
    PlayerStats         _subscribedStats;

    BossGeneralDorado _activeBoss;
    float             _introEndTime;
    int               _nextScheduleIndex;       // 0-based index of the NEXT boss to spawn (endless)
    int               _lastSpawnedIndex = -1;   // 0-based index of the most recently spawned boss

    /// <summary>Elapsed-time threshold (seconds) at which the next scheduled boss should spawn.
    /// Endless: boss #N spawns at N × interval, where #1 is index 0 (so N = index + 1).
    /// First spawn additionally adds <see cref="_firstSpawnExtraDelay"/>.</summary>
    float NextSpawnTime
    {
        get
        {
            float baseTime = (_nextScheduleIndex + 1) * Mathf.Max(1f, _bossSpawnInterval);
            return _nextScheduleIndex == 0 ? baseTime + _firstSpawnExtraDelay : baseTime;
        }
    }

    public bool IsActive          => _activeBoss != null && !_activeBoss.IsDead;
    public bool PlayerLocked      => IsActive && Time.time < _introEndTime;
    public bool NonBossRetreat    => IsActive;
    public Vector2 RetreatCenter  { get; private set; }
    public float   RetreatRadius  => _retreatRadius;

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

    public void Init(Transform player, PlayerStats stats, SpatialHashManager spatial, EnemyManager mgr)
    {
        _playerTransform = player;
        _playerStats     = stats;
        _spatial         = spatial;
        _enemyManager    = mgr;

        // Subscribe to player-level milestones for boss-only scaling. This is INDEPENDENT of the
        // normal-enemy ramp on EnemyManager — boss has higher steps but only every 20 levels.
        if (_subscribedStats != null) _subscribedStats.OnLevelUp -= OnPlayerLevelUp;
        _subscribedStats = stats;
        if (stats != null) stats.OnLevelUp += OnPlayerLevelUp;
    }

    /// <summary>Boss-only scaling: bumps HP / damage / cooldown multipliers at every
    /// _bossScalingInterval milestone (default 20: lv20/40/60). Applied at next SpawnBoss().
    /// Does not retroactively patch a live boss.</summary>
    void OnPlayerLevelUp(int level)
    {
        if (_bossScalingInterval <= 0) return;
        if (level <= 0 || level % _bossScalingInterval != 0) return;

        int milestoneCount = level / _bossScalingInterval;
        BossHpMul       = Mathf.Min(_bossScalingMaxMul, 1f + milestoneCount * _bossHpMulPerMilestone);
        BossDamageMul   = Mathf.Min(_bossScalingMaxMul, 1f + milestoneCount * _bossDamageMulPerMilestone);
        BossCooldownMul = Mathf.Max(_bossCooldownFloorMul, 1f - milestoneCount * _bossCooldownReductionPerMilestone);

        Debug.LogFormat("[BossScaling] Player reached lv{0}, boss hardened: HP×{1:F2} DMG×{2:F2} CD×{3:F2}",
            level, BossHpMul, BossDamageMul, BossCooldownMul);
    }

    void Update()
    {
        // Endless schedule: cheap branch — no per-frame allocation, no spatial query, no array work.
        // We early-out the moment one of: not playing, no wave manager, boss alive (next spawn is held),
        // or current elapsed-time hasn't reached NextSpawnTime yet.
        if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
        if (WaveManager.Instance == null) return;
        if (IsActive) return;   // hold the schedule while the current boss is alive
        if (WaveManager.Instance.ElapsedTime < NextSpawnTime) return;

        SpawnBoss(_nextScheduleIndex);
    }

    public void SpawnBoss(int bossIndex)
    {
        if (IsActive || _playerTransform == null) return;
        if (bossIndex < 0) return;

        EnemyBase skin = ResolveSkinPrefab();
        if (skin == null)
        {
            Debug.LogWarning("[BossEncounter] No soldier prefab available — boss can't spawn.");
            return;
        }

        Vector2 playerPos = _playerTransform.position;
        Vector2 spawnPos  = playerPos + Random.insideUnitCircle.normalized * _spawnDistance;

        // Compose the per-spawn-index scaling on top of player-level milestone scaling. Cooldown
        // reduction is FLOOR-CLAMPED so even very-late bosses still have readable attack timings —
        // pressure, not unavoidable damage. HP / damage caps are independent so we can tune them
        // separately as the run pushes deeper into endless territory.
        //
        //   hpMul       = clamp(1 + bossIndex * hpIncreasePerBoss,        max = _bossSpawnHpMaxMul)
        //   damageMul   = clamp(1 + bossIndex * damageIncreasePerBoss,    max = _bossSpawnDamageMaxMul)
        //   cooldownMul = clamp(1 - bossIndex * cooldownReductionPerBoss, min = _bossSpawnCooldownFloorMul)
        float spawnHpMul       = Mathf.Min(_bossSpawnHpMaxMul,            1f + bossIndex * _bossSpawnHpMulPerIndex);
        float spawnDamageMul   = Mathf.Min(_bossSpawnDamageMaxMul,        1f + bossIndex * _bossSpawnDamageMulPerIndex);
        float spawnCooldownMul = Mathf.Max(_bossSpawnCooldownFloorMul,    1f - bossIndex * _bossSpawnCdReductionPerIndex);

        float composedHpMul       = Mathf.Min(_bossSpawnHpMaxMul,         BossHpMul     * spawnHpMul);
        float composedDamageMul   = Mathf.Min(_bossSpawnDamageMaxMul,     BossDamageMul * spawnDamageMul);
        float composedCooldownMul = Mathf.Max(_bossSpawnCooldownFloorMul, BossCooldownMul * spawnCooldownMul);

        // Instantiate the soldier prefab as a fresh GameObject, strip its EnemySoldier behavior
        // (so it doesn't drive normal soldier logic), then graft the boss controller on top.
        var inst = Instantiate(skin.gameObject, spawnPos, Quaternion.identity);
        inst.name = $"Boss_GeneralDorado_#{bossIndex + 1}";

        var soldier = inst.GetComponent<EnemySoldier>();
        if (soldier != null) DestroyImmediate(soldier);

        // Keep soldier sprite/animator; the boss uses them at 3× scale.
        inst.transform.localScale *= 3f;

        var dorado = inst.AddComponent<BossGeneralDorado>();
        // Bake stats onto the boss BEFORE Init runs — Init reads maxHp into currentHp.
        // attackCooldown is the legacy contact-attack cooldown; phase cooldowns inside
        // BossGeneralDorado read ActiveBossCooldownMul live so they get the same scaling.
        dorado.maxHp          *= composedHpMul;
        dorado.damage         *= composedDamageMul;
        dorado.attackCooldown *= composedCooldownMul;
        dorado.Init(_playerTransform, _playerStats, _spatial, _enemyManager);

        // The boss is non-pooled — register it manually so EnemyManager's tick loop drives it.
        _enemyManager?.RegisterStandalone(dorado);

        _activeBoss            = dorado;
        ActiveBossIndex        = bossIndex;
        _spawnCooldownMul      = composedCooldownMul;
        _spawnDamageMul        = composedDamageMul;
        RetreatCenter          = playerPos;
        _introEndTime          = Time.time + _introFreezeDuration;
        _lastSpawnedIndex      = bossIndex;
        _nextScheduleIndex     = bossIndex + 1;

        Debug.LogFormat("[BossEncounter] Boss #{0} spawned at t={1:F1}s — HP×{2:F2} DMG×{3:F2} CD×{4:F2}",
            bossIndex + 1,
            WaveManager.Instance != null ? WaveManager.Instance.ElapsedTime : -1f,
            composedHpMul, composedDamageMul, composedCooldownMul);
    }

    EnemyBase ResolveSkinPrefab()
    {
        if (_bossSkinPrefab != null) return _bossSkinPrefab;
        if (_enemyManager  != null) return _enemyManager.GetPrefabFor(EnemyType.Soldier);
        return null;
    }

    /// <summary>Called by BossGeneralDorado.Die() before it tears itself down. Drops a burst of
    /// XP orbs around the boss using the existing XpManager pool. Schedule index is already
    /// advanced in SpawnBoss; just clears the active reference so the next scheduled spawn fires.</summary>
    public void NotifyBossDied()
    {
        if (_activeBoss != null) DropXpBurst(_activeBoss.Position, _activeBoss.xpValue, _lastSpawnedIndex);
        _activeBoss           = null;
        ActiveBossIndex       = -1;
        _spawnCooldownMul     = 1f;
        _spawnDamageMul       = 1f;
    }

    void DropXpBurst(Vector2 center, float bossXpValue, int bossIndex)
    {
        if (XpManager.Instance == null) return;
        int   count       = Mathf.Max(1, _bossXpBurstOrbCount);
        float perOrb;
        if (_bossXpBurstPerOrbXp > 0f)
        {
            perOrb = _bossXpBurstPerOrbXp;
        }
        else
        {
            float total = bossXpValue;
            if (_bossXpBurstScaleByIndex && bossIndex >= 0)
                total *= 1f + bossIndex * _bossXpBurstIndexBonus;
            perOrb = total / count;
        }

        // Place orbs in a ring with small jitter so they don't all stack at the boss center.
        // No Instantiate/Destroy — XpManager.SpawnOrb pulls from the existing orb pool.
        float twoPi = Mathf.PI * 2f;
        for (int i = 0; i < count; i++)
        {
            float angle  = twoPi * (i / (float)count) + Random.Range(-0.15f, 0.15f);
            float r      = _bossXpBurstRadius * Random.Range(0.6f, 1.0f);
            Vector2 pos  = center + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
            XpManager.Instance.SpawnOrb(pos, perOrb);
        }
    }
}
