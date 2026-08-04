using BoardView.Core.Pdf;
using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Convierte las palabras extraídas por PDFium en observaciones textuales
/// expresadas en coordenadas de píxeles del render original.
/// </summary>
/// <remarks>
/// <see cref="PdfWord"/> utiliza puntos PDF con origen en la esquina inferior
/// izquierda. El pipeline geométrico utiliza píxeles con origen en la esquina
/// superior izquierda.
///
/// Esta clase realiza exclusivamente esa transformación de coordenadas. No
/// ejecuta OCR, no clasifica referencias y no modifica el texto extraído.
/// </remarks>
public sealed class BoardPdfTextObservationFactory
{
    /// <summary>
    /// Confianza asignada por defecto al texto nativo extraído del PDF.
    /// </summary>
    public const double DefaultNativeTextConfidence = 1D;

    /// <summary>
    /// Genera observaciones para una página PDF completa.
    /// </summary>
    /// <param name="page">
    /// Página técnica que contiene palabras y coordenadas en puntos PDF.
    /// </param>
    /// <param name="renderPixelWidth">
    /// Ancho del render original en píxeles.
    /// </param>
    /// <param name="renderPixelHeight">
    /// Alto del render original en píxeles.
    /// </param>
    /// <param name="pageIndex">
    /// Índice cero-basado de la página.
    /// </param>
    public IReadOnlyList<BoardTextObservation> Create(
        PdfPage page,
        int renderPixelWidth,
        int renderPixelHeight,
        int pageIndex)
    {
        return Create(
            page,
            renderPixelWidth,
            renderPixelHeight,
            pageIndex,
            BoardPdfTextObservationOptions.Default,
            CancellationToken.None);
    }

    /// <summary>
    /// Genera observaciones utilizando opciones configurables.
    /// </summary>
    public IReadOnlyList<BoardTextObservation> Create(
        PdfPage page,
        int renderPixelWidth,
        int renderPixelHeight,
        int pageIndex,
        BoardPdfTextObservationOptions options)
    {
        return Create(
            page,
            renderPixelWidth,
            renderPixelHeight,
            pageIndex,
            options,
            CancellationToken.None);
    }

    /// <summary>
    /// Genera observaciones textuales y transforma los límites desde puntos PDF
    /// a píxeles del render.
    /// </summary>
    public IReadOnlyList<BoardTextObservation> Create(
        PdfPage page,
        int renderPixelWidth,
        int renderPixelHeight,
        int pageIndex,
        BoardPdfTextObservationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            page);

        ArgumentNullException.ThrowIfNull(
            options);

        options.Validate();

        ValidatePageAndRender(
            page,
            renderPixelWidth,
            renderPixelHeight,
            pageIndex);

        cancellationToken.ThrowIfCancellationRequested();

        double scaleX =
            renderPixelWidth /
            page.WidthPoints;

        double scaleY =
            renderPixelHeight /
            page.HeightPoints;

        var observations =
            new List<BoardTextObservation>(
                page.Words.Count);

        int observationId = 0;

