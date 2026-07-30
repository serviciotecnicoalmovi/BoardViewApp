using BoardView.Core.Geometry;

namespace BoardView.Core.Spatial;

/// <summary>Represents one object returned by an advanced spatial query.</summary>
/// <typeparam name="T">Type of indexed object.</typeparam>
public sealed record SpatialHit<T>(T Item, Bounds2D Bounds, double Distance) where T : notnull;
