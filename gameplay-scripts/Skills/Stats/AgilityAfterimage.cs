using UnityEngine;

/// <summary>
/// Decoy clone dropped by <see cref="AgilitySkill"/> at T3. Static visual that fades over its
/// short lifetime — purely a visual; enemy AI re-targets the real player anyway, but the clone
/// briefly draws attention by being the most-recently-seen "player" in the spatial hash if the
/// enemy AI ever consults it (cheap, allocation-free).
/// </summary>
public class AgilityAfterimage : MonoBehaviour
{
    SpriteRenderer _sr;
    DynamicPool<AgilityAfterimage> _pool;
    float _spawnTime;
    float _lifetime;

    public void Configure(DynamicPool<AgilityAfterimage> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
    }

    public void Launch(Vector2 pos, float lifetime)
    {
        transform.position = pos;
        transform.localScale = new Vector3(0.4f, 0.4f, 1f);
        _spawnTime = Time.time;
        _lifetime  = lifetime;
        if (_sr != null)
        {
            var c = SkillColors.Agility;
            c.a = 0.55f;
            _sr.color = c;
        }
    }

    void Update()
    {
        float elapsed = Time.time - _spawnTime;
        if (elapsed >= _lifetime) { _pool?.Return(this); return; }
        if (_sr != null)
        {
            var c = _sr.color;
            c.a = Mathf.Lerp(0.55f, 0f, elapsed / _lifetime);
            _sr.color = c;
        }
    }
}
