using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PlayerStats))]
public class PlayerMovements : MonoBehaviour, ISpatialEntity
{
    [SerializeField] float _collisionRadius = 0.3f;

    PlayerStats    _stats;
    Animator       _anim;
    SpriteRenderer _sr;

    Vector2 _input;
    Vector2 _lastMoveDir = Vector2.right;
    bool    _isDead;
    bool    _registered;
    float   _attackSlowdown = 1f;  // movement speed multiplier during attack

    readonly List<ISpatialEntity> _obstacleBuffer = new List<ISpatialEntity>(8);

    public Vector2         LastMoveDir     => _lastMoveDir;
    public Vector2         Position        => transform.position;
    public SpatialCategory SpatialCategory => SpatialCategory.Player;
    public bool            IsActiveInHash  => !_isDead && gameObject.activeSelf;

    public void SetAttackSlowdown(float multiplier) => _attackSlowdown = multiplier;

    static readonly int HashMoving = Animator.StringToHash("Moving");

    void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _anim  = GetComponent<Animator>();
        _sr    = GetComponent<SpriteRenderer>();
        _stats.OnDeath += () => { _isDead = true; _input = Vector2.zero; };
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

    void Start()
    {
        if (_registered || SpatialHashManager.Instance == null) return;
        SpatialHashManager.Instance.Register(this);
        _registered = true;
    }

    void Update()
    {
        if (_isDead || GameManager.Instance == null || GameManager.Instance.State != GameState.Playing)
        {
            _input = Vector2.zero;
            return;
        }
        // Boss intro freeze — player stands still for ~1 second while the encounter sets up.
        if (BossEncounter.Instance != null && BossEncounter.Instance.PlayerLocked)
        {
            _input = Vector2.zero;
            _anim?.SetBool(HashMoving, false);
            return;
        }
        _input = InputService.MoveAxis;
        if (_input.sqrMagnitude > 1f) _input.Normalize();
        if (_input.sqrMagnitude > 0f) _lastMoveDir = _input;
        if (_input.x != 0f) _sr.flipX = _input.x < 0f;
        _anim?.SetBool(HashMoving, _input.sqrMagnitude > 0f);

        Vector2 from   = transform.position;
        float   waterMul = WorldManager.Instance != null
            ? WorldManager.Instance.GetMoveSpeedMultiplier(from)
            : 1f;
        // Agility "Light Footing": reduce terrain slow by lerping waterMul toward 1.
        if (_stats.terrainSlowResist > 0f && waterMul < 1f)
            waterMul = Mathf.Lerp(waterMul, 1f, Mathf.Clamp01(_stats.terrainSlowResist));
        Vector2 desired  = from + _input * _stats.moveSpeed * waterMul * _attackSlowdown * Time.deltaTime;
        Vector2 resolved = BuildingCollision.Resolve(
            SpatialHashManager.Instance, desired, _collisionRadius, _obstacleBuffer);
        transform.position = new Vector3(resolved.x, resolved.y, transform.position.z);
    }

    // Top-down Y-sort so enemies above the player draw behind and enemies below draw in front.
    // Must use the same base/multiplier as EnemyBase so characters share a coordinate space, and
    // a high base keeps characters above tiles/buildings at any reasonable Y.
    const int YSortBase       = 20000;
    const int YSortMultiplier = 10;
    // Floor stays above world sprites (tiles -10..-8, buildings 5..6) so extreme Y never buries the player.
    const int YSortMin        = 10;
    const int YSortMax        = 32000;

    void LateUpdate()
    {
        if (_sr == null) return;
        int raw = YSortBase - Mathf.RoundToInt(transform.position.y * YSortMultiplier);
        _sr.sortingOrder = Mathf.Clamp(raw, YSortMin, YSortMax);
    }
}
