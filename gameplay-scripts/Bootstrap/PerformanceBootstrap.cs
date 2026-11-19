using UnityEngine;

/// <summary>
/// Centralises runtime tuning so every scene starts with consistent v-sync,
/// target framerate, quality, and (on WebGL/mobile) low-performance scaling.
///
/// Three modes:
///   - DesktopHigh:    desktop standalone — 60 fps, full quality, no down-scaling.
///   - WebGLDesktop:   WebGL on a desktop browser — 60 fps cap, full quality.
///   - WebGLMobileLow: WebGL/PWA on Android/iOS — 30 fps cap, lowered quality,
///                     suggests render-scale + particle/effect reduction.
///
/// SetMobileLowMode toggles WebGLMobileLow at runtime for the in-game "performance
/// mode" toggle. Other systems (HUD, gameplay caps) read <see cref="IsMobileLow"/>
/// to throttle their own work (max enemy count, damage text, particles, etc).
/// </summary>
[DefaultExecutionOrder(-1000)]
public class PerformanceBootstrap : MonoBehaviour
{
    public enum PerfMode { DesktopHigh, WebGLDesktop, WebGLMobileLow }

    public static PerfMode Current { get; private set; }
    public static bool IsMobileLow => Current == PerfMode.WebGLMobileLow;

    /// <summary>
    /// Multipliers other systems can apply (max enemies, projectile cap, etc).
    /// Mobile-low halves most caps; desktop high passes 1.0.
    /// </summary>
    public static float GameplayLoadMultiplier => Current switch
    {
        PerfMode.DesktopHigh    => 1.0f,
        PerfMode.WebGLDesktop   => 0.85f,
        PerfMode.WebGLMobileLow => 0.5f,
        _                       => 1.0f,
    };

    static bool _initialized;

    /// <summary>
    /// Auto-spawn a hidden PerformanceBootstrap before any scene loads so the
    /// frame cap and v-sync are applied regardless of how the game launches
    /// (main menu, gameplay scene, deep link, etc).
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoSpawn()
    {
        if (_initialized) return;
        var go = new GameObject("[PerformanceBootstrap]");
        go.AddComponent<PerformanceBootstrap>();
    }

    void Awake()
    {
        if (_initialized) { Destroy(gameObject); return; }
        _initialized = true;
        DontDestroyOnLoad(gameObject);

        QualitySettings.vSyncCount = 0;
        bool savedMobileLow = PlayerPrefs.GetInt("settings.perf_mobile", 0) == 1;

        if (Application.platform == RuntimePlatform.WebGLPlayer)
        {
            // Heuristic: mobile UAs typically land with low DPI / small screen;
            // honor the saved toggle first, then fall back to a size hint.
            if (savedMobileLow || IsMobileBrowser())
                ApplyMode(PerfMode.WebGLMobileLow);
            else
                ApplyMode(PerfMode.WebGLDesktop);
        }
        else if (Application.isMobilePlatform)
        {
            ApplyMode(PerfMode.WebGLMobileLow);
        }
        else
        {
            ApplyMode(PerfMode.DesktopHigh);
        }
    }

    public static void SetMobileLowMode(bool on)
    {
        ApplyMode(on ? PerfMode.WebGLMobileLow : (Application.platform == RuntimePlatform.WebGLPlayer ? PerfMode.WebGLDesktop : PerfMode.DesktopHigh));
    }

    static void ApplyMode(PerfMode mode)
    {
        Current = mode;
        QualitySettings.vSyncCount = 0;

        switch (mode)
        {
            case PerfMode.DesktopHigh:
                Application.targetFrameRate = 60;
                // Leave QualitySettings alone — user/quality dropdown can drive it.
                break;

            case PerfMode.WebGLDesktop:
                Application.targetFrameRate = 60;
                break;

            case PerfMode.WebGLMobileLow:
                Application.targetFrameRate = 30;
                // Force lowest quality so URP renderer drops post effects / MSAA.
                if (QualitySettings.names.Length > 0)
                    QualitySettings.SetQualityLevel(0, true);
                break;
        }
    }

    static bool IsMobileBrowser()
    {
        // Crude but cheap: small physical screen + portrait-ish aspect hints at a phone.
        if (Screen.dpi > 0f && Screen.dpi < 220f && Mathf.Min(Screen.width, Screen.height) <= 720)
            return true;
        return false;
    }
}
