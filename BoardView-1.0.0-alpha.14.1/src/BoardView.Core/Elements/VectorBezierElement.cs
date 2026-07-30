using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Segmento Bézier cúbico importado desde un documento vectorial.</summary>
public sealed class VectorBezierElement : BoardElement
{
    /// <summary>Inicializa un segmento Bézier.</summary>
    public VectorBezierElement(
        string id,
        string layerId,
        Point2D start,
        Point2D control1,
        Point2D control2,
        Point2D end,
        double width)
        : base(id, layerId, CreateBounds(start, control1, control2, end, width))
    {
        if (width < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        Start = start;
        Control1 = control1;
        Control2 = control2;
        End = end;
        Width = width;
    }

    public Point2D Start { get; }
    public Point2D Control1 { get; }
    public Point2D Control2 { get; }
    public Point2D End { get; }
    public double Width { get; }

    private static Bounds2D CreateBounds(
        Point2D start,
        Point2D control1,
        Point2D control2,
        Point2D end,
        double width)
    {
        Bounds2D bounds = Bounds2D.FromPoints([start, control1, control2, end]);
        double radius = Math.Max(0D, width) / 2D;
        return new Bounds2D(
            bounds.Left - radius,
            bounds.Top - radius,
            bounds.Right + radius,
            bounds.Bottom + radius);
    }
}
