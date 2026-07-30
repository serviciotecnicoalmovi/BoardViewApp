using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Imagen raster referenciada desde un documento importado.</summary>
public sealed class RasterImageElement : BoardElement
{
    /// <summary>Inicializa una referencia de imagen.</summary>
    public RasterImageElement(
        string id,
        string layerId,
        Bounds2D bounds,
        string resourceId,
        string mediaType)
        : base(id, layerId, bounds)
    {
        if (bounds.IsEmpty)
        {
            throw new ArgumentException("La imagen debe tener límites positivos.", nameof(bounds));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ResourceId = resourceId.Trim();
        MediaType = mediaType.Trim();
    }

    public string ResourceId { get; }
    public string MediaType { get; }
}
