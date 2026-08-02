using System;
using System.Threading;
using System.Threading.Tasks;
using BoardView.Formats.Pdf;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Coordina el renderizado completo de una página PDF y su posterior
/// análisis geométrico.
/// </summary>
/// <remarks>
/// La clase no contiene dependencias de WPF ni lógica de interfaz.
///
/// Flujo ejecutado:
///
/// <list type="number">
/// <item>Renderiza la página completa mediante PDFium.</item>
/// <item>Genera una máscara binaria de geometría.</item>
/// <item>Calcula el rectángulo útil.</item>
/// <item>Recorta la imagen original sin modificar sus píxeles.</item>
/// </list>
///
/// El renderizador PDF puede ser proporcionado externamente o creado
/// internamente desde una ruta de archivo.
/// </remarks>
public sealed class GeometryRenderPipeline : IDisposable
{
    private const double PdfPointsPerInch = 72D;
    private const double BasePixelsPerInch = 96D;

    private readonly PdfiumDocumentRenderer _documentRenderer;
    private readonly bool _ownsDocumentRenderer;
    private readonly BoardGeometryCropper _cropper;

    private bool _disposed;

    /// <summary>
    /// Inicializa el pipeline abriendo el documento PDF indicado.
    /// </summary>
    /// <param name="filePath">
    /// Ruta absoluta o relativa del documento PDF.
    /// </param>
    public GeometryRenderPipeline(string filePath)
        : this(
            new PdfiumDocumentRenderer(filePath),
            ownsDocumentRenderer: true)
    {
    }

    /// <summary>
    /// Inicializa el pipeline utilizando un renderizador PDF existente.
    /// </summary>
    /// <param name="documentRenderer">
    /// Renderizador que contiene el documento abierto.
    /// </param>
    /// <param name="ownsDocumentRenderer">
    /// Indica si este pipeline debe liberar el renderizador al ejecutar
    /// <see cref="Dispose"/>.
    /// </param>
    public GeometryRenderPipeline(
        PdfiumDocumentRenderer documentRenderer,
        bool ownsDocumentRenderer = false)
    {
        ArgumentNullException.ThrowIfNull(documentRenderer);

        _documentRenderer = documentRenderer;
        _ownsDocumentRenderer = ownsDocumentRenderer;
        _cropper = new BoardGeometryCropper();
    }

    /// <summary>
    /// Obtiene la cantidad de páginas del documento asociado.
    /// </summary>
    public int PageCount
    {
        get
        {
            ThrowIfDisposed();
            return _documentRenderer.PageCount;
        }
    }

