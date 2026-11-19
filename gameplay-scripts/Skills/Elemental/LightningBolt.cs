using UnityEngine;

public class LightningBolt : MonoBehaviour
{
    SpriteRenderer _sr;
    DynamicPool<LightningBolt> _pool;
    float _lifetime = 0.12f;
    float _spawnTime;

    public void Configure(DynamicPool<LightningBolt> pool, SpriteRenderer sr)
    {
        _pool = pool;
        _sr   = sr;
    }

    public void Show(Vector2 from, Vector2 to, float thickness, Color color)
    {
        Vector2 mid = (from + to) * 0.5f;
        Vector2 d   = to - from;
        float len   = d.magnitude;
        float ang   = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

        transform.position   = mid;
        transform.rotation   = Quaternion.Euler(0f, 0f, ang);
        // PlaceholderSprites squares are 32px @ 32ppu = 1 unit. Scale to target world size.
        transform.localScale = new Vector3(len, thickness, 1f);
        if (_sr != null) _sr.color = color;
        _spawnTime = Time.time;
    }

    void Update()
    {
        if (Time.time - _spawnTime >= _lifetime) _pool?.Return(this);
    }
}
