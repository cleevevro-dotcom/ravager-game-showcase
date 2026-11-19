using System;
using UnityEngine;

[DisallowMultipleComponent]
public class Destructible : MonoBehaviour, IObstacle
{
    public enum Kind : byte { House, Tower, Tree }

    SpriteRenderer _sr;
    Kind    _kind;
    float   _maxHp;
    float   _hp;
    Vector2 _obstacleHalf;
    Vector2 _obstacleOffset;
    bool    _isDead;
    bool    _registered;
    bool    _hasReward;
    Color   _baseColor = Color.white;
    Color   _damagedTint;

    Sprite _intactSprite;
    Sprite _destroyedSprite;

    Action<Destructible> _onDefeated;

    /// <summary>Static event fired when ANY destructible is defeated. Used by global systems
    /// (MissionManager, etc.) that need to react to building destruction without holding a
    /// per-instance reference. Subscribers must unsubscribe on destroy to avoid leaks.</summary>
    public static event Action<Destructible> OnAnyDefeated;

    // Box center in world space (sprite anchor + configured offset). Used by the spatial hash.
    public Vector2         Position            => (Vector2)transform.position + _obstacleOffset;
    public Vector2         SpriteAnchor        => (Vector2)transform.position;
    public SpatialCategory SpatialCategory     => SpatialCategory.Building;
    public bool            IsActiveInHash      => !_isDead && gameObject.activeSelf;
    public Vector2         ObstacleHalfExtents => _obstacleHalf;
    public Kind            BuildingKind        => _kind;
    public bool            HasReward           => _hasReward;
    public bool            IsDead              => _isDead;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
    }

    public void Setup(Kind kind, float hp, Vector2 obstacleSize, Vector2 obstacleOffset,
                      Vector2 scale, Color baseColor, bool hasReward, int sortingOrder,
                      Action<Destructible> onDefeated,
                      Sprite intactSprite = null, Sprite destroyedSprite = null)
    {
        _kind            = kind;
        _maxHp           = _hp = hp;
        _obstacleHalf    = obstacleSize * 0.5f;
        _obstacleOffset  = obstacleOffset;
        _isDead          = false;
        _hasReward       = hasReward;
        _onDefeated      = onDefeated;
        _baseColor       = baseColor;
        _damagedTint     = new Color(baseColor.r * 0.5f, baseColor.g * 0.5f, baseColor.b * 0.5f, baseColor.a);
        _intactSprite    = intactSprite;
        _destroyedSprite = destroyedSprite;

        transform.localScale = new Vector3(scale.x, scale.y, 1f);
        if (_sr != null)
        {
            // Use the real intact sprite if available; otherwise keep the placeholder square
            // and tint it. Real sprites carry their own colors so don't tint them.
            if (_intactSprite != null)
            {
                _sr.sprite = _intactSprite;
                _sr.color  = Color.white;
            }
            else
            {
                _sr.color = baseColor;
            }
            _sr.sortingOrder = sortingOrder;
            _sr.enabled      = true;
        }
    }

    void OnEnable()
    {
        if (_registered || SpatialHashManager.Instance == null) return;
        SpatialHashManager.Instance.Register(this);
        _registered = true;
    }

    void OnDisable()
    {
        if (!_registered) return;
        SpatialHashManager.Instance?.Unregister(this);
        _registered = false;
    }

    public void TakeDamage(float dmg)
    {
        if (_isDead || dmg <= 0f) return;
        _hp -= dmg;

        if (_sr != null)
        {
            if (_intactSprite != null)
            {
                float frac = Mathf.Clamp01(_hp / _maxHp);
                float k    = Mathf.Lerp(0.55f, 1f, frac);
                _sr.color  = new Color(k, k, k, 1f);
            }
            else
            {
                float frac = Mathf.Clamp01(_hp / _maxHp);
                _sr.color  = Color.Lerp(_damagedTint, _baseColor, frac);
            }
        }

        if (_hp <= 0f) Die();
    }

    void Die()
    {
        _isDead = true;
        _onDefeated?.Invoke(this);
        OnAnyDefeated?.Invoke(this);

        if (_destroyedSprite != null && _sr != null)
        {
            _sr.sprite = _destroyedSprite;
            _sr.color  = Color.white;
            _sr.sortingOrder = 4;
        }

        if (_destroyedSprite == null)
        {
            gameObject.SetActive(false);
        }
        else
        {
            if (_registered && SpatialHashManager.Instance != null)
            {
                SpatialHashManager.Instance.Unregister(this);
                _registered = false;
            }
        }
    }
}
