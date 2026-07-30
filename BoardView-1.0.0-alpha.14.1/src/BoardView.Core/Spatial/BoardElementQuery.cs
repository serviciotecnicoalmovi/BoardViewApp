using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Core.Spatial;

/// <summary>
/// Defines spatial and domain filters used to retrieve board elements for rendering,
/// selection, search, net tracing and cross-probe operations.
/// </summary>
public sealed record class BoardElementQuery
{
    private BoardElementQuery(Bounds2D area, Point2D? origin, double? radius)
    {
        Area = area;
        Origin = origin;
        Radius = radius;
    }

    /// <summary>Gets the requested candidate area.</summary>
    public Bounds2D Area { get; }

    /// <summary>Gets the optional proximity origin.</summary>
    public Point2D? Origin { get; }

    /// <summary>Gets the optional circular query radius.</summary>
    public double? Radius { get; }

    /// <summary>Gets or sets whether hidden elements must be excluded.</summary>
    public bool VisibleOnly { get; init; } = true;

    /// <summary>Gets or sets the accepted layer identifiers.</summary>
    public IReadOnlySet<string>? LayerIds { get; init; }

    /// <summary>Gets or sets the accepted net identifiers.</summary>
    public IReadOnlySet<string>? NetIds { get; init; }

    /// <summary>Gets or sets the accepted component identifiers.</summary>
    public IReadOnlySet<string>? ComponentIds { get; init; }

    /// <summary>Gets or sets the accepted concrete or base element types.</summary>
    public IReadOnlySet<Type>? ElementTypes { get; init; }

    /// <summary>Gets or sets the maximum number of results.</summary>
    public int? MaximumResults { get; init; }

    /// <summary>Creates a rectangular board-element query.</summary>
    public static BoardElementQuery InArea(Bounds2D area) => new(area, null, null);

    /// <summary>Creates a circular proximity board-element query.</summary>
    public static BoardElementQuery Near(Point2D origin, double radius)
    {
        if (!double.IsFinite(radius) || radius < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        return new BoardElementQuery(
            new Bounds2D(origin.X - radius, origin.Y - radius, origin.X + radius, origin.Y + radius),
            origin,
            radius);
    }

    internal SpatialQuery<BoardElement> ToSpatialQuery()
    {
        if (MaximumResults is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumResults));
        }

        bool Predicate(BoardElement element)
        {
            if (VisibleOnly && !element.IsVisible)
            {
                return false;
            }

            if (LayerIds is not null && !LayerIds.Contains(element.LayerId))
            {
                return false;
            }

            if (NetIds is not null && (element.NetId is null || !NetIds.Contains(element.NetId)))
            {
                return false;
            }

            if (ComponentIds is not null &&
                (element.ComponentId is null || !ComponentIds.Contains(element.ComponentId)))
            {
                return false;
            }

            return ElementTypes is null || ElementTypes.Any(type => type.IsAssignableFrom(element.GetType()));
        }

        return Origin is Point2D origin && Radius is double radius
            ? SpatialQuery<BoardElement>.Near(origin, radius, Predicate, MaximumResults)
            : SpatialQuery<BoardElement>.InArea(Area, Predicate, MaximumResults);
    }
}
