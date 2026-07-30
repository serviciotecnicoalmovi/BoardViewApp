using BoardView.Core.Geometry;

namespace BoardView.Core.Graphics;

/// <summary>Texto posicionado, conservando su caja delimitadora original.</summary>
public sealed class TextGraphic : GraphicObject
{
    public TextGraphic(
        string id,
        string text,
        Point2D origin,
        Bounds2D bounds,
        double fontSize,
        double rotationDegrees = 0D)
        : base(id, bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (fontSize <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        Text = text;
        Origin = origin;
        FontSize = fontSize;
        RotationDegrees = rotationDegrees;
    }

    public string Text { get; }
    public Point2D Origin { get; }
    public double FontSize { get; }
    public double RotationDegrees { get; }
}
