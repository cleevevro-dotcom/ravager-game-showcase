public interface ISpatialEntity : IPositionable
{
    SpatialCategory SpatialCategory { get; }
    bool IsActiveInHash { get; }
}
