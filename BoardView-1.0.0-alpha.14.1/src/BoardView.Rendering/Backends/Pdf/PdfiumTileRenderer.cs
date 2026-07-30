using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BoardView.Formats.Pdf;
using BoardView.Rendering.Tiles;

namespace BoardView.Rendering.Backends.Pdfium;

/// <summary>
/// Implementa el renderizado de teselas PDF mediante PDFium.
/// </summary>
/// <remarks>
/// Esta clase actúa como adaptador entre:
///
/// <list type="bullet">
/// <item><see cref="ITileRenderer"/> y el motor de teselas.</item>
/// <item><see cref="PdfiumDocumentRenderer"/> y el backend PDFium.</item>
/// </list>
///
/// La instancia puede cambiar de documento mediante
/// <see cref="SetDocument"/> sin tener que reconstruir el resto
/// de la infraestructura de renderizado.
/// </remarks>
public sealed class PdfiumTileRenderer : ITileRenderer, IDisposable
{
    /*
     * Una unidad PDF equivale a 1/72 de pulgada.
     *
     * BoardView utiliza 96 píxeles por pulgada como resolución lógica
     * base, igual que el sistema de unidades independientes del
     * dispositivo utilizado habitualmente por WPF.
     */
    private const double PdfPointsPerInch = 72D;
    private const double BasePixelsPerInch = 96D;

    private readonly object _syncRoot = new();

    private PdfiumDocumentRenderer? _documentRenderer;
    private string? _documentPath;
    private bool _disposed;

    /// <summary>
    /// Obtiene la ruta absoluta del documento cargado actualmente.
    /// </summary>
    /// <remarks>
    /// Devuelve <see langword="null"/> cuando no existe un documento
    /// cargado.
    /// </remarks>
    public string? DocumentPath
    {
        get
        {
            lock (_syncRoot)
            {
                return _documentPath;
            }
        }
    }

    /// <summary>
    /// Obtiene un valor que indica si existe un documento cargado.
    /// </summary>
    public bool HasDocument
    {
        get
        {
            lock (_syncRoot)
            {
                return _documentRenderer is not null;
            }
        }
    }

