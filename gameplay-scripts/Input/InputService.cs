using UnityEngine;

/// <summary>
/// Platform-agnostic input façade. Game code reads <see cref="MoveAxis"/> and <see cref="AttackHeld"/>
/// without knowing or caring whether the source is a keyboard, a virtual joystick, or a touch pad.
/// Mobile UI widgets (virtual joystick, auto-attack zone) push their values in via the Mobile* hooks.
/// <see cref="InputDriver"/> reads keyboard/mouse once per frame and combines with mobile sources.
/// </summary>
public static class InputService
{
    /// <summary>Normalized (-1..1 each axis) movement intent for the current frame.</summary>
    public static Vector2 MoveAxis   { get; internal set; }

    /// <summary>True while the player wants to attack this frame (hold-to-attack).</summary>
    public static bool    AttackHeld { get; internal set; }

    // --- Mobile sources. UI widgets set these; InputDriver blends them with keyboard/mouse. ---

    public static Vector2 MobileMove       { get; set; }
    public static bool    MobileAttackHeld { get; set; }

    /// <summary>Reset every-frame mobile state that relies on touch phase (call on touch end).</summary>
    public static void ResetMobile()
    {
        MobileMove       = Vector2.zero;
        MobileAttackHeld = false;
    }
}
