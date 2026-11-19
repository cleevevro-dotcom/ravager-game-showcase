using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

public class XpManager : MonoBehaviour
{
    public static XpManager Instance { get; private set; }

    [SerializeField] XpOrb _orbPrefab;
    [SerializeField] int _poolSize = 120;

    ObjectPool<XpOrb> _orbPool;
    Transform _playerTransform;
    PlayerStats _playerStats;

    static readonly CustomSampler s_SpawnOrbSampler = CustomSampler.Create("XpManager.SpawnOrb");

    // Reused for one-shot magnet pickups (e.g. tower kill reward). Pre-sized so a typical
    // tower clear doesn't allocate.
    readonly List<ISpatialEntity> _magnetBuf = new List<ISpatialEntity>(32);

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void Init(Transform player, PlayerStats stats)
    {
        _playerTransform = player;
        _playerStats     = stats;
        // Runtime safety: if the editor builder ever runs after the prefab was already cloned
        // (or the user hasn't run the menu item yet), force the orb sprite to match the
        // configured XP art so dropped orbs always render with the right icon.
        ApplyConfiguredOrbSprite();
        _orbPool         = new ObjectPool<XpOrb>(_orbPrefab, _poolSize, transform);
    }

    void ApplyConfiguredOrbSprite()
    {
        if (_orbPrefab == null) return;
        var art = RavagerArt.Load();
        if (art == null || art.xpOrb == null) return;
        var sr = _orbPrefab.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != art.xpOrb) sr.sprite = art.xpOrb;
    }

    public void SpawnOrb(Vector2 position, float xpValue)
    {
        s_SpawnOrbSampler.Begin();
        XpOrb orb = _orbPool.Get(position, Quaternion.identity);
        orb.Init(position, xpValue, _playerTransform, _playerStats, _orbPool);
        s_SpawnOrbSampler.End();
    }

    /// <summary>
    /// Instant-collects every loose XP orb whose position lies within <paramref name="radius"/>
    /// of <paramref name="center"/>. One-shot: queries the spatial hash once, applies XP, and
    /// returns each orb to the pool. Used as the "magnet pickup" reward when the player clears
    /// a tower. Allocation-free after first call.
    /// </summary>
    public int MagnetCollectInRadius(Vector2 center, float radius)
    {
        if (radius <= 0f || _playerStats == null) return 0;
        var hash = SpatialHashManager.Instance;
        if (hash == null) return 0;

        _magnetBuf.Clear();
        hash.Query(SpatialCategory.XpOrb, center, radius, _magnetBuf);

        int collected = 0;
        for (int i = 0; i < _magnetBuf.Count; i++)
        {
            if (_magnetBuf[i] is XpOrb orb && orb.gameObject.activeSelf)
            {
                orb.ForceCollect();
                collected++;
            }
        }
        return collected;
    }
}
