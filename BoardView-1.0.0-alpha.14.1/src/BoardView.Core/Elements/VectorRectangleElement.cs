using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Rectángulo documental alineado con los ejes.</summary>
public sealed class VectorRectangleElement : BoardElement
{
    /// <summary>Inicializa un rectángulo vectorial.</summary>
    public VectorRectangleElement(
        string id,
        string layerId,
        Bounds2D rectangle,
        double strokeWidth,
        bool isFilled)
        : base(id, layerId, rectangle)
    {
        if (rectangle.IsEmpty)
        {
            throw new ArgumentException("El rectángulo debe tener un área positiva.", nameof(rectangle));
        }

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