        foreach (PdfWord word in page.Words)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CanConvert(
                    word,
                    options))
            {
                continue;
            }

            BoardGeometryBounds? bounds =
                ConvertBounds(
                    word,
                    page,
                    renderPixelWidth,
                    renderPixelHeight,
                    scaleX,
                    scaleY,
                    options);

            if (bounds is null)
            {
                continue;
            }

            observations.Add(
                new BoardTextObservation(
                    observationId++,
                    word.Text,
                    bounds.Value,
                    options.NativeTextConfidence,
                    pageIndex,
                    rotationDegrees: 0D,
                    sourceId:
                        $"pdfium:p{pageIndex}:w{observationId - 1}"));
        }

        return observations;
    }

    /// <summary>
    /// Convierte los límites de una palabra individual.
    /// </summary>
    private static BoardGeometryBounds? ConvertBounds(
        PdfWord word,
        PdfPage page,
        int renderPixelWidth,
        int renderPixelHeight,
        double scaleX,
        double scaleY,
        BoardPdfTextObservationOptions options)
    {
        double pdfLeft =
            word.Left;

        double pdfRight =
            word.Left +
            word.Width;

        double pdfBottom =
            word.Bottom;

        double pdfTop =
            word.Bottom +
            word.Height;

        // PDF: origen inferior izquierdo.
        // Render: origen superior izquierdo.
        double pixelLeft =
            pdfLeft *
            scaleX;

        double pixelRight =
            pdfRight *
            scaleX;

        double pixelTop =
            (page.HeightPoints - pdfTop) *
            scaleY;

        double pixelBottom =
            (page.HeightPoints - pdfBottom) *
            scaleY;

        int left =
            Clamp(
                FloorToInt(pixelLeft) -
                options.BoundsPaddingPixels,
                0,
                renderPixelWidth);

        int top =
            Clamp(
                FloorToInt(pixelTop) -
                options.BoundsPaddingPixels,
                0,
                renderPixelHeight);

        int right =
            Clamp(
                CeilingToInt(pixelRight) +
                options.BoundsPaddingPixels,
                0,
                renderPixelWidth);

        int bottom =
            Clamp(
                CeilingToInt(pixelBottom) +
                options.BoundsPaddingPixels,
                0,
                renderPixelHeight);

        int width =
            right -
            left;

        int height =
            bottom -
            top;

        if (width < options.MinimumPixelWidth ||
            height < options.MinimumPixelHeight)
        {
            if (!options.ExpandSmallBounds)
            {
                return null;
            }

            ExpandToMinimumSize(
                ref left,
                ref top,
                ref width,
                ref height,
                renderPixelWidth,
                renderPixelHeight,
                options.MinimumPixelWidth,
                options.MinimumPixelHeight);
        }

        if (width <= 0 ||
            height <= 0)
        {
            return null;
        }

        return new BoardGeometryBounds(
            left,
            top,
            width,
            height);
    }

    /// <summary>
    /// Amplía límites demasiado pequeños sin salir del render.
    /// </summary>
    private static void ExpandToMinimumSize(
        ref int left,
        ref int top,
        ref int width,
        ref int height,
        int renderPixelWidth,
        int renderPixelHeight,
        int minimumWidth,
        int minimumHeight)
    {
        if (width < minimumWidth)
        {
            int missing =
                minimumWidth -
                width;

            left =
                Math.Max(
                    0,
                    left -
                    (missing / 2));

            width =
                Math.Min(
                    minimumWidth,
                    renderPixelWidth -
                    left);
        }

        if (height < minimumHeight)
        {
            int missing =
                minimumHeight -
                height;

            top =
                Math.Max(
                    0,
                    top -
                    (missing / 2));

            height =
                Math.Min(
                    minimumHeight,
                    renderPixelHeight -
                    top);
        }
    }

    /// <summary>
    /// Determina si una palabra posee datos suficientes para convertirse.
    /// </summary>
    private static bool CanConvert(
        PdfWord word,
        BoardPdfTextObservationOptions options)
    {
        if (string.IsNullOrWhiteSpace(
                word.Text))
        {
            return false;
        }

        string text =
            word.Text.Trim();

        if (text.Length <
                options.MinimumTextLength ||
            text.Length >
                options.MaximumTextLength)
        {
            return false;
        }

        if (!double.IsFinite(word.Left) ||
            !double.IsFinite(word.Bottom) ||
            !double.IsFinite(word.Width) ||
            !double.IsFinite(word.Height))
        {
            return false;
        }

        return word.Width > 0D &&
               word.Height > 0D;
    }

    private static void ValidatePageAndRender(
        PdfPage page,
        int renderPixelWidth,
        int renderPixelHeight,
        int pageIndex)
    {
        if (!double.IsFinite(page.WidthPoints) ||
            page.WidthPoints <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "El ancho de la página PDF debe ser finito y mayor que cero.");
        }

        if (!double.IsFinite(page.HeightPoints) ||
            page.HeightPoints <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(page),
                "El alto de la página PDF debe ser finito y mayor que cero.");
        }

        if (renderPixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderPixelWidth),
                renderPixelWidth,
                "El ancho del render debe ser mayor que cero.");
        }

        if (renderPixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderPixelHeight),
                renderPixelHeight,
                "El alto del render debe ser mayor que cero.");
        }

        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                "El índice de página no puede ser negativo.");
        }
    }

    private static int FloorToInt(
        double value)
    {
        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Floor(
            value);
    }

    private static int CeilingToInt(
        double value)
    {
        if (value <= int.MinValue)
        {
            return int.MinValue;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Ceiling(
            value);
    }

    private static int Clamp(
        int value,
        int minimum,
        int maximum)
    {
        return Math.Max(
            minimum,
            Math.Min(
                maximum,
                value));
    }
}

/// <summary>
/// Configuración de la conversión de palabras PDF a observaciones del render.
/// </summary>
public sealed record BoardPdfTextObservationOptions
{
    /// <summary>
    /// Configuración predeterminada.
    /// </summary>
    public static BoardPdfTextObservationOptions Default { get; } =
        new();

    /// <summary>
    /// Confianza asignada al texto nativo de PDFium.
    /// </summary>
    public double NativeTextConfidence { get; init; } =
        BoardPdfTextObservationFactory.DefaultNativeTextConfidence;

    /// <summary>
    /// Longitud mínima del texto conservado.
    /// </summary>
    public int MinimumTextLength { get; init; } =
        1;

    /// <summary>
    /// Longitud máxima del texto conservado.
    /// </summary>
    public int MaximumTextLength { get; init; } =
        128;

    /// <summary>
    /// Ancho mínimo de la observación en píxeles.
    /// </summary>
    public int MinimumPixelWidth { get; init; } =
        1;

    /// <summary>
    /// Alto mínimo de la observación en píxeles.
    /// </summary>
    public int MinimumPixelHeight { get; init; } =
        1;

    /// <summary>
    /// Margen adicional alrededor de cada palabra.
    /// </summary>
    public int BoundsPaddingPixels { get; init; } =
        0;

    /// <summary>
    /// Amplía los límites menores que el tamaño mínimo.
    /// </summary>
    public bool ExpandSmallBounds { get; init; } =
        true;

    /// <summary>
    /// Valida las opciones.
    /// </summary>
    public void Validate()
    {
        if (!double.IsFinite(
                NativeTextConfidence) ||
            NativeTextConfidence < 0D ||
            NativeTextConfidence > 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(NativeTextConfidence),
                NativeTextConfidence,
                "La confianza debe estar entre cero y uno.");
        }

        if (MinimumTextLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumTextLength));
        }

        if (MaximumTextLength <
            MinimumTextLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumTextLength));
        }

        if (MinimumPixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumPixelWidth));
        }

        if (MinimumPixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinimumPixelHeight));
        }

        if (BoundsPaddingPixels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BoundsPaddingPixels));
        }
    }
}
