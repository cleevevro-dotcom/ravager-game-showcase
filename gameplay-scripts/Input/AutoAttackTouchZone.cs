using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Invisible UI Image covering the right half of the screen. While a touch/click is held down on
/// it, <see cref="InputService.MobileAttackHeld"/> is true — so the claw fires automatically
/// (and thus all elemental skills piggyback on the swing). Releasing resets the flag.
///
/// When the game isn't in the Playing state (level-up, dead), the zone stops accepting raycasts
/// so the mutation selection and retry buttons get touches unimpeded.
/// </summary>
[RequireComponent(typeof(Image))]
public class AutoAttackTouchZone : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    Image _image;

    void Awake() => _image = GetComponent<Image>();

    void Update()
    {
        if (_image == null) return;
        bool playing = GameManager.Instance == null || GameManager.Instance.State == GameState.Playing;
        if (_image.raycastTarget != playing) _image.raycastTarget = playing;
        if (!playing) InputService.MobileAttackHeld = false;
    }

    public void OnPointerDown(PointerEventData e) => InputService.MobileAttackHeld = true;
    public void OnPointerUp  (PointerEventData e) => InputService.MobileAttackHeld = false;

    void OnDisable() => InputService.MobileAttackHeld = false;

    public static AutoAttackTouchZone Build(Transform canvas)
    {
        var go = new GameObject("AutoAttackZone", typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.001f); // practically invisible but raycast-eligible
        img.raycastTarget = true;

        return go.AddComponent<AutoAttackTouchZone>();
    }
}
