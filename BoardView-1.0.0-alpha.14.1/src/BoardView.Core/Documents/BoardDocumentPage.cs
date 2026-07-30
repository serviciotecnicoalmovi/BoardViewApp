using BoardView.Core.Geometry;

namespace BoardView.Core.Documents;

/// <summary>
/// Describe una página o superficie de origen dentro del espacio normalizado del documento.
/// Permite conservar documentos multipágina sin superponer sus coordenadas.
/// </summary>
public sealed class BoardDocumentPage
{
    /// <summary>Inicializa una página normalizada.</summary>
    public BoardDocumentPage(
        int number,
        double width,
        double height,
        Point2D offset,
        IEnumerable<string> layerIds)
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

        ArgumentNullException.ThrowIfNull(layerIds);
        string[] normalizedLayerIds = layerIds
            .Select(static id =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(id);
                return id.Trim();
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedLayerIds.Length == 0)
        {
            throw new ArgumentException("La página debe estar asociada al menos a una capa.", nameof(layerIds));
        }

        Number = number;
        Width = width;
        Height = height;
        Offset = offset;
        LayerIds = Array.AsReadOnly(normalizedLayerIds);
    }

    /// <summary>Número de página comenzando en uno.</summary>
    public int Number { get; }

    /// <summary>Ancho físico normalizado en milímetros.</summary>
    public double Width { get; }

    /// <summary>Alto físico normalizado en milímetros.</summary>
    public double Height { get; }

    /// <summary>Desplazamiento de la página dentro del espacio global del documento.</summary>
    public Point2D Offset { get; }

    /// <summary>Capas pertenecientes a esta página.</summary>
    public IReadOnlyList<string> LayerIds { get; }

    /// <summary>Límites de la página dentro del espacio global.</summary>
    public Bounds2D Bounds => new(Offset.X, Offset.Y, Offset.X + Width, Offset.Y + Height);
}
