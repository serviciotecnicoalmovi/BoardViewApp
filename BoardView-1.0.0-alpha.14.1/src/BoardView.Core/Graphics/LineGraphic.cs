using BoardView.Core.Geometry;

namespace BoardView.Core.Graphics;

/// <summary>Segmento de línea con ancho físico.</summary>
public sealed class LineGraphic : GraphicObject
{
    public LineGraphic(string id, Point2D start, Point2D end, double width)
        : base(id, CreateBounds(start, end, width))
    {
        if (width < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        Start = start;
        End = end;
        Width = width;
    }

    public Point2D Start { get; }
    public Point2D End { get; }
    public double Width { get; }

    private static Bounds2D CreateBounds(Point2D start, Point2D end, double width)
    {
        double radius = Math.Max(0D, width) / 2D;
        return new Bounds2D(
            Math.Min(start.X, end.X) - radius,
            Math.Min(start.Y, end.Y) - radius,
            Math.Max(start.X, end.X) + radius,
            Math.Max(start.Y, end.Y) + radius);
    }
}
