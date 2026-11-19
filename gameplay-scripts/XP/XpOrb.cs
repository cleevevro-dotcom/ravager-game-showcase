using UnityEngine;

public class XpOrb : MonoBehaviour, ISpatialEntity
{
    [SerializeField] float _attractRadius = 4f;
    [SerializeField] float _moveSpeed     = 9f;

    static readonly float LifeTime      = 15f;
    static readonly float CollectRadius = 0.35f;

    float _xpValue;
    float _spawnTime;
    ObjectPool<XpOrb> _pool;
    Transform _player;
    PlayerStats _playerStats;
    bool _registered;

    public Vector2         Position        => transform.position;
    public SpatialCategory SpatialCategory => SpatialCategory.XpOrb;
    public bool            IsActiveInHash  => gameObject.activeSelf;

    public void Init(Vector2 position, float xpValue, Transform player, PlayerStats stats, ObjectPool<XpOrb> pool)
    {
        transform.position = position;
        _xpValue     = xpValue;
        _player      = player;
        _playerStats = stats;
        _pool        = pool;
        _spawnTime   = Time.time;

        if (!_registered && SpatialHashManager.Instance != null)
        {
            SpatialHashManager.Instance.Register(this);
            _registered = true;
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

    /// <summary>
    /// Used by one-shot magnet effects (e.g. tower-kill reward) to consume this orb regardless
    /// of distance. Bypasses the normal attract → collect-radius dance and credits the XP
    /// immediately, then returns to pool.
    /// </summary>
    public void ForceCollect()
    {
        if (_playerStats != null) _playerStats.AddXp(_xpValue);
        if (_pool != null) _pool.Return(this);
        else gameObject.SetActive(false);
    }

    void Update()
    {
        if (_player == null) return;
        Vector2 toPlayer = (Vector2)_player.position - (Vector2)transform.position;
        float distSqr = toPlayer.sqrMagnitude;

        if (distSqr <= CollectRadius * CollectRadius)
        {
            _playerStats.AddXp(_xpValue);
            _pool.Return(this);
            return;
        }

        // Vision skill scales the magnet radius.
        float radiusMul = _playerStats != null ? _playerStats.xpMagnetRadiusMul : 1f;
        float effectiveAttract = _attractRadius * radiusMul;
        if (distSqr <= effectiveAttract * effectiveAttract)
            transform.position = Vector2.MoveTowards(transform.position, _player.position, _moveSpeed * Time.deltaTime);

        if (Time.time - _spawnTime >= LifeTime)
            _pool.Return(this);
    }
}
