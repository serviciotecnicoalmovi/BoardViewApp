using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Área conductora destinada a conexión o montaje de un componente.</summary>
public sealed class PadElement : BoardElement
{
    public PadElement(
        string id,
        string layerId,
        Point2D position,
        double width,
        double height,
        PadShape shape,
        string? netId = null)
        : base(id, layerId, CreateBounds(position, width, height), netId)
    {
        if (width <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "El ancho debe ser mayor que cero.");
        }

        if (height <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "La altura debe ser mayor que cero.");
        }

        Position = position;
        Width = width;
        Height = height;
        Shape = shape;
    }

    public Point2D Position { get; }
    public double Width { get; }
    public double Height { get; }
    public PadShape Shape { get; }

    private static Bounds2D CreateBounds(Point2D position, double width, double height)
    {
        double halfWidth = Math.Max(width, 0D) / 2D;
        double halfHeight = Math.Max(height, 0D) / 2D;
        return new Bounds2D(
            position.X - halfWidth,
            position.Y - halfHeight,
            position.X + halfWidth,
            position.Y + halfHeight);
    }
}
