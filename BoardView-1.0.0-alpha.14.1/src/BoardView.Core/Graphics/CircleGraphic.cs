using BoardView.Core.Geometry;

namespace BoardView.Core.Graphics;

/// <summary>Círculo definido por centro y radio.</summary>
public sealed class CircleGraphic : GraphicObject
{
    public CircleGraphic(string id, Point2D center, double radius, double strokeWidth = 0D, bool isFilled = false)
        : base(id, CreateBounds(center, radius))
    {
        if (radius < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        if (strokeWidth < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        }

        Center = center;
        Radius = radius;
        StrokeWidth = strokeWidth;
        IsFilled = isFilled;
    }

    public Point2D Center { get; }
    public double Radius { get; }
    public double StrokeWidth { get; }
    public bool IsFilled { get; }

    private static Bounds2D CreateBounds(Point2D center, double radius)
    {
        double safeRadius = Math.Max(0D, radius);
        return new Bounds2D(
            center.X - safeRadius,
            center.Y - safeRadius,
            center.X + safeRadius,
            center.Y + safeRadius);
    }
}
