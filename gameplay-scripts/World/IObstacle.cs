using UnityEngine;

public interface IObstacle : ISpatialEntity
{
    // Half width / height of the obstacle's blocking box in world units.
    // Position (from ISpatialEntity) is the box center.
    Vector2 ObstacleHalfExtents { get; }
}
