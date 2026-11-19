using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-90)]
public class RunStats : MonoBehaviour
{
    public static RunStats Instance { get; private set; }

    public int Kills              { get; private set; }
    public int BuildingsDestroyed { get; private set; }

    /// <summary>Phase 10: completed mission entries this run (capped to 4 by MissionManager).
    /// Each entry maps directly to <see cref="FirebaseMissionDto"/> for run submission.</summary>
    public List<FirebaseMissionDto> CompletedMissions { get; } = new List<FirebaseMissionDto>();

    PlayerStats _playerStats;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Kills = 0;
    }

    void Start()
    {
        TrySubscribe();
        Destructible.OnAnyDefeated -= HandleBuildingDefeated;
        Destructible.OnAnyDefeated += HandleBuildingDefeated;
    }

    void OnEnable()
    {
        if (Instance == this) TrySubscribe();
    }

    void OnDisable()
    {
        if (_playerStats != null) _playerStats.OnEnemyKilled -= HandleEnemyKilled;
        _playerStats = null;
        Destructible.OnAnyDefeated -= HandleBuildingDefeated;
    }

    void HandleBuildingDefeated(Destructible d)
    {
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
        BuildingsDestroyed++;
    }

    /// <summary>Phase 10: MissionManager appends here on each completed mission (max 4/run).</summary>
    public void RecordCompletedMission(string id, string type, int target, int bloodReward)
    {
        if (CompletedMissions.Count >= 4) return; // hard backstop; MissionManager already gates
        CompletedMissions.Add(new FirebaseMissionDto
        {
            id           = id,
            type         = type,
            target       = target,
            blood_reward = bloodReward,
        });
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void TrySubscribe()
    {
        if (_playerStats != null) return;
        _playerStats = FindObjectOfType<PlayerStats>();
        if (_playerStats != null) _playerStats.OnEnemyKilled += HandleEnemyKilled;
    }

    void HandleEnemyKilled(EnemyBase _)
    {
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
        Kills++;
    }

    public void ResetForNewRun()
    {
        Kills              = 0;
        BuildingsDestroyed = 0;
        CompletedMissions.Clear();
    }
}
