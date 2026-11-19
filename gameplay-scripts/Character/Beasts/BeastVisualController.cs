using UnityEngine;

/// <summary>
/// Swaps the player's gameplay sprite based on which beast is selected. The Animator on the
/// player drives Goblin idle/run by default; for non-Goblin beasts (Skeleton/Golem/...) this
/// component disables the Animator and cycles the sliced idle / run frames directly on the
/// SpriteRenderer. Designed so the user only assigns frames in the Inspector — no per-beast
/// AnimatorController asset needed.
///
/// Per the Goal-of-the-Day brief: "Use the beast sprite/animation in gameplay" so the
/// chosen beast actually appears during play.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BeastVisualController : MonoBehaviour
{
    [System.Serializable]
    public class BeastVisuals
    {
        public BeastType type;
        public Sprite[]  idleFrames;
        public Sprite[]  runFrames;
        [Tooltip("Optional scale multiplier when this beast is active. Stacks with the BeastConfig.scaleMul that BeastApplier already applies.")]
        public Vector3   localScaleOverride = Vector3.one;
    }

    [SerializeField] BeastVisuals[] _visuals;
    [SerializeField] float          _fps            = 6f;
    [Tooltip("When true the existing Animator on the player is disabled while a non-Goblin beast is active so it doesn't fight the sprite swaps.")]
    [SerializeField] bool           _disableAnimatorOnSwap = true;

    SpriteRenderer  _sr;
    Animator        _anim;
    PlayerMovements _movement;

    BeastVisuals _active;
    BeastType    _appliedBeast;
    bool         _initialized;
    float        _frameAccum;
    int          _frameIndex;
    bool         _wasMoving;

    void Awake()
    {
        _sr       = GetComponent<SpriteRenderer>();
        _anim     = GetComponent<Animator>();
        _movement = GetComponent<PlayerMovements>();
    }

    void OnEnable()
    {
        // Re-apply on enable so reloads pick the right beast immediately.
        _initialized = false;
    }

    void LateUpdate()
    {
        // Late so we run AFTER the Animator wrote a sprite this frame; we can overwrite cleanly.
        BeastType current = BeastManager.Instance != null
            ? CharacterManager.ResolveEffectiveBeast(BeastManager.Instance)
            : BeastType.Goblin;

        if (!_initialized || current != _appliedBeast)
        {
            ApplyBeast(current);
            _initialized = true;
        }

        if (_active == null) return;       // Goblin / no override — let the Animator drive.

        bool moving = _movement != null && _movement.LastMoveDir != Vector2.zero && Mathf.Abs(transform.position.x) >= 0f
                      ? IsMovingNow()
                      : false;

        Sprite[] frames = moving ? _active.runFrames : _active.idleFrames;
        if (frames == null || frames.Length == 0) frames = _active.idleFrames;
        if (frames == null || frames.Length == 0) return;

        if (moving != _wasMoving)
        {
            _frameAccum = 0f;
            _frameIndex = 0;
            _wasMoving  = moving;
        }

        _frameAccum += Time.deltaTime;
        float step = 1f / Mathf.Max(0.01f, _fps);
        while (_frameAccum >= step)
        {
            _frameAccum -= step;
            _frameIndex = (_frameIndex + 1) % frames.Length;
        }
        if (_sr != null) _sr.sprite = frames[Mathf.Clamp(_frameIndex, 0, frames.Length - 1)];
    }

    bool IsMovingNow()
    {
        // PlayerMovements doesn't expose its current input directly — derive via the SetBool the
        // Animator already reads. We mirror its logic: input != zero last frame ⇒ moving.
        return InputService.MoveAxis.sqrMagnitude > 0.0001f;
    }

    void ApplyBeast(BeastType beast)
    {
        _appliedBeast = beast;
        _active = null;
        if (_visuals != null)
        {
            for (int i = 0; i < _visuals.Length; i++)
            {
                if (_visuals[i] != null && _visuals[i].type == beast)
                {
                    if (_visuals[i].idleFrames != null && _visuals[i].idleFrames.Length > 0)
                    {
                        _active = _visuals[i];
                    }
                    break;
                }
            }
        }

        // Re-enable the Animator when no override is in effect (Goblin / unconfigured beast)
        // so the default gameplay animation keeps working. When we DO have frames we disable
        // it to stop the controller from writing over our swapped sprite.
        if (_anim != null) _anim.enabled = (_active == null || !_disableAnimatorOnSwap);

        if (_active != null)
        {
            // Apply the per-beast scale override. BeastApplier has already scaled the transform
            // by BeastConfig.scaleMul, so we MULTIPLY here instead of overwriting — preserves
            // the design-time size hierarchy across beasts.
            transform.localScale = new Vector3(
                transform.localScale.x * _active.localScaleOverride.x,
                transform.localScale.y * _active.localScaleOverride.y,
                transform.localScale.z);
        }
    }
}
