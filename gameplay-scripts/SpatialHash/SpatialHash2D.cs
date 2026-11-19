using System.Collections.Generic;
using UnityEngine;

public class SpatialHash2D<T> where T : IPositionable
{
    readonly float _cellSize;
    readonly Dictionary<Vector2Int, List<T>> _grid = new Dictionary<Vector2Int, List<T>>();

    public float CellSize => _cellSize;

    public SpatialHash2D(float cellSize) => _cellSize = cellSize;

    public Vector2Int GetCell(Vector2 pos) => new Vector2Int(
        Mathf.FloorToInt(pos.x / _cellSize),
        Mathf.FloorToInt(pos.y / _cellSize));

    public void Add(T item)
    {
        Vector2Int cell = GetCell(item.Position);
        if (!_grid.TryGetValue(cell, out var list))
        {
            list = new List<T>();
            _grid[cell] = list;
        }
        list.Add(item);
    }

    public void Clear()
    {
        foreach (var kv in _grid) kv.Value.Clear();
    }

    public void Query(Vector2 position, float range, List<T> results)
    {
        results.Clear();
        int cellRadius = Mathf.CeilToInt(range / _cellSize);
        Vector2Int center = GetCell(position);
        float rangeSqr = range * range;
        for (int x = -cellRadius; x <= cellRadius; x++)
        {
            for (int y = -cellRadius; y <= cellRadius; y++)
            {
                var cell = new Vector2Int(center.x + x, center.y + y);
                if (_grid.TryGetValue(cell, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if ((list[i].Position - position).sqrMagnitude <= rangeSqr)
                            results.Add(list[i]);
                    }
                }
            }
        }
    }

    public void QueryCell(Vector2 position, List<T> results)
    {
        results.Clear();
        if (_grid.TryGetValue(GetCell(position), out var list))
        {
            for (int i = 0; i < list.Count; i++)
                results.Add(list[i]);
        }
    }

    public Dictionary<Vector2Int, List<T>> GetGrid() => _grid;
}
