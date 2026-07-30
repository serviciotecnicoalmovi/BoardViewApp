using BoardView.Core.Geometry;

namespace BoardView.Core.Graphics;

/// <summary>Secuencia abierta o cerrada de puntos.</summary>
public sealed class PolylineGraphic : GraphicObject
{
    public PolylineGraphic(string id, IEnumerable<Point2D> points, double width, bool isClosed = false)
        : this(id, Materialize(points), width, isClosed)
    {
    }

    private PolylineGraphic(string id, IReadOnlyList<Point2D> points, double width, bool isClosed)
        : base(id, CreateBounds(points, width))
    {
        if (points.Count < 2)
        {
            throw new ArgumentException("Una polilínea requiere al menos dos puntos.", nameof(points));
        }

        if (width < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        Points = points;
        Width = width;
        IsClosed = isClosed;
    }

    public IReadOnlyList<Point2D> Points { get; }
    public double Width { get; }
    public bool IsClosed { get; }

    private static IReadOnlyList<Point2D> Materialize(IEnumerable<Point2D> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return points.ToArray();
    }

    private static Bounds2D CreateBounds(IReadOnlyList<Point2D> points, double width)
    {
        if (points.Count == 0)
        {
            return default;
        }

        Bounds2D bounds = Bounds2D.FromPoints(points);
        double radius = Math.Max(0D, width) / 2D;
        return new Bounds2D(
            bounds.Left - radius,
            bounds.Top - radius,
            bounds.Right + radius,
            bounds.Bottom + radius);
    }
}
