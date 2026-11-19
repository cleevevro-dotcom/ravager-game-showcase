/// <summary>
/// Selectable player monster. Each value maps to a config in <see cref="BeastDatabase"/>
/// and an attack component on the player.
/// </summary>
public enum BeastType : byte
{
    Goblin   = 0,
    Skeleton = 1,
    Slime    = 2,
    Golem    = 3,
    Zombie   = 4,
}
