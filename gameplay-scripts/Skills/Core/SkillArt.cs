using UnityEngine;

/// <summary>
/// Inspector-driven registry of skill / world sprites. Drop one instance on the bootstrap
/// GameObject, assign each Sprite slot in the Inspector, and the skills automatically pick
/// the new art up via <see cref="Instance"/> on first use. Falling back to
/// <see cref="PlaceholderSprites"/> when a slot is left empty means the game keeps working
/// even before the user has wired everything.
///
/// Multi-frame slots take a <see cref="Sprite"/> array — assign 1 sprite for a static
/// renderer (Sword, Axe, etc.) or N sprites for an animated effect (Tentacles 6, Fire 6,
/// Freeze 6, Lightning 3, EarthShatter 2, Fear stun 2/3, Explosion 6). Multi-sprite imports
/// require the .png to be sliced in the Sprite Editor first so each frame is a distinct
/// Sprite sub-asset.
/// </summary>
[DisallowMultipleComponent]
public class SkillArt : MonoBehaviour
{
    public static SkillArt Instance { get; private set; }

    [Header("Weapon skills")]
    public Sprite weaponSword;
    public Sprite weaponAxe;
    public Sprite weaponSpiritOrb;
    public Sprite[] weaponTentacles;        // 6 frames per brief (1-3 emerge, 4-6 strike).

    [Header("Elemental skills")]
    public Sprite[] elementalFire;          // 6-frame fireball anim
    public Sprite[] elementalFreeze;        // 6 frames (1 = radius, 2-6 = activation)
    public Sprite[] elementalLightning;     // 3-frame left-to-right strike
    public Sprite[] elementalEarthshatter;  // 2 frames (impact + aftershock)
    public Sprite   elementalWind;

    [Header("Bonus skills")]
    public Sprite   fearAura;               // single frame for the radius indicator
    public Sprite[] fearNoStunBurst;        // 3 frames when no-one was stunned
    public Sprite[] fearStunBurst;          // 2 frames when enemies were stunned

    [Header("World VFX")]
    public Sprite[] effectExplosion;        // 6 frames for tower destruction
    public Sprite   towerNormal;
    public Sprite   towerDestroyed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // Carry sprite assignments from the main menu (where they're authored) into gameplay
        // scenes — skills + tower-destruction VFX run in SampleScene but they look up Instance.
        // Without DontDestroyOnLoad the wiring would reset every scene load.
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // --- helpers ----------------------------------------------------------

    /// <summary>Return the first frame, or null when the array is empty.</summary>
    public static Sprite FirstOr(Sprite[] frames, Sprite fallback = null)
    {
        return (frames != null && frames.Length > 0 && frames[0] != null) ? frames[0] : fallback;
    }

    /// <summary>Index-safe lookup that loops once past the end (so a 3-frame array reads index 4 as frame 1).</summary>
    public static Sprite At(Sprite[] frames, int index)
    {
        if (frames == null || frames.Length == 0) return null;
        return frames[((index % frames.Length) + frames.Length) % frames.Length];
    }

    /// <summary>Best-effort lookup — returns the registry sprite, or a procedural square fallback.</summary>
    public static Sprite SpriteOrSquareFallback(Sprite preferred, int fallbackSize = 32)
    {
        return preferred != null ? preferred : PlaceholderSprites.GetSquare(fallbackSize);
    }
}
