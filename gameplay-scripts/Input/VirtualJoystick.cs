using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Floating virtual joystick. The touch zone is the whole left half of the screen — wherever the
/// player first presses, the background + knob pop in at that point and the knob tracks the drag.
/// On release the widget hides and <see cref="InputService.MobileMove"/> is zeroed.
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] RectTransform _background;
    [SerializeField] RectTransform _knob;
    [SerializeField] float         _handleRange = 110f;   // px radius for full-stick deflection

    RectTransform _self;
    Canvas        _canvas;
    Image         _zoneImage;
    Vector2       _inputDir;

    void Awake()
    {
        _self      = GetComponent<RectTransform>();
        _canvas    = GetComponentInParent<Canvas>();
        _zoneImage = GetComponent<Image>();
        HideStick();
    }

    void Update()
    {
        // Disable the joystick zone while the game isn't in Playing state (level-up, death)
        // so the mutation selection / restart buttons get touches without this zone eating them.
        bool playing = GameManager.Instance == null || GameManager.Instance.State == GameState.Playing;
        if (_zoneImage != null && _zoneImage.raycastTarget != playing)
            _zoneImage.raycastTarget = playing;

        if (!playing)
        {
            _inputDir = Vector2.zero;
            InputService.MobileMove = Vector2.zero;
            HideStick();
        }
    }

    public void OnPointerDown(PointerEventData e)
    {
        // Move the stick under the press location, then snap the knob to center.
        if (_background != null)
        {
            Vector2 local;
            Camera cam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : e.pressEventCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_self, e.position, cam, out local);
            _background.anchoredPosition = local;
            _background.gameObject.SetActive(true);
            if (_knob != null) _knob.anchoredPosition = Vector2.zero;
        }
        _inputDir = Vector2.zero;
        InputService.MobileMove = Vector2.zero;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_background == null) return;

        Vector2 local;
        Camera cam = _canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : e.pressEventCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_background, e.position, cam, out local);

        Vector2 clamped = Vector2.ClampMagnitude(local, _handleRange);
        if (_knob != null) _knob.anchoredPosition = clamped;

        _inputDir = clamped / _handleRange;
        InputService.MobileMove = _inputDir;
    }

    public void OnPointerUp(PointerEventData e)
    {
        _inputDir = Vector2.zero;
        InputService.MobileMove = Vector2.zero;
        HideStick();
    }

    void OnDisable() => InputService.MobileMove = Vector2.zero;

    void HideStick()
    {
        if (_background != null) _background.gameObject.SetActive(false);
    }

    /// <summary>
    /// Build the floating joystick. Touch zone covers the left half of the screen; the visible
    /// stick only appears while the player is pressing.
    /// </summary>
    public static VirtualJoystick Build(Transform canvas, float stickSize = 260f)
    {
        // Invisible touch zone — left half of the canvas. Pivot pinned to bottom-left so the
        // local-point math used to reposition the stick matches anchoredPosition 1:1.
        var zoneGO = new GameObject("JoystickZone", typeof(RectTransform));
        zoneGO.transform.SetParent(canvas, false);
        var zoneRT = zoneGO.GetComponent<RectTransform>();
        zoneRT.anchorMin = new Vector2(0f, 0f);
        zoneRT.anchorMax = new Vector2(0.5f, 1f);
        zoneRT.pivot     = new Vector2(0f, 0f);
        zoneRT.offsetMin = zoneRT.offsetMax = Vector2.zero;
        var zoneImg = zoneGO.AddComponent<Image>();
        zoneImg.color = new Color(1f, 1f, 1f, 0.001f); // practically invisible but raycastable
        zoneImg.raycastTarget = true;

        // Background (moves to the tap point) + knob. Pivot centered so anchoredPosition
        // represents the stick's CENTER, matching the touch coordinate.
        var bgGO = new GameObject("Background", typeof(RectTransform));
        bgGO.transform.SetParent(zoneGO.transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = bgRT.anchorMax = new Vector2(0f, 0f);
        bgRT.pivot     = new Vector2(0.5f, 0.5f);
        bgRT.sizeDelta = new Vector2(stickSize, stickSize);
        var bgImg = bgGO.AddComponent<Image>();
        bgImg.sprite = UISprites.Circle;
        bgImg.color  = new Color(1f, 1f, 1f, 0.18f);
        bgImg.raycastTarget = false;

        var knobGO = new GameObject("Knob", typeof(RectTransform));
        knobGO.transform.SetParent(bgGO.transform, false);
        var knobRT = knobGO.GetComponent<RectTransform>();
        knobRT.anchorMin = knobRT.anchorMax = knobRT.pivot = new Vector2(0.5f, 0.5f);
        knobRT.sizeDelta = new Vector2(stickSize * 0.45f, stickSize * 0.45f);
        knobRT.anchoredPosition = Vector2.zero;
        var knobImg = knobGO.AddComponent<Image>();
        knobImg.sprite = UISprites.Circle;
        knobImg.color  = new Color(1f, 1f, 1f, 0.55f);
        knobImg.raycastTarget = false;

        var joy = zoneGO.AddComponent<VirtualJoystick>();
        var bgField = typeof(VirtualJoystick).GetField("_background",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var knobField = typeof(VirtualJoystick).GetField("_knob",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        bgField?.SetValue(joy, bgRT);
        knobField?.SetValue(joy, knobRT);
        joy._handleRange = stickSize * 0.5f - knobRT.sizeDelta.x * 0.25f;
        return joy;
    }
}
