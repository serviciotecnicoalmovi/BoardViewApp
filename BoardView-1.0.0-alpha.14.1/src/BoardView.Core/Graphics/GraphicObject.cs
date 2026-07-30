using BoardView.Core.Documents.Common;
using BoardView.Core.Geometry;

namespace BoardView.Core.Graphics;

/// <summary>Base independiente de UI para toda primitiva gráfica importada.</summary>
public abstract class GraphicObject
{
    protected GraphicObject(string id, Bounds2D bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id.Trim();
        Bounds = bounds;
    }

    public string Id { get; }
    public Bounds2D Bounds { get; }
    public bool IsVisible { get; set; } = true;
    public string? LayerId { get; init; }
    public DocumentMetadata Metadata { get; } = new();
}
