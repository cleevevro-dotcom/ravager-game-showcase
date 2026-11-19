using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-90)]
public class SpatialHashManager : MonoBehaviour
{
    public static SpatialHashManager Instance { get; private set; }

    [SerializeField] float _cellSize = 2f;

    public struct QueryEvent
    {
        public Vector2         position;
        public float           range;
        public SpatialCategory category;
        public float           recordedAt;
        public int             hitCount;
        public bool            isCellQuery;
    }

    SpatialHash2D<ISpatialEntity>[] _grids;
    List<ISpatialEntity>[]          _registry;
    readonly List<QueryEvent>       _recentQueries = new List<QueryEvent>(32);

    const float QueryVizLifetime = 0.35f;
    static readonly int CategoryCount = System.Enum.GetValues(typeof(SpatialCategory)).Length;

    public float CellSize => _cellSize;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        _grids    = new SpatialHash2D<ISpatialEntity>[CategoryCount];
        _registry = new List<ISpatialEntity>[CategoryCount];
        for (int i = 0; i < CategoryCount; i++)
        {
            _grids[i]    = new SpatialHash2D<ISpatialEntity>(_cellSize);
            _registry[i] = new List<ISpatialEntity>(64);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        RebuildGrids();
        CullOldQueries();
    }

    void RebuildGrids()
    {
        for (int c = 0; c < _grids.Length; c++)
        {
            _grids[c].Clear();
            var list = _registry[c];
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e != null && e.IsActiveInHash)
                    _grids[c].Add(e);
            }
        }
    }

    void CullOldQueries()
    {
        float cutoff = Time.time - QueryVizLifetime;
        for (int i = _recentQueries.Count - 1; i >= 0; i--)
            if (_recentQueries[i].recordedAt < cutoff) _recentQueries.RemoveAt(i);
    }

    public void Register(ISpatialEntity entity)
    {
        if (entity == null || _registry == null) return;
        _registry[(int)entity.SpatialCategory].Add(entity);
    }

    public void Unregister(ISpatialEntity entity)
    {
        if (entity == null || _registry == null) return;
        // Swap-and-pop: order doesn't matter for the spatial registry, and avoids the
        // shift cost of List.Remove during chunk-unload of many buildings at once.
        var list = _registry[(int)entity.SpatialCategory];
        int idx  = list.IndexOf(entity);
        if (idx < 0) return;
        int last = list.Count - 1;
        if (idx != last) list[idx] = list[last];
        list.RemoveAt(last);
    }

    public void Query(SpatialCategory category, Vector2 position, float range, List<ISpatialEntity> results)
    {
        _grids[(int)category].Query(position, range, results);
#if UNITY_EDITOR
        _recentQueries.Add(new QueryEvent
        {
            position = position, range = range, category = category,
            recordedAt = Time.time, hitCount = results.Count, isCellQuery = false
        });
#endif
    }

    public void QueryCell(SpatialCategory category, Vector2 position, List<ISpatialEntity> results)
    {
        _grids[(int)category].QueryCell(position, results);
#if UNITY_EDITOR
        _recentQueries.Add(new QueryEvent
        {
            position = position, range = _cellSize * 0.5f, category = category,
            recordedAt = Time.time, hitCount = results.Count, isCellQuery = true
        });
#endif
    }

    public SpatialHash2D<ISpatialEntity> GetGrid(SpatialCategory category) => _grids[(int)category];
    public List<QueryEvent> GetRecentQueries() => _recentQueries;
}
