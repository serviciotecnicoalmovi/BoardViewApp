using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Texto normalizado asociado a una capa de la placa.</summary>
public sealed class TextElement : BoardElement
{
    public TextElement(string id, string layerId, string text, Point2D position, double height, double rotationDegrees = 0D)
        : base(id, layerId, EstimateBounds(text, position, height))
    {
        ArgumentNullException.ThrowIfNull(text);
        if (height <= 0D) throw new ArgumentOutOfRangeException(nameof(height));
        Text = text;
        Position = position;
        Height = height;
        RotationDegrees = Normalize(rotationDegrees);
    }

    public string Text { get; }
    public Point2D Position { get; }
    public double Height { get; }
    public double RotationDegrees { get; }

    private static Bounds2D EstimateBounds(string text, Point2D position, double height)
    {
        double width = Math.Max(height * 0.5D, text.Length * height * 0.6D);
        return new Bounds2D(position.X, position.Y - height, position.X + width, position.Y);
    }

    private static double Normalize(double value)
    {
        double normalized = value % 360D;
        return normalized < 0D ? normalized + 360D : normalized;
    }
}
