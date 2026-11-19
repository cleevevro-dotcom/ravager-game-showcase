/// <summary>
/// Mission categories. Each category resolves to a different progress-tracking event:
/// kill-counts subscribe to PlayerStats.OnEnemyKilled, building-destroys subscribe to
/// Destructible.OnAnyDefeated, and survive missions just sample WaveManager.ElapsedTime.
/// </summary>
public enum MissionType : byte
{
    KillSoldiers,
    KillArchers,
    KillAny,
    DestroyTowers,
    DestroyBuildings,   // tower OR house
    SurviveSeconds,
}
