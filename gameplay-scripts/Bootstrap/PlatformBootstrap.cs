using UnityEngine;

/// <summary>
/// Single-shot platform-specific runtime setup. Lives at the very start of the boot chain
/// (created by GameBootstrap.Awake) so by the time HUDs construct their CanvasScalers, the
/// global Screen orientation + auto-rotation rules are already in place.
///
/// Android: force landscape — the gameplay HUD and skill icons are laid out wide, and the
/// joystick + auto-attack-zone touch split expects ≈16:9 horizontal space.
/// WebGL: leave Screen.* alone (browser controls aspect/fullscreen); CanvasScaler in
/// GameScreenHub already handles responsive sizing.
/// </summary>
[DefaultExecutionOrder(-200)]
public class PlatformBootstrap : MonoBehaviour
{
    public static PlatformBootstrap Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (transform.parent == null) DontDestroyOnLoad(gameObject);
        ApplyPlatformDefaults();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void ApplyPlatformDefaults()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Block portrait — only landscape-left and landscape-right are valid orientations.
        // AutoRotation respects the device's current landscape tilt (so users holding the
        // phone either way see the UI correctly). ProjectSettings.defaultScreenOrientation
        // is also AutoRotation with these same flags so the very first frame is landscape.
        Screen.autorotateToPortrait          = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft     = true;
        Screen.autorotateToLandscapeRight    = true;
        Screen.orientation                   = ScreenOrientation.AutoRotation;
#endif

        // WebGL: nothing to do — the browser decides aspect. We rely on CanvasScaler
        // matchWidthOrHeight = 0.5 (set in GameScreenHub + MobileControls) so UI scales
        // sensibly when the player resizes the browser window.
    }
}
