using System.Collections.Generic;
using UnityEngine;

public static class BuildingCollision
{
    // Generous query margin so we still find big buildings whose center sits a few
    // units away but whose box edge is right next to the query point.
    const float MaxBuildingExtent = 8f;

    public static Vector2 Resolve(SpatialHashManager spatial, Vector2 desired, float selfRadius,
                                  List<ISpatialEntity> buffer)
    {
        if (spatial == null) return desired;

        spatial.Query(SpatialCategory.Building, desired, selfRadius + MaxBuildingExtent, buffer);
        int n = buffer.Count;
        if (n == 0) return desired;

        Vector2 pos = desired;
        for (int i = 0; i < n; i++)
        {
            var ent = buffer[i];
            if (ent == null || !ent.IsActiveInHash) continue;
            var obs = ent as IObstacle;
            if (obs == null) continue;

            Vector2 c    = obs.Position;
            Vector2 half = obs.ObstacleHalfExtents;
            if (half.x <= 0f || half.y <= 0f) continue;

            // Closest point on the AABB to the circle center.
            float cx = Mathf.Clamp(pos.x, c.x - half.x, c.x + half.x);
            float cy = Mathf.Clamp(pos.y, c.y - half.y, c.y + half.y);
            Vector2 closest = new Vector2(cx, cy);
            Vector2 diff    = pos - closest;
            float   d2      = diff.sqrMagnitude;
            float   r2      = selfRadius * selfRadius;

            if (d2 >= r2) continue;

            if (d2 > 1e-6f)
            {
                float d = Mathf.Sqrt(d2);
                pos = closest + diff * (selfRadius / d);
            }
            else
            {
                // Inside the box — push out along the shortest axis.
                float dxLeft  = pos.x - (c.x - half.x);
                float dxRight = (c.x + half.x) - pos.x;
                float dyDown  = pos.y - (c.y - half.y);
                float dyUp    = (c.y + half.y) - pos.y;
                float minDx   = dxLeft < dxRight ? dxLeft : dxRight;
                float minDy   = dyDown < dyUp    ? dyDown : dyUp;
                if (minDx < minDy)
                    pos.x = dxLeft < dxRight ? c.x - half.x - selfRadius
                                             : c.x + half.x + selfRadius;
                else
                    pos.y = dyDown < dyUp    ? c.y - half.y - selfRadius
                                             : c.y + half.y + selfRadius;
            }
        }
        return pos;
    }

    // Closest-point AABB-vs-circle test, returning the closest box point and the squared
    // distance. Callers use it for projectile/area hit checks against a Destructible.
    public static bool CircleOverlapsBox(Vector2 boxCenter, Vector2 boxHalf,
                                         Vector2 circleCenter, float circleRadius,
                                         out Vector2 closest, out float distSqr)
    {
        float cx = Mathf.Clamp(circleCenter.x, boxCenter.x - boxHalf.x, boxCenter.x + boxHalf.x);
        float cy = Mathf.Clamp(circleCenter.y, boxCenter.y - boxHalf.y, boxCenter.y + boxHalf.y);
        closest = new Vector2(cx, cy);
        distSqr = (circleCenter - closest).sqrMagnitude;
        return distSqr <= circleRadius * circleRadius;
    }
}
