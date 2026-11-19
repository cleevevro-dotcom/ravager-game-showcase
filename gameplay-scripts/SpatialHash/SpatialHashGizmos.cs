#if UNITY_EDITOR
using UnityEngine;

public class SpatialHashGizmos : MonoBehaviour
{
    [SerializeField] bool  _drawCells   = true;
    [SerializeField] bool  _drawEntities = true;
    [SerializeField] bool  _drawQueries = true;
    [SerializeField] float _queryAlpha  = 0.8f;

    [SerializeField] Color _colorPlayer     = new Color(0.3f, 0.7f, 1f);
    [SerializeField] Color _colorEnemy      = new Color(1f,   0.3f, 0.3f);
    [SerializeField] Color _colorProjectile = new Color(1f,   0.85f, 0.2f);
    [SerializeField] Color _colorXpOrb      = new Color(0.4f, 1f,    0.4f);
    [SerializeField] Color _colorBuilding   = new Color(0.75f, 0.55f, 0.3f);

    Color ColorFor(SpatialCategory c)
    {
        switch (c)
        {
            case SpatialCategory.Player:     return _colorPlayer;
            case SpatialCategory.Enemy:      return _colorEnemy;
            case SpatialCategory.Projectile: return _colorProjectile;
            case SpatialCategory.XpOrb:      return _colorXpOrb;
            case SpatialCategory.Building:   return _colorBuilding;
        }
        return Color.gray;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        var mgr = SpatialHashManager.Instance;
        if (mgr == null) return;

        if (_drawCells || _drawEntities) DrawGrids(mgr);
        if (_drawQueries) DrawQueries(mgr);
    }

    void DrawGrids(SpatialHashManager mgr)
    {
        float cs = mgr.CellSize;
        foreach (SpatialCategory cat in System.Enum.GetValues(typeof(SpatialCategory)))
        {
            var grid = mgr.GetGrid(cat).GetGrid();
            Color col = ColorFor(cat);
            foreach (var kv in grid)
            {
                if (kv.Value.Count == 0) continue;
                Vector3 world = new Vector3(kv.Key.x * cs + cs * 0.5f, kv.Key.y * cs + cs * 0.5f, 0f);

                if (_drawCells)
                {
                    Gizmos.color = new Color(col.r, col.g, col.b, 0.9f);
                    Gizmos.DrawWireCube(world, new Vector3(cs, cs, 0f));
                }

                if (_drawEntities)
                {
                    Gizmos.color = col;
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        Vector2 p = kv.Value[i].Position;
                        Gizmos.DrawSphere(new Vector3(p.x, p.y, 0f), cs * 0.08f);
                    }
                }
            }
        }
    }

    void DrawQueries(SpatialHashManager mgr)
    {
        var queries = mgr.GetRecentQueries();
        for (int i = 0; i < queries.Count; i++)
        {
            var q = queries[i];
            Color c = ColorFor(q.category);
            c.a = _queryAlpha;
            Gizmos.color = c;

            Vector3 center = new Vector3(q.position.x, q.position.y, 0f);
            if (q.isCellQuery)
            {
                float cs = mgr.CellSize;
                Vector2Int cell = mgr.GetGrid(q.category).GetCell(q.position);
                Vector3 world = new Vector3(cell.x * cs + cs * 0.5f, cell.y * cs + cs * 0.5f, 0f);
                Gizmos.DrawWireCube(world, new Vector3(cs, cs, 0f));
            }
            else
            {
                Gizmos.DrawWireSphere(center, q.range);
            }

            if (q.hitCount > 0)
            {
                Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
                Gizmos.DrawSphere(center, 0.1f);
            }
        }
    }
}
#endif
