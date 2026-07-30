using BoardView.Core.Geometry;
using BoardView.Core.Graphics;

namespace BoardView.Core.Documents.Common;

/// <summary>Página o superficie lógica de un documento técnico.</summary>
public sealed class DocumentPage
{
    private readonly List<GraphicObject> graphics = [];

    public DocumentPage(int number, double width, double height, MeasurementUnit sourceUnit)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        if (width <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Number = number;
        Width = width;
        Height = height;
        SourceUnit = sourceUnit;
    }

    public int Number { get; }
    public double Width { get; }
    public double Height { get; }
    public MeasurementUnit SourceUnit { get; }
    public IReadOnlyList<GraphicObject> Graphics => graphics;
    public DocumentMetadata Metadata { get; } = new();
    public Bounds2D Bounds => new(0D, 0D, Width, Height);

    public void AddGraphic(GraphicObject graphic)
    {
        ArgumentNullException.ThrowIfNull(graphic);
        if (graphics.Any(item => string.Equals(item.Id, graphic.Id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"La página {Number} ya contiene un objeto con el identificador '{graphic.Id}'.");
        }

        graphics.Add(graphic);
    }
}
