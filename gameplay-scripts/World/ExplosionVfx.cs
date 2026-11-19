using UnityEngine;

/// <summary>
/// One-shot pooled explosion sprite animation. Cycles a configurable frame array on a
/// SpriteRenderer at a fixed FPS, then returns itself to its pool. Used by tower destruction
/// (per Goal-of-the-Day brief) — no Instantiate/Destroy in combat, every explosion comes
/// from a pre-allocated pool.
/// </summary>
public class ExplosionVfx : MonoBehaviour
{
    DynamicPool<ExplosionVfx> _pool;
    SpriteRenderer            _sr;
    Sprite[]                  _frames;
    float                     _fps = 18f;
    float                     _elapsed;
    int                       _index;
    bool                      _active;

    public void Configure(DynamicPool<ExplosionVfx> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
    }

    /// <summary>Launch the effect at the given world position with the given frame array.</summary>
    public void Play(Vector3 worldPos, Sprite[] frames, float fps, float visualScale)
    {
        transform.position   = worldPos;
        transform.localScale = new Vector3(visualScale, visualScale, 1f);
        _frames  = frames;
        _fps     = Mathf.Max(1f, fps);
        _elapsed = 0f;
        _index   = 0;
        _active  = true;
        if (_sr != null)
        {
            _sr.sprite  = (_frames != null && _frames.Length > 0) ? _frames[0] : null;
            _sr.color   = Color.white;
            _sr.enabled = _sr.sprite != null;
        }
    }

    void Update()
    {
        if (!_active) return;
        if (_frames == null || _frames.Length == 0)
        {
            Recycle();
            return;
        }
        _elapsed += Time.deltaTime;
        int frame = Mathf.FloorToInt(_elapsed * _fps);
        if (frame >= _frames.Length)
        {
            Recycle();
            return;
        }
        if (frame != _index && _sr != null)
        {
            _index     = frame;
            _sr.sprite = _frames[_index];
        }
    }

    void Recycle()
    {
        if (!_active) return;
        _active = false;
        if (_sr != null) _sr.enabled = false;
        if (_pool != null) _pool.Return(this);
        else               gameObject.SetActive(false);
    }
}
