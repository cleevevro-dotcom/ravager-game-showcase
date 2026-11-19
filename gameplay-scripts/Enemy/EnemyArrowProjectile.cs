using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class EnemyArrowProjectile : MonoBehaviour
{
    SpriteRenderer _sr;
    EnemyManager _owner;
    Transform _playerTransform;
    PlayerStats _playerStats;

    Vector2 _dir;
    float _speed;
    float _damage;
    float _hitRadius;
    float _maxDistance;
    float _traveled;
    bool _alive;

    static Sprite _sharedSprite;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        EnsureSprite();
        if (_sr.sprite == null) _sr.sprite = _sharedSprite;
        _sr.sortingOrder = 30000;
        _sr.color = new Color(0.95f, 0.85f, 0.6f, 1f);
        transform.localScale = new Vector3(2.4f, 0.45f, 1f);
    }

    static void EnsureSprite()
    {
        if (_sharedSprite != null) return;
        _sharedSprite = PlaceholderSprites.GetSquare(8);
    }

    public void Launch(EnemyManager owner, Transform player, PlayerStats stats,
                       Vector2 origin, Vector2 dir,
                       float speed, float damage, float hitRadius, float maxDistance)
    {
        _owner           = owner;
        _playerTransform = player;
        _playerStats     = stats;
        _dir             = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        _speed           = speed;
        _damage          = damage;
        _hitRadius       = hitRadius;
        _maxDistance     = maxDistance;
        _traveled        = 0f;
        _alive           = true;

        transform.position = origin;
        float angleDeg = Mathf.Atan2(_dir.y, _dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
    }

    void Update()
    {
        if (!_alive) return;
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

        float dt = Time.deltaTime;
        Vector2 step = _dir * _speed * dt;
        transform.position += (Vector3)step;
        _traveled += step.magnitude;

        if (TryHitPlayer()) { Recycle(); return; }
        if (_traveled >= _maxDistance) { Recycle(); return; }
    }

    bool TryHitPlayer()
    {
        if (_playerTransform == null || _playerStats == null) return false;
        Vector2 to = (Vector2)_playerTransform.position - (Vector2)transform.position;
        if (to.sqrMagnitude > _hitRadius * _hitRadius) return false;
        // Pass the arrow's own position as source so knockback pushes the player along the
        // arrow's flight direction, not from the archer who fired it.
        _playerStats.TakeDamage(_damage, transform.position);
        return true;
    }

    void Recycle()
    {
        if (!_alive) return;
        _alive = false;
        if (_owner != null) _owner.ReturnArrow(this);
        else gameObject.SetActive(false);
    }
}
