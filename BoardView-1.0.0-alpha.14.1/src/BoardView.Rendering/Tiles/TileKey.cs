using System;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Identifica de forma única una tesela renderizada.
///
/// Una tesela queda definida por:
/// - el documento al que pertenece;
/// - la página PDF;
/// - el nivel de renderizado;
/// - la columna horizontal;
/// - la fila vertical.
///
/// Esta estructura es inmutable y puede utilizarse como clave
/// de diccionarios y cachés de renderizado.
/// </summary>
public readonly record struct TileKey(
    Guid DocumentId,
    int Page,
    int ZoomLevel,
    int TileX,
    int TileY)
{
    /// <summary>
    /// Inicializa una clave sin identificador de documento.
    ///
    /// Este constructor mantiene temporalmente la compatibilidad
    /// con los consumidores existentes mientras se actualizan para
    /// proporcionar un DocumentId real.
    /// </summary>
    public TileKey(
        int page,
        int zoomLevel,
        int tileX,
        int tileY)
        : this(
            Guid.Empty,
            page,
            zoomLevel,
            tileX,
            tileY)
    {
    }

    /// <summary>
    /// Indica si la clave contiene un identificador de documento válido.
    /// </summary>
    public bool HasDocumentId =>
        DocumentId != Guid.Empty;

    /// <summary>
    /// Devuelve una representación legible para diagnóstico.
    /// </summary>
    public override string ToString()
    {
        string documentText = HasDocumentId
            ? DocumentId.ToString("D")
            : "Unassigned";

        return
            $"Document={documentText} " +
            $"Page={Page} " +
            $"Zoom={ZoomLevel} " +
            $"X={TileX} " +
            $"Y={TileY}";
    }
}
