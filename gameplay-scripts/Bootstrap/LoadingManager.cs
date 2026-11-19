using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the boot sequence: builds a placeholder loading UI in Awake (so it covers
/// gameplay from frame 1), then runs a coroutine that calls GameBootstrap step methods
/// in order. Gameplay-update guards already early-return on GameState.Loading, so
/// EnemyManager / WaveManager / projectiles stay quiet until we flip to Playing.
/// </summary>
[DefaultExecutionOrder(-95)]
public class LoadingManager : MonoBehaviour
{
    public static LoadingManager Instance { get; private set; }

    [Header("Tuning")]
    [Tooltip("Min seconds to display the 100% bar before revealing gameplay. Avoids a single-frame flash on fast machines.")]
    [SerializeField] float _minDisplaySeconds = 0.15f;

    Canvas          _canvas;
    Image           _barFill;
    TextMeshProUGUI _stepLabel;
    bool            _running;
    bool            _completed;

    public bool IsLoading => !_completed;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Force the Loading state from frame 1. Time.timeScale -> 0, which keeps any
        // physics / animation noise quiet while the UI is up. yield return null still
        // ticks our loading coroutine.
        if (GameManager.Instance != null)
            GameManager.Instance.SetState(GameState.Loading);

        BuildUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void BuildUI()
    {
        var canvasGO = new GameObject("[LoadingCanvas]");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 32000; // above every other in-scene canvas (HUDCanvas=80, GameUI=200)

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // Full-screen dark background — opaque so it hides player + gameplay.
        var bgGO = new GameObject("BG", typeof(RectTransform));
        bgGO.transform.SetParent(canvasGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        var bg = bgGO.AddComponent<Image>();
        bg.sprite = UISprites.Square;
        bg.color  = new Color(0.04f, 0.04f, 0.05f, 1f);
        bg.raycastTarget = true;

        // Title — tinted blood red to match the Ravager identity.
        var title = HUDUIFactory.BuildLabel(
            canvasGO.transform,
            "RAVAGER",
            offset: new Vector2(0f, 280f),
            align: TextAlignmentOptions.Center,
            fontSize: 96);
        var titleRT = title.GetComponent<RectTransform>();
        titleRT.anchorMin = titleRT.anchorMax = titleRT.pivot = new Vector2(0.5f, 0.5f);
        titleRT.sizeDelta = new Vector2(900f, 120f);
        titleRT.anchoredPosition = new Vector2(0f, 280f);
        title.color = new Color(0.85f, 0.18f, 0.18f, 1f);

        // Subtitle.
        var subtitle = HUDUIFactory.BuildLabel(
            canvasGO.transform,
            "Rise of the Beast",
            offset: new Vector2(0f, 200f),
            align: TextAlignmentOptions.Center,
            fontSize: 32);
        var subRT = subtitle.GetComponent<RectTransform>();
        subRT.anchorMin = subRT.anchorMax = subRT.pivot = new Vector2(0.5f, 0.5f);
        subRT.sizeDelta = new Vector2(900f, 44f);
        subRT.anchoredPosition = new Vector2(0f, 200f);
        subtitle.color = new Color(0.65f, 0.62f, 0.6f, 1f);

        // Progress bar (centered).
        var barGO = new GameObject("ProgressBar", typeof(RectTransform));
        barGO.transform.SetParent(canvasGO.transform, false);
        var barRT = barGO.GetComponent<RectTransform>();
        barRT.anchorMin = barRT.anchorMax = barRT.pivot = new Vector2(0.5f, 0.5f);
        barRT.sizeDelta = new Vector2(700f, 28f);
        barRT.anchoredPosition = Vector2.zero;
        var barBG = barGO.AddComponent<Image>();
        barBG.sprite = UISprites.Square;
        barBG.color  = new Color(0f, 0f, 0f, 0.55f);

        var fillGO = new GameObject("Fill", typeof(RectTransform));
        fillGO.transform.SetParent(barGO.transform, false);
        var fillRT = fillGO.GetComponent<RectTransform>();
        fillRT.anchorMin = new Vector2(0f, 0f);
        fillRT.anchorMax = new Vector2(1f, 1f);
        fillRT.offsetMin = new Vector2(2f, 2f);
        fillRT.offsetMax = new Vector2(-2f, -2f);
        _barFill = fillGO.AddComponent<Image>();
        _barFill.sprite     = UISprites.Square;
        _barFill.color      = new Color(0.85f, 0.18f, 0.18f, 1f);
        _barFill.type       = Image.Type.Filled;
        _barFill.fillMethod = Image.FillMethod.Horizontal;
        _barFill.fillAmount = 0f;

        // Step label below the bar.
        _stepLabel = HUDUIFactory.BuildLabel(
            canvasGO.transform,
            "Loading...",
            offset: new Vector2(0f, -50f),
            align: TextAlignmentOptions.Center,
            fontSize: 32);
        var stepRT = _stepLabel.GetComponent<RectTransform>();
        stepRT.anchorMin = stepRT.anchorMax = stepRT.pivot = new Vector2(0.5f, 0.5f);
        stepRT.sizeDelta = new Vector2(900f, 44f);
        stepRT.anchoredPosition = new Vector2(0f, -50f);
        _stepLabel.color = new Color(0.85f, 0.85f, 0.85f, 1f);
    }

    public void BeginLoad(GameBootstrap bootstrap)
    {
        if (_running || _completed || bootstrap == null) return;
        _running = true;
        StartCoroutine(LoadRoutine(bootstrap));
    }

    IEnumerator LoadRoutine(GameBootstrap b)
    {
        // Step weights chosen so the bar moves smoothly; the village preload is the
        // longest real wait so it owns the biggest chunk of progress.
        SetProgress(0.02f, "Booting core systems...");
        yield return null;
        b.Step_EnsureSystems();

        SetProgress(0.15f, "Prewarming object pools...");
        yield return null;
        b.Step_PrewarmPools();

        SetProgress(0.30f, "Generating village & terrain...");
        yield return null;
        yield return b.Step_PreloadVillage();

        SetProgress(0.80f, "Wiring gameplay systems...");
        yield return null;
        b.Step_WireGameplay();

        SetProgress(0.95f, "Finalizing...");
        // Two frames of breathing room so the HUD canvas finishes its first pass before
        // we reveal the player to the camera — avoids a one-frame UI flash.
        yield return null;
        yield return null;

        SetProgress(1f, "Ready");
        if (_minDisplaySeconds > 0f)
            yield return new WaitForSecondsRealtime(_minDisplaySeconds);

        Hide();
        _completed = true;
        _running   = false;

        if (GameManager.Instance != null)
            GameManager.Instance.SetState(GameState.Playing);
    }

    public void SetProgress(float t, string label)
    {
        if (_barFill != null) _barFill.fillAmount = Mathf.Clamp01(t);
        if (_stepLabel != null && label != null) _stepLabel.text = label;
    }

    public void Hide()
    {
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }
}
