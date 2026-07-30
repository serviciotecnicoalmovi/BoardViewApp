using BoardView.Core.Geometry;

namespace BoardView.Core.Graphics;

/// <summary>Rectángulo alineado con los ejes.</summary>
public sealed class RectangleGraphic : GraphicObject
{
    public RectangleGraphic(string id, Bounds2D rectangle, double strokeWidth = 0D, bool isFilled = false)
        : base(id, rectangle)
    {
        if (strokeWidth < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        }

        Rectangle = rectangle;
        StrokeWidth = strokeWidth;
        IsFilled = isFilled;
    }

    public Bounds2D Rectangle { get; }
    public double StrokeWidth { get; }
    public bool IsFilled { get; }
}
