using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Segmento conductor recto con ancho definido.</summary>
public sealed class TrackElement : BoardElement
{
    public TrackElement(
        string id,
        string layerId,
        Point2D start,
        Point2D end,
        double width,
        string? netId = null)
        : base(id, layerId, CreateBounds(start, end, width), netId)
    {
        if (width <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "El ancho debe ser mayor que cero.");
        }

        Start = start;
        End = end;
        Width = width;
    }

    public Point2D Start { get; }
    public Point2D End { get; }
    public double Width { get; }
    public double Length => Start.DistanceTo(End);

    private static Bounds2D CreateBounds(Point2D start, Point2D end, double width)
    {
        double radius = Math.Max(width, 0D) / 2D;
        return new Bounds2D(
            Math.Min(start.X, end.X) - radius,
            Math.Min(start.Y, end.Y) - radius,
            Math.Max(start.X, end.X) + radius,
            Math.Max(start.Y, end.Y) + radius);
    }
}
