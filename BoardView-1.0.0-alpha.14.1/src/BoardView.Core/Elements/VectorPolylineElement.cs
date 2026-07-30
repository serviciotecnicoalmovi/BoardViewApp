using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Polilínea documental abierta o cerrada sin semántica eléctrica.</summary>
public sealed class VectorPolylineElement : BoardElement
{
    /// <summary>Inicializa una polilínea vectorial.</summary>
    public VectorPolylineElement(
        string id,
        string layerId,
        IEnumerable<Point2D> points,
        double width,
        bool isClosed)
        : this(id, layerId, Materialize(points), width, isClosed)
    {
    }

    private VectorPolylineElement(
        string id,
        string layerId,
        IReadOnlyList<Point2D> points,
        double width,
        bool isClosed)
        : base(id, layerId, CreateBounds(points, width))
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
        return Array.AsReadOnly(points.ToArray());
    }

    private static Bounds2D CreateBounds(IReadOnlyList<Point2D> points, double width)
    {
        Bounds2D bounds = Bounds2D.FromPoints(points);
        double radius = Math.Max(0D, width) / 2D;
        return new Bounds2D(
            bounds.Left - radius,
            bounds.Top - radius,
            bounds.Right + radius,
            bounds.Bottom + radius);
    }
}
