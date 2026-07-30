using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Arco circular normalizado.</summary>
public sealed class ArcElement : BoardElement
{
    public ArcElement(string id, string layerId, Point2D center, double radius, double startAngleDegrees, double sweepAngleDegrees, double width, string? netId = null)
        : base(id, layerId, CreateBounds(center, radius, width), netId)
    {
        if (radius <= 0D) throw new ArgumentOutOfRangeException(nameof(radius));
        if (width < 0D) throw new ArgumentOutOfRangeException(nameof(width));
        Center = center;
        Radius = radius;
        StartAngleDegrees = startAngleDegrees;
        SweepAngleDegrees = sweepAngleDegrees;
        Width = width;
    }

    public Point2D Center { get; }
    public double Radius { get; }
    public double StartAngleDegrees { get; }
    public double SweepAngleDegrees { get; }
    public double Width { get; }

    private static Bounds2D CreateBounds(Point2D center, double radius, double width)
    {
        double extent = radius + (width / 2D);
        return new Bounds2D(center.X - extent, center.Y - extent, center.X + extent, center.Y + extent);
    }
}
