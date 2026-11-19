using UnityEngine;

/// <summary>
/// Pooled fire-tower impact marker. Lifecycle:
///   1. SHOWN: orange-tinted telegraph circle on the ground for `telegraphDuration` seconds.
///      Player's dodge window — walk out of the radius to avoid damage.
///   2. IMPACT: one-shot AoE damage check; if the player is inside `radius`, takes damage and
///      knockback (routed through PlayerStats.TakeDamage with intensityMul=1.5 so the screen
///      shakes harder than a melee hit).
///   3. FADE: brighter flash for `postImpactDuration` seconds, then returns itself to the pool.
///
/// All visuals are SpriteRenderer + PlaceholderSprites — pure structure, no VFX yet.
/// </summary>
public class FireImpact : MonoBehaviour
{
    enum Phase : byte { Telegraph, PostImpact }

    SpriteRenderer _sr;
    DynamicPool<FireImpact> _pool;
    PlayerStats _playerStats;
    Transform   _playerTransform;

    Vector2 _landPos;
    float   _radius;
    float   _damage;
    float   _telegraphDuration;
    float   _postImpactDuration;
    float   _spawnTime;
    Phase   _phase;
    bool    _hitDealt;

    static readonly Color TelegraphColor = new Color(1f,    0.45f, 0.10f, 0.35f);
    static readonly Color ImpactColor    = new Color(1f,    0.75f, 0.20f, 0.85f);

    public void Configure(DynamicPool<FireImpact> pool, SpriteRenderer sr, PlayerStats playerStats, Transform playerTransform)
    {
        _pool            = pool;
        _sr              = sr;
        _playerStats     = playerStats;
        _playerTransform = playerTransform;
    }

    public void Launch(Vector2 landPos, float radius, float damage, float telegraphDur, float postImpactDur)
    {
        transform.position  = landPos;
        _landPos            = landPos;
        _radius             = radius;
        _damage             = damage;
        _telegraphDuration  = Mathf.Max(0f, telegraphDur);
        _postImpactDuration = Mathf.Max(0.05f, postImpactDur);
        _spawnTime          = Time.time;
        _phase              = Phase.Telegraph;
        _hitDealt           = false;

        if (_sr != null)
        {
            // Visible diameter scales with radius. PlaceholderSprites.GetCircle has a 1px margin
            // baked in, so use GetVisibleDiameterUnits for accurate sizing.
            float visibleDiameterUnits = PlaceholderSprites.GetVisibleDiameterUnits(64);
            float scale = (radius * 2f) / Mathf.Max(0.0001f, visibleDiameterUnits);
            transform.localScale = new Vector3(scale, scale, 1f);
            _sr.color = TelegraphColor;
            _sr.enabled = true;
        }
    }

    void Update()
    {
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

        float elapsed = Time.time - _spawnTime;

        if (_phase == Phase.Telegraph)
        {
            if (elapsed >= _telegraphDuration)
            {
                _phase = Phase.PostImpact;
                if (_sr != null) _sr.color = ImpactColor;
                TryDealImpactDamage();
            }
            return;
        }

        // Post-impact fade out, then return to pool.
        float postElapsed = elapsed - _telegraphDuration;
        if (_sr != null)
        {
            float k = 1f - Mathf.Clamp01(postElapsed / _postImpactDuration);
            var c = _sr.color;
            c.a = Mathf.Lerp(0f, ImpactColor.a, k);
            _sr.color = c;
        }
        if (postElapsed >= _postImpactDuration)
        {
            if (_pool != null) _pool.Return(this);
            else gameObject.SetActive(false);
        }
    }

    void TryDealImpactDamage()
    {
        if (_hitDealt) return;
        _hitDealt = true;
        if (_playerStats == null || _playerTransform == null) return;
        Vector2 pp = _playerTransform.position;
        float r2 = _radius * _radius;
        if ((pp - _landPos).sqrMagnitude > r2) return;
        // Heavier shake (intensityMul = 1.5) so a fire hit feels weightier than a melee hit
        // but lighter than a boss attack.
        _playerStats.TakeDamage(_damage, _landPos, 1.5f);
    }
}
