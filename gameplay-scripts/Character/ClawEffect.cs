using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class ClawEffect : MonoBehaviour
{
    [SerializeField] Sprite[] _frames;
    [SerializeField] float    _fps            = 24f;
    [SerializeField] float    _flashDuration  = 0.10f;     // used when no frames are assigned
    [SerializeField] float    _flashScale     = 1.6f;

    SpriteRenderer _sr;
    float          _timer;
    int            _frame;
    bool           _frameMode;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        // Auto-load the 4-frame Claw_attack spritesheet from Resources/Player/ when the
        // Inspector slot is empty. The user's brief instructed swapping the default
        // ring-flash for the animated claw, so this is the default visual now.
        if (_frames == null || _frames.Length == 0)
        {
            var loaded = Resources.LoadAll<Sprite>("Player/Claw_attack");
            if (loaded != null && loaded.Length > 0)
            {
                // Resources.LoadAll returns sub-sprites in import order, which may
                // not match the Claw_attack_0/1/2/3 logical order — sort by name so
                // the animation always plays left-to-right.
                System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a.name, b.name));
                _frames = loaded;
            }
        }
    }

    public void Play(Vector2 worldPos, float angleDeg)
    {
        transform.position    = worldPos;
        transform.eulerAngles = new Vector3(0f, 0f, angleDeg);
        transform.localScale  = Vector3.one * _flashScale;
        _frame = 0;
        _timer = 0f;
        _frameMode = _frames != null && _frames.Length > 0;
        if (_frameMode && _sr != null) _sr.sprite = _frames[0];
        gameObject.SetActive(true);
    }

    void Update()
    {
        _timer += Time.deltaTime;

        if (_frameMode)
        {
            if (_timer >= 1f / _fps)
            {
                _timer -= 1f / _fps;
                _frame++;
                if (_frame >= _frames.Length) { gameObject.SetActive(false); return; }
                _sr.sprite = _frames[_frame];
            }
        }
        else
        {
            // Simple flash: fade alpha down, then hide.
            if (_sr != null)
            {
                var c = _sr.color;
                c.a = Mathf.Clamp01(1f - _timer / _flashDuration);
                _sr.color = c;
            }
            if (_timer >= _flashDuration)
            {
                if (_sr != null)
                {
                    var c = _sr.color; c.a = 1f; _sr.color = c;
                }
                gameObject.SetActive(false);
            }
        }
    }
}
