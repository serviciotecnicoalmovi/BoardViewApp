using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Elipse o círculo documental importado.</summary>
public sealed class VectorEllipseElement : BoardElement
{
    /// <summary>Inicializa una elipse vectorial.</summary>
    public VectorEllipseElement(
        string id,
        string layerId,
        Point2D center,
        double radiusX,
        double radiusY,
        double strokeWidth,
        bool isFilled)
        : base(id, layerId, CreateBounds(center, radiusX, radiusY, strokeWidth))
    {
        if (radiusX <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusX));
        }

        if (radiusY <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusY));
        }

        if (strokeWidth < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        }

        Center = center;
        RadiusX = radiusX;
        RadiusY = radiusY;
        StrokeWidth = strokeWidth;
        IsFilled = isFilled;
    }

    public Point2D Center { get; }
    public double RadiusX { get; }
    public double RadiusY { get; }
    public double StrokeWidth { get; }
    public bool IsFilled { get; }

    private static Bounds2D CreateBounds(
        Point2D center,
        double radiusX,
        double radiusY,
        double strokeWidth)
    {
        double halfStroke = Math.Max(0D, strokeWidth) / 2D;
        return new Bounds2D(
            center.X - radiusX - halfStroke,
            center.Y - radiusY - halfStroke,
            center.X + radiusX + halfStroke,
            center.Y + radiusY + halfStroke);
    }
}
