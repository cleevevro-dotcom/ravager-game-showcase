using UnityEngine;

/// <summary>
/// Visual-only one-shot for the Golem heavy smash. Two layered SpriteRenderers:
///   1. BrownDisc — dark brown circle on the ground (impact zone indicator)
///   2. ShatterIcon — the Earthshatter sprite from SkillArt on top
///
/// No damage / no spatial query — GolemSmashAttack already handles those. This component
/// is created lazily on the player when the GolemSmashAttack swings. Lives across swings
/// (pooled to one instance per beast).
/// </summary>
public class GolemSmashEffect : MonoBehaviour
{
    [SerializeField] float _duration = 0.45f;

    SpriteRenderer _brown;
    SpriteRenderer _shatter;
    float _t;
    bool  _playing;
    float _baseRadius = 1f;

    void Awake()
    {
        // Brown disc — large, faded, beneath the icon.
        var brownGo = new GameObject("BrownDisc");
        brownGo.transform.SetParent(transform, false);
        _brown = brownGo.AddComponent<SpriteRenderer>();
        _brown.sprite = PlaceholderSprites.GetCircle(64);
        _brown.color  = new Color(0.27f, 0.16f, 0.08f, 0.6f);
        _brown.sortingOrder = 4;

        // Earthshatter icon — uses SkillArt if available, falls back to ring.
        var iconGo = new GameObject("ShatterIcon");
        iconGo.transform.SetParent(transform, false);
        _shatter = iconGo.AddComponent<SpriteRenderer>();
        var icon = SkillArt.Instance != null ? SkillArt.FirstOr(SkillArt.Instance.elementalEarthshatter) : null;
        _shatter.sprite = icon != null ? icon : PlaceholderSprites.GetRing(64, 0.18f);
        _shatter.color  = Color.white;
        _shatter.sortingOrder = 5;

        gameObject.SetActive(false);
    }

    public void Play(Vector2 worldPos, float radius)
    {
        transform.position = worldPos;
        // The brown disc represents the full smash radius; the icon sits at 90% of that.
        _baseRadius = Mathf.Max(0.5f, radius);
        float discScale = (_baseRadius * 2f) / PlaceholderSprites.GetVisibleDiameterUnits(64);
        transform.localScale = new Vector3(discScale, discScale, 1f);
        if (_shatter != null) _shatter.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        _t = 0f;
        _playing = true;
        gameObject.SetActive(true);
    }

    void Update()
    {
        if (!_playing) return;
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _duration);
        float a = 1f - k;

        if (_brown != null)   { var c = _brown.color;   c.a = 0.6f * a; _brown.color = c; }
        if (_shatter != null) { var c = _shatter.color; c.a = a;        _shatter.color = c; }

        if (_t >= _duration)
        {
            _playing = false;
            gameObject.SetActive(false);
        }
    }
}