    /// <summary>
    /// Obtiene la cantidad de páginas del documento cargado.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No existe ningún documento cargado.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// La instancia ya fue liberada.
    /// </exception>
    public int PageCount
    {
        get
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();

                PdfiumDocumentRenderer renderer =
                    GetRequiredDocumentRenderer();

                return renderer.PageCount;
            }
        }
    }

    /// <summary>
    /// Obtiene las dimensiones originales de una página en puntos PDF.
    /// </summary>
    /// <param name="pageIndex">
    /// Índice de página basado en cero.
    /// </param>
    /// <returns>
    /// Dimensiones originales de la página expresadas en puntos PDF.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// No existe ningún documento cargado.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// El índice de página no pertenece al documento.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// La instancia ya fue liberada.
    /// </exception>
    public PdfiumPageSize GetPageSize(int pageIndex)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();

            PdfiumDocumentRenderer renderer =
                GetRequiredDocumentRenderer();

            return renderer.GetPageSize(pageIndex);
        }
    }

    /// <summary>
    /// Calcula el tamaño renderizado de una página para un nivel de zoom.
    /// </summary>
    /// <param name="pageIndex">
    /// Índice de página basado en cero.
    /// </param>
    /// <param name="zoomFactor">
    /// Factor de ampliación solicitado.
    ///
    /// Un valor de <c>1.0</c> representa el tamaño lógico de la página
    /// calculado a 96 píxeles por pulgada.
    /// </param>
    /// <returns>
    /// Tamaño total de la página renderizada expresado en píxeles.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// El factor de zoom no es positivo, no es finito o genera unas
    /// dimensiones que exceden el rango de <see cref="int"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No existe ningún documento cargado.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// La instancia ya fue liberada.
    /// </exception>
    public PdfiumRenderedPageSize GetRenderedPageSize(
        int pageIndex,
        double zoomFactor)
    {
        ValidateZoomFactor(zoomFactor);

        lock (_syncRoot)
        {
            ThrowIfDisposed();

            PdfiumDocumentRenderer renderer =
                GetRequiredDocumentRenderer();

            return CalculateRenderedPageSize(
                renderer,
                pageIndex,
                zoomFactor);
        }
    }

    /// <summary>
    /// Carga un documento PDF y libera el documento anterior.
    /// </summary>
    /// <param name="filePath">
    /// Ruta absoluta o relativa del nuevo documento.
    /// </param>
    /// <remarks>
    /// El documento nuevo se abre antes de reemplazar el documento
    /// anterior. Si la apertura falla, el documento actualmente
    /// cargado permanece disponible.
    ///
    /// Cuando se cambia de documento, la caché externa debe descartar
    /// las teselas pertenecientes al documento anterior.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// La ruta está vacía.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// El archivo no existe.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// La instancia ya fue liberada.
    /// </exception>
    public void SetDocument(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "La ruta del documento PDF no puede estar vacía.",
                nameof(filePath));
        }

        string absolutePath = Path.GetFullPath(filePath);

        /*
         * Comprobación rápida para evitar abrir nuevamente
         * el mismo documento.
         */
        lock (_syncRoot)
        {
            ThrowIfDisposed();

            if (string.Equals(
                    _documentPath,
                    absolutePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        /*
         * El documento nuevo se abre fuera del bloqueo.
         *
         * De esta manera, si PDFium tarda en abrirlo, no se bloquean
         * innecesariamente las consultas simples al estado actual.
         */
        PdfiumDocumentRenderer? newRenderer =
            new PdfiumDocumentRenderer(absolutePath);

        PdfiumDocumentRenderer? previousRenderer = null;

        try
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();

                /*
                 * Otra llamada concurrente podría haber cargado el mismo
                 * documento mientras se abría newRenderer.
                 */
                if (string.Equals(
                        _documentPath,
                        absolutePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                previousRenderer = _documentRenderer;

                _documentRenderer = newRenderer;
                _documentPath = absolutePath;

                /*
                 * La propiedad fue transferida a esta instancia.
                 * Evita que el bloque finally destruya el renderizador
                 * recién instalado.
                 */
                newRenderer = null;
            }
        }
        finally
        {
            /*
             * Si no se transfirió la propiedad, la apertura quedó
             * descartada o la instancia fue liberada concurrentemente.
             */
            newRenderer?.Dispose();
        }

        /*
         * El documento anterior se libera después de completar
         * el intercambio.
         */
        previousRenderer?.Dispose();
    }

    /// <summary>
    /// Libera el documento cargado sin destruir esta instancia.
    /// </summary>
    /// <remarks>
    /// La instancia podrá recibir posteriormente otro documento
    /// mediante <see cref="SetDocument"/>.
    /// </remarks>
    public void ClearDocument()
    {
        PdfiumDocumentRenderer? rendererToDispose;

        lock (_syncRoot)
        {
            ThrowIfDisposed();

            rendererToDispose = _documentRenderer;

            _documentRenderer = null;
            _documentPath = null;
        }

        rendererToDispose?.Dispose();
    }

    /// <inheritdoc />
    public Task<Tile> RenderTileAsync(
        TileRenderRequest request,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedThreadSafe();

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * PdfiumDocumentRenderer realiza una operación síncrona.
         *
         * Task.Run evita bloquear el hilo de la interfaz mientras
         * PDFium produce los píxeles de la tesela.
         */
        return Task.Run(
            () => RenderTile(
                request,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Libera permanentemente el renderizador y su documento.
    /// </summary>
    public void Dispose()
    {
        PdfiumDocumentRenderer? rendererToDispose;

        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            rendererToDispose = _documentRenderer;

            _documentRenderer = null;
            _documentPath = null;
        }

        rendererToDispose?.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Produce una tesela utilizando el contrato automático
    /// o el contrato temporal de compatibilidad.
    /// </summary>
    private Tile RenderTile(
        TileRenderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PdfiumRenderResult result;

        /*
         * El bloqueo impide que SetDocument, ClearDocument o Dispose
         * cierren el documento mientras PDFium está renderizando.
         *
         * El tamaño completo de la página también se resuelve dentro
         * del mismo bloqueo para garantizar que sus dimensiones
         * correspondan al documento que será renderizado.
         */
        lock (_syncRoot)
        {
            ThrowIfDisposed();

            PdfiumDocumentRenderer renderer =
                GetRequiredDocumentRenderer();

            PdfiumRenderedPageSize pagePixelSize =
                ResolvePagePixelSize(
                    renderer,
                    request);

            cancellationToken.ThrowIfCancellationRequested();

            result = renderer.RenderRegion(
                pageIndex: request.Key.Page,
                pagePixelWidth: pagePixelSize.Width,
                pagePixelHeight: pagePixelSize.Height,
                regionX: request.TileBounds.X,
                regionY: request.TileBounds.Y,
                regionWidth: request.TileBounds.Width,
                regionHeight: request.TileBounds.Height,
                cancellationToken: cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new Tile(
            request.Key,
            result.PixelWidth,
            result.PixelHeight,
            result.Stride,
            result.PixelData);
    }

    /// <summary>
    /// Determina el tamaño renderizado completo de la página.
    /// </summary>
    /// <remarks>
    /// Las solicitudes nuevas proporcionan un factor de zoom y delegan
    /// el cálculo de las dimensiones al backend.
    ///
    /// Las solicitudes antiguas proporcionan directamente el tamaño
    /// renderizado de la página y se mantienen temporalmente para
    /// conservar la compatibilidad durante la migración.
    /// </remarks>
    private static PdfiumRenderedPageSize ResolvePagePixelSize(
        PdfiumDocumentRenderer renderer,
        TileRenderRequest request)
    {
        if (request.UsesAutomaticPageSize)
        {
            return CalculateRenderedPageSize(
                renderer,
                request.Key.Page,
                request.ZoomFactor);
        }

        int pagePixelWidth =
            ConvertPageDimensionToInt32(
                request.PagePixelSize.Width,
                nameof(request.PagePixelSize));

        int pagePixelHeight =
            ConvertPageDimensionToInt32(
                request.PagePixelSize.Height,
                nameof(request.PagePixelSize));

        return new PdfiumRenderedPageSize(
            pagePixelWidth,
            pagePixelHeight);
    }

    /// <summary>
    /// Convierte las dimensiones físicas de una página PDF
    /// en dimensiones renderizadas expresadas en píxeles.
    /// </summary>
    private static PdfiumRenderedPageSize CalculateRenderedPageSize(
        PdfiumDocumentRenderer renderer,
        int pageIndex,
        double zoomFactor)
    {
        ValidateZoomFactor(zoomFactor);

        PdfiumPageSize pageSize =
            renderer.GetPageSize(pageIndex);

        /*
         * PDFium devuelve las dimensiones físicas en puntos PDF.
         *
         * Conversión:
         *
         * píxeles = puntos × 96 / 72 × zoom
         */
        double pixelScale =
            BasePixelsPerInch /
            PdfPointsPerInch *
            zoomFactor;

        int pixelWidth =
            ConvertRenderedDimensionToInt32(
                pageSize.Width,
                pixelScale,
                nameof(pageSize.Width));

        int pixelHeight =
            ConvertRenderedDimensionToInt32(
                pageSize.Height,
                pixelScale,
                nameof(pageSize.Height));

        return new PdfiumRenderedPageSize(
            pixelWidth,
            pixelHeight);
    }

    /// <summary>
    /// Obtiene el renderizador de documento activo.
    /// </summary>
    private PdfiumDocumentRenderer GetRequiredDocumentRenderer()
    {
        return _documentRenderer ??
            throw new InvalidOperationException(
                "No existe ningún documento PDF cargado. " +
                "Ejecuta SetDocument antes de solicitar una tesela.");
    }

    /// <summary>
    /// Valida el factor de zoom utilizado para calcular
    /// las dimensiones renderizadas de una página.
    /// </summary>
    private static void ValidateZoomFactor(double zoomFactor)
    {
        if (!double.IsFinite(zoomFactor) ||
            zoomFactor <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoomFactor),
                zoomFactor,
                "El factor de zoom debe ser un número finito y positivo.");
        }
    }

    /// <summary>
    /// Convierte una dimensión física PDF en píxeles enteros.
    /// </summary>
    private static int ConvertRenderedDimensionToInt32(
        double pagePointDimension,
        double pixelScale,
        string parameterName)
    {
        if (!double.IsFinite(pagePointDimension) ||
            pagePointDimension <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                pagePointDimension,
                "La dimensión original de la página no es válida.");
        }

        double pixelDimension =
            pagePointDimension * pixelScale;

        if (!double.IsFinite(pixelDimension) ||
            pixelDimension <= 0D ||
            pixelDimension > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                pixelDimension,
                "La dimensión renderizada queda fuera del rango permitido.");
        }

        /*
         * Se utiliza Ceiling para asegurar que la página renderizada
         * nunca pierda una fracción de píxel en los bordes derecho
         * o inferior.
         */
        double roundedDimension =
            Math.Ceiling(pixelDimension);

        if (roundedDimension <= 0D ||
            roundedDimension > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                roundedDimension,
                "La dimensión redondeada queda fuera del rango permitido.");
        }

        return checked((int)roundedDimension);
    }

    /// <summary>
    /// Convierte una dimensión proporcionada por el contrato temporal
    /// de compatibilidad en un entero válido para PDFium.
    /// </summary>
    private static int ConvertPageDimensionToInt32(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value <= 0D ||
            value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "La dimensión de la página no es válida.");
        }

        /*
         * Los consumidores actuales construyen PagePixelSize
         * normalmente a partir de enteros.
         *
         * Math.Round protege el backend ante pequeñas imprecisiones
         * introducidas por representaciones de punto flotante.
         */
        double roundedValue = Math.Round(
            value,
            MidpointRounding.AwayFromZero);

        if (roundedValue <= 0D ||
            roundedValue > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "La dimensión redondeada queda fuera del rango permitido.");
        }

        return checked((int)roundedValue);
    }

    /// <summary>
    /// Comprueba de forma sincronizada que la instancia
    /// todavía se encuentre disponible.
    /// </summary>
    private void ThrowIfDisposedThreadSafe()
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
        }
    }

    /// <summary>
    /// Lanza una excepción cuando la instancia ya fue liberada.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

/// <summary>
/// Representa las dimensiones renderizadas de una página PDF.
/// </summary>
/// <param name="Width">
/// Ancho total de la página en píxeles.
/// </param>
/// <param name="Height">
/// Alto total de la página en píxeles.
/// </param>
public readonly record struct PdfiumRenderedPageSize(
    int Width,
    int Height)
{
    /// <summary>
    /// Obtiene la cantidad total de píxeles de la página.
    /// </summary>
    public long PixelCount =>
        checked((long)Width * Height);
}
