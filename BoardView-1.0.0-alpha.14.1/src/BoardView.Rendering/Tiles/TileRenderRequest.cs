using System;
using System.Windows;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Contiene toda la información necesaria para solicitar
/// el renderizado de una única tesela.
///
/// La solicitud identifica la tesela, define la región exacta
/// que debe producirse y especifica el factor de zoom aplicado
/// sobre la página PDF.
///
/// Durante la migración del motor se mantiene temporalmente
/// el tamaño renderizado de la página para conservar compatibilidad
/// con los consumidores existentes.
/// </summary>
public readonly record struct TileRenderRequest
{
    /// <summary>
    /// Inicializa una solicitud mediante el nuevo contrato
    /// basado en factor de zoom.
    /// </summary>
    /// <param name="key">
    /// Identificador único de la tesela.
    /// </param>
    /// <param name="tileBounds">
    /// Región de la página, expresada en píxeles renderizados,
    /// que debe producir el motor.
    /// </param>
    /// <param name="zoomFactor">
    /// Factor de escala aplicado sobre la resolución base
    /// de la página PDF.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce cuando la región o el factor de zoom
    /// contienen valores inválidos.
    /// </exception>
    public TileRenderRequest(
        TileKey key,
        Int32Rect tileBounds,
        double zoomFactor)
    {
        ValidateTileBounds(tileBounds);
        ValidateZoomFactor(zoomFactor);

        Key = key;
        TileBounds = tileBounds;
        ZoomFactor = zoomFactor;

        /*
         * El tamaño completo de la página será calculado
         * internamente por PdfiumTileRenderer.
         *
         * Size.Empty permite distinguir las solicitudes creadas
         * mediante el nuevo contrato de las solicitudes antiguas.
         */
        PagePixelSize = Size.Empty;
    }

    /// <summary>
    /// Inicializa una solicitud mediante el contrato anterior,
    /// que proporciona manualmente el tamaño renderizado
    /// completo de la página.
    ///
    /// Este constructor se conserva temporalmente para mantener
    /// compilables los consumidores que todavía no han sido
    /// migrados al factor de zoom.
    /// </summary>
    /// <param name="key">
    /// Identificador único de la tesela.
    /// </param>
    /// <param name="tileBounds">
    /// Región de la página que debe producir el motor.
    /// </param>
    /// <param name="pagePixelSize">
    /// Tamaño completo de la página renderizada en píxeles.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce cuando alguna dimensión es inválida.
    /// </exception>
    public TileRenderRequest(
        TileKey key,
        Int32Rect tileBounds,
        Size pagePixelSize)
    {
        ValidateTileBounds(tileBounds);
        ValidatePagePixelSize(pagePixelSize);

        Key = key;
        TileBounds = tileBounds;
        PagePixelSize = pagePixelSize;

        /*
         * El contrato anterior no contiene el zoom original.
         * Se mantiene 1.0 como valor neutral durante la migración.
         *
         * PdfiumTileRenderer seguirá utilizando PagePixelSize
         * hasta que sea actualizado en la siguiente entrega.
         */
        ZoomFactor = 1D;
    }

    /// <summary>
    /// Identificador único de la tesela.
    /// </summary>
    public TileKey Key { get; }

    /// <summary>
    /// Región exacta de la página que debe producir
    /// el motor de renderizado.
    /// </summary>
    public Int32Rect TileBounds { get; }

    /// <summary>
    /// Factor de escala aplicado sobre la resolución base
    /// de la página PDF.
    /// </summary>
    public double ZoomFactor { get; }

    /// <summary>
    /// Tamaño completo de la página renderizada en píxeles.
    ///
    /// Cuando su valor es <see cref="Size.Empty"/>, el backend
    /// debe calcularlo a partir de <see cref="ZoomFactor"/>.
    ///
    /// Esta propiedad se mantiene temporalmente hasta finalizar
    /// la migración de todos los consumidores.
    /// </summary>
    public Size PagePixelSize { get; }

    /// <summary>
    /// Indica si la solicitud utiliza el contrato nuevo,
    /// basado en factor de zoom.
    /// </summary>
    public bool UsesAutomaticPageSize =>
        PagePixelSize.IsEmpty;

    /// <summary>
    /// Devuelve una representación legible para diagnóstico.
    /// </summary>
    public override string ToString()
    {
        string pageSizeText = UsesAutomaticPageSize
            ? "Automatic"
            : $"{PagePixelSize.Width:F0}x{PagePixelSize.Height:F0}";

        return
            $"[{Key}] " +
            $"Bounds=({TileBounds.X},{TileBounds.Y}," +
            $"{TileBounds.Width},{TileBounds.Height}) " +
            $"Zoom={ZoomFactor:F4} " +
            $"Page={pageSizeText}";
    }

    /// <summary>
    /// Comprueba que la región solicitada posea
    /// coordenadas y dimensiones válidas.
    /// </summary>
    private static void ValidateTileBounds(Int32Rect tileBounds)
    {
        if (tileBounds.X < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileBounds),
                tileBounds.X,
                "La coordenada X de la tesela no puede ser negativa.");
        }

        if (tileBounds.Y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileBounds),
                tileBounds.Y,
                "La coordenada Y de la tesela no puede ser negativa.");
        }

        if (tileBounds.Width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileBounds),
                tileBounds.Width,
                "El ancho de la tesela debe ser mayor que cero.");
        }

        if (tileBounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tileBounds),
                tileBounds.Height,
                "El alto de la tesela debe ser mayor que cero.");
        }
    }

    /// <summary>
    /// Comprueba que el factor de zoom sea finito
    /// y estrictamente mayor que cero.
    /// </summary>
    private static void ValidateZoomFactor(double zoomFactor)
    {
        if (!double.IsFinite(zoomFactor) || zoomFactor <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoomFactor),
                zoomFactor,
                "El factor de zoom debe ser un número finito mayor que cero.");
        }
    }

    /// <summary>
    /// Comprueba que el tamaño renderizado de la página
    /// posea dimensiones finitas y mayores que cero.
    /// </summary>
    private static void ValidatePagePixelSize(Size pagePixelSize)
    {
        if (pagePixelSize.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelSize),
                "El tamaño renderizado de la página no puede estar vacío.");
        }

        if (!double.IsFinite(pagePixelSize.Width) ||
            pagePixelSize.Width <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelSize),
                pagePixelSize.Width,
                "El ancho de la página debe ser un número finito mayor que cero.");
        }

        if (!double.IsFinite(pagePixelSize.Height) ||
            pagePixelSize.Height <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelSize),
                pagePixelSize.Height,
                "El alto de la página debe ser un número finito mayor que cero.");
        }
    }
}
