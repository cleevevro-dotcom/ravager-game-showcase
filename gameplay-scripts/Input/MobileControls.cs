using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds a screen-space Canvas with a virtual joystick (bottom-left) and an auto-attack touch
/// zone (right half). Enabled automatically on touch platforms; forceable via <see cref="forceEnable"/>.
/// Spawned by GameBootstrap; zero scene wiring required.
/// </summary>
public class MobileControls : MonoBehaviour
{
    [SerializeField] bool forceEnable = true;

    Canvas _canvas;

    void Awake()
    {
        if (!ShouldEnable()) { gameObject.SetActive(false); return; }
        EnsureEventSystem();
        Build();
    }

    bool ShouldEnable()
    {
        // Default-on: the joystick is the primary control scheme on mobile and a
        // friendly mouse-drag fallback on desktop. Turn off via Inspector if you
        // want keyboard-only testing.
        if (forceEnable) return true;
        return Application.isMobilePlatform || Input.touchSupported;
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        es.transform.SetParent(null);
    }

    void Build()
    {
        var canvasGO = new GameObject("MobileControlsCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 90;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode           = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution   = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight    = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();

        // Left-half floating joystick and right-half auto-attack zone. The two zones split
        // the screen so a finger can pick whichever side it wants.
        VirtualJoystick.Build(canvasGO.transform, stickSize: 260f);
        AutoAttackTouchZone.Build(canvasGO.transform);
    }
}
