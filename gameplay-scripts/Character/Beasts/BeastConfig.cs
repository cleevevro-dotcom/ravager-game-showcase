using UnityEngine;

/// <summary>
/// Per-beast tuning — the values that differentiate Goblin/Skeleton/Slime/Golem/Zombie.
/// Multiplicative on the base PlayerStats (which always start at design defaults) so changing
/// beast at runtime stays clean.
///
/// Display fields (description, attack style) live here too so the Mutation panel can render
/// beast stats without depending on the live shop catalog — important for offline browsing
/// of LOCKED beasts the player doesn't own yet.
/// </summary>
public readonly struct BeastConfig
{
    public readonly BeastType type;
    public readonly string    displayName;

    public readonly float hpMul;          // 1.0 = baseline
    public readonly float damageMul;
    public readonly float moveSpeedMul;
    public readonly float scaleMul;
    public readonly float lifePerKillBonus; // flat add to PlayerStats.lifePerKill

    public readonly string description;
    public readonly string attackStyle;

    public BeastConfig(
        BeastType type, string displayName,
        float hpMul, float damageMul, float moveSpeedMul,
        float scaleMul, float lifePerKillBonus,
        string description, string attackStyle)
    {
        this.type             = type;
        this.displayName      = displayName;
        this.hpMul            = hpMul;
        this.damageMul        = damageMul;
        this.moveSpeedMul     = moveSpeedMul;
        this.scaleMul         = scaleMul;
        this.lifePerKillBonus = lifePerKillBonus;
        this.description      = description ?? string.Empty;
        this.attackStyle      = attackStyle ?? string.Empty;
    }
}
