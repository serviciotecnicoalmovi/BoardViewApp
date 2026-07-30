using BoardView.Core.Geometry;

namespace BoardView.Core.Graphics;

/// <summary>Referencia a una imagen raster embebida o almacenada externamente.</summary>
public sealed class ImageGraphic : GraphicObject
{
    public ImageGraphic(string id, Bounds2D bounds, string resourceId, string mediaType)
        : base(id, bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ResourceId = resourceId.Trim();
        MediaType = mediaType.Trim();
    }

    public string ResourceId { get; }
    public string MediaType { get; }
}