    /// <summary>
    /// Renderiza una página completa utilizando un factor de zoom.
    /// </summary>
    /// <param name="pageIndex">
    /// Índice de página basado en cero.
    /// </param>
    /// <param name="zoomFactor">
    /// Factor de zoom. Un valor de <c>1.0</c> representa 96 píxeles
    /// por pulgada.
    /// </param>
    /// <param name="cancellationToken">
    /// Permite cancelar la operación.
    /// </param>
    /// <returns>
    /// Resultado BGRA32 de la página completa.
    /// </returns>
    public Task<GeometryPageRenderResult> RenderOriginalAsync(
        int pageIndex,
        double zoomFactor,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateZoomFactor(zoomFactor);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => RenderOriginal(
                pageIndex,
                zoomFactor,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Renderiza una página completa y ejecuta el análisis geométrico.
    /// </summary>
    /// <param name="pageIndex">
    /// Índice de página basado en cero.
    /// </param>
    /// <param name="zoomFactor">
    /// Factor de zoom utilizado para producir la imagen.
    /// </param>
    /// <param name="options">
    /// Opciones de clasificación geométrica.
    /// </param>
    /// <param name="cancellationToken">
    /// Permite cancelar la operación.
    /// </param>
    /// <returns>
    /// Página original, máscara, análisis y recorte resultante.
    /// </returns>
    public Task<GeometryRenderResult> RenderGeometryAsync(
        int pageIndex,
        double zoomFactor,
        GeometryRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateZoomFactor(zoomFactor);

        GeometryRenderOptions effectiveOptions =
            options ?? GeometryRenderOptions.Default;

        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => RenderGeometry(
                pageIndex,
                zoomFactor,
                effectiveOptions,
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Libera los recursos propiedad del pipeline.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsDocumentRenderer)
        {
            _documentRenderer.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Ejecuta el renderizado síncrono de la página completa.
    /// </summary>
    private GeometryPageRenderResult RenderOriginal(
        int pageIndex,
        double zoomFactor,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        ValidatePageIndex(pageIndex);

        PdfiumPageSize pageSize =
            _documentRenderer.GetPageSize(pageIndex);

        GeometryPagePixelSize pixelSize =
            CalculateRenderedPageSize(
                pageSize,
                zoomFactor);

        cancellationToken.ThrowIfCancellationRequested();

        PdfiumRenderResult renderResult =
            _documentRenderer.RenderRegion(
                pageIndex: pageIndex,
                pagePixelWidth: pixelSize.Width,
                pagePixelHeight: pixelSize.Height,
                regionX: 0,
                regionY: 0,
                regionWidth: pixelSize.Width,
                regionHeight: pixelSize.Height,
                cancellationToken: cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return new GeometryPageRenderResult(
            pageIndex,
            zoomFactor,
            pageSize,
            renderResult);
    }

    /// <summary>
    /// Ejecuta el flujo geométrico completo de forma síncrona.
    /// </summary>
    private GeometryRenderResult RenderGeometry(
        int pageIndex,
        double zoomFactor,
        GeometryRenderOptions options,
        CancellationToken cancellationToken)
    {
        GeometryPageRenderResult original =
            RenderOriginal(
                pageIndex,
                zoomFactor,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        PdfiumRenderResult image =
            original.Image;

        BoardGeometryMask mask =
            BoardGeometryMask.CreateFromBgra32(
                image.PixelData,
                image.PixelWidth,
                image.PixelHeight,
                image.Stride,
                options.DarkChannelThreshold,
                options.MinimumAlpha);

        cancellationToken.ThrowIfCancellationRequested();

        var analyzer = new BoardGeometryAnalyzer(
            options.DarkChannelThreshold,
            options.MinimumAlpha);

        BoardGeometryAnalysisResult analysis =
            analyzer.Analyze(
                image.PixelData,
                image.PixelWidth,
                image.PixelHeight,
                image.Stride);

        cancellationToken.ThrowIfCancellationRequested();

        BoardGeometryCropResult? cropResult = null;

        if (analysis.HasGeometry)
        {
            cropResult =
                _cropper.Crop(
                    image.PixelData,
                    image.PixelWidth,
                    image.PixelHeight,
                    image.Stride,
                    analysis.Bounds);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new GeometryRenderResult(
            original,
            mask,
            analysis,
            cropResult);
    }

    /// <summary>
    /// Convierte las dimensiones físicas PDF en píxeles.
    /// </summary>
    private static GeometryPagePixelSize CalculateRenderedPageSize(
        PdfiumPageSize pageSize,
        double zoomFactor)
    {
        double pixelScale =
            BasePixelsPerInch /
            PdfPointsPerInch *
            zoomFactor;

        int width =
            ConvertRenderedDimension(
                pageSize.Width,
                pixelScale,
                nameof(pageSize.Width));

        int height =
            ConvertRenderedDimension(
                pageSize.Height,
                pixelScale,
                nameof(pageSize.Height));

        return new GeometryPagePixelSize(
            width,
            height);
    }

    /// <summary>
    /// Convierte una dimensión física en una dimensión entera de píxeles.
    /// </summary>
    private static int ConvertRenderedDimension(
        double pointDimension,
        double pixelScale,
        string parameterName)
    {
        if (!double.IsFinite(pointDimension) ||
            pointDimension <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                pointDimension,
                "La dimensión física de la página no es válida.");
        }

        double pixelDimension =
            pointDimension *
            pixelScale;

        if (!double.IsFinite(pixelDimension) ||
            pixelDimension <= 0D ||
            pixelDimension > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                pixelDimension,
                "La dimensión renderizada queda fuera del rango permitido.");
        }

        return checked(
            (int)Math.Ceiling(pixelDimension));
    }

    /// <summary>
    /// Valida el factor de zoom.
    /// </summary>
    private static void ValidateZoomFactor(double zoomFactor)
    {
        if (!double.IsFinite(zoomFactor) ||
            zoomFactor <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zoomFactor),
                zoomFactor,
                "El factor de zoom debe ser finito y positivo.");
        }
    }

    /// <summary>
    /// Valida el índice de página.
    /// </summary>
    private void ValidatePageIndex(int pageIndex)
    {
        if (pageIndex < 0 ||
            pageIndex >= _documentRenderer.PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"El índice debe estar entre 0 y {_documentRenderer.PageCount - 1}.");
        }
    }

    /// <summary>
    /// Impide utilizar el pipeline después de liberarlo.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

/// <summary>
/// Opciones utilizadas durante la clasificación geométrica.
/// </summary>
public sealed record GeometryRenderOptions
{
    /// <summary>
    /// Opciones predeterminadas.
    /// </summary>
    public static GeometryRenderOptions Default { get; } =
        new();

    /// <summary>
    /// Valor máximo permitido para el canal RGB más oscuro.
    /// </summary>
    public byte DarkChannelThreshold { get; init; } =
        BoardGeometryAnalyzer.DefaultDarkChannelThreshold;

    /// <summary>
    /// Canal alfa mínimo considerado visible.
    /// </summary>
    public byte MinimumAlpha { get; init; } =
        BoardGeometryAnalyzer.DefaultMinimumAlpha;
}

/// <summary>
/// Dimensiones renderizadas de una página expresadas en píxeles.
/// </summary>
public readonly record struct GeometryPagePixelSize(
    int Width,
    int Height);

/// <summary>
/// Página completa renderizada mediante PDFium.
/// </summary>
public sealed record GeometryPageRenderResult(
    int PageIndex,
    double ZoomFactor,
    PdfiumPageSize PageSize,
    PdfiumRenderResult Image);

/// <summary>
/// Resultado completo del pipeline geométrico.
/// </summary>
public sealed record GeometryRenderResult(
    GeometryPageRenderResult Original,
    BoardGeometryMask Mask,
    BoardGeometryAnalysisResult Analysis,
    BoardGeometryCropResult? CropResult)
{
    /// <summary>
    /// Indica si el pipeline detectó y recortó geometría.
    /// </summary>
    public bool HasGeometry =>
        Analysis.HasGeometry &&
        CropResult is not null;
}
