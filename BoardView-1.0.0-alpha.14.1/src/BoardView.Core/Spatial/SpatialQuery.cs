using BoardView.Core.Geometry;

namespace BoardView.Core.Spatial;

/// <summary>
/// Describes an immutable spatial query. A query can use an area or a circular proximity
/// search and can optionally apply a domain predicate after spatial candidate reduction.
/// </summary>
/// <typeparam name="T">Type of indexed object.</typeparam>
public sealed class SpatialQuery<T> where T : notnull
{
    private SpatialQuery(
        Bounds2D area,
        Point2D? origin,
        double? radius,
        Func<T, bool>? predicate,
        int? maximumResults)
    {
        Area = area;
        Origin = origin;
        Radius = radius;
        Predicate = predicate;
        MaximumResults = maximumResults;
    }

    /// <summary>Gets the rectangular candidate area.</summary>
    public Bounds2D Area { get; }

    /// <summary>Gets the distance origin when proximity ordering is requested.</summary>
    public Point2D? Origin { get; }

    /// <summary>Gets the circular radius when the query is constrained to a circle.</summary>
    public double? Radius { get; }

    /// <summary>Gets the optional domain predicate.</summary>
    public Func<T, bool>? Predicate { get; }

    /// <summary>Gets the optional maximum number of returned hits.</summary>
    public int? MaximumResults { get; }

    /// <summary>Creates a rectangular query.</summary>
    public static SpatialQuery<T> InArea(
        Bounds2D area,
        Func<T, bool>? predicate = null,
        int? maximumResults = null)
    {
        ValidateMaximum(maximumResults);
        return new SpatialQuery<T>(area, null, null, predicate, maximumResults);
    }

    /// <summary>Creates a proximity query centered at a point.</summary>
    public static SpatialQuery<T> Near(
        Point2D origin,
        double radius,
        Func<T, bool>? predicate = null,
        int? maximumResults = null)
    {
        if (!double.IsFinite(radius) || radius < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        ValidateMaximum(maximumResults);
        Bounds2D area = new(
            origin.X - radius,
            origin.Y - radius,
            origin.X + radius,
            origin.Y + radius);
        return new SpatialQuery<T>(area, origin, radius, predicate, maximumResults);
    }

    private static void ValidateMaximum(int? maximumResults)
    {
        if (maximumResults is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResults));
        }
    }
}
