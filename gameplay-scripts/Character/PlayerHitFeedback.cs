using UnityEngine;

/// <summary>
/// Applies knockback + camera shake when the player takes damage. Routed through PlayerStats.TakeDamage(amount, fromPos)
/// so every damage source — melee, arrow, dash, future tower attacks — gets feedback automatically.
///
/// Performance:
/// - Knockback runs per hit but decays in ~0.15s, so the player keeps control.
/// - Camera shake is THROTTLED. With 200 enemies hitting at once, only one shake fires per
///   _shakeCooldown window, preventing the screen from blendering.
/// </summary>
public class PlayerHitFeedback : MonoBehaviour
{
    public static PlayerHitFeedback Instance { get; private set; }

    [Header("Knockback")]
    [Tooltip("Speed (units/sec) the player is pushed when hit. Decays exponentially each frame.")]
    [SerializeField] float _knockbackStrength = 4.0f;
    [Tooltip("Per-frame velocity decay. 0.85 ≈ 60% remaining after 0.05s, 15% remaining after 0.15s.")]
    [SerializeField, Range(0.5f, 0.99f)] float _knockbackDecay = 0.85f;
    [Tooltip("Hard cap on how long knockback velocity may be applied.")]
    [SerializeField] float _knockbackDuration = 0.15f;

    [Header("Camera Shake")]
    [Tooltip("Min seconds between shakes. Throttles spam when many enemies hit at once.")]
    [SerializeField] float _shakeCooldown   = 0.15f;
    [SerializeField] float _shakeAmplitude  = 0.18f;
    [SerializeField] float _shakeDuration   = 0.12f;
    [Tooltip("Multiplier for shake amplitude on bigger sources (boss / tower). Pass intensityMul=2+ to OnHit.")]
    [SerializeField] float _strongShakeMul  = 2.0f;

    Vector2 _knockbackVelocity;
    float   _knockbackTimer;
    float   _lastShakeTime = -100f;

    PlayerStats  _stats;
    CameraFollow _cameraFollow;

    void Awake()
    {
        Instance      = this;
        _stats        = GetComponent<PlayerStats>();
        var cam       = Camera.main;
        if (cam != null) _cameraFollow = cam.GetComponent<CameraFollow>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Called by PlayerStats when the player takes damage with a known source position.
    /// Applies knockback away from the source and a (throttled) camera shake.</summary>
    public void OnHit(Vector2 fromPos, float intensityMul = 1f)
    {
        // Knockback — direction is away from the hit source, scaled by intensity and resist.
        Vector2 dir = (Vector2)transform.position - fromPos;
        if (dir.sqrMagnitude > 0.0001f)
        {
            float resist = _stats != null ? Mathf.Clamp01(_stats.knockbackResist) : 0f;
            float strength = _knockbackStrength * intensityMul * (1f - resist);
            _knockbackVelocity = dir.normalized * strength;
            _knockbackTimer    = _knockbackDuration;
        }

        // Camera shake — throttled. Many simultaneous hits collapse into one shake.
        if (_cameraFollow != null && Time.time - _lastShakeTime >= _shakeCooldown)
        {
            _lastShakeTime = Time.time;
            float amp = _shakeAmplitude * (intensityMul >= 2f ? _strongShakeMul : 1f);
            _cameraFollow.Shake(amp, _shakeDuration);
        }
    }

    void Update()
    {
        if (_knockbackTimer <= 0f) return;
        if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        transform.position += (Vector3)(_knockbackVelocity * dt);
        // Exponential per-frame decay, framerate-corrected so 60 vs 144 fps feels similar.
        float decay = Mathf.Pow(_knockbackDecay, dt * 60f);
        _knockbackVelocity *= decay;
        _knockbackTimer    -= dt;
    }
}
