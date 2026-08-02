using System;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Analiza una imagen BGRA32 renderizada y calcula el rectángulo mínimo
/// que contiene la geometría visible de una placa.
/// </summary>
/// <remarks>
/// El analizador no modifica la imagen original.
///
/// Para reducir falsos positivos provocados por fondos blancos y marcas
/// de agua claras, un píxel se considera geometría únicamente cuando su
/// canal alfa es visible y su canal RGB más oscuro no supera el umbral
/// configurado.
/// </remarks>
public sealed class BoardGeometryAnalyzer
{
    /// <summary>
    /// Umbral predeterminado utilizado para separar líneas oscuras de
    /// fondos y marcas de agua claras.
    /// </summary>
    public const byte DefaultDarkChannelThreshold = 190;

    /// <summary>
    /// Canal alfa mínimo considerado visible.
    /// </summary>
    public const byte DefaultMinimumAlpha = 1;

    /// <summary>
    /// Inicializa el analizador con los umbrales predeterminados.
    /// </summary>
    public BoardGeometryAnalyzer()
        : this(
            DefaultDarkChannelThreshold,
            DefaultMinimumAlpha)
    {
    }

    /// <summary>
    /// Inicializa el analizador con umbrales explícitos.
    /// </summary>
    /// <param name="darkChannelThreshold">
    /// Valor máximo permitido para el canal RGB más oscuro de un píxel.
    /// Los valores bajos seleccionan únicamente trazos más oscuros.
    /// </param>
    /// <param name="minimumAlpha">
    /// Canal alfa mínimo requerido para considerar visible un píxel.
    /// </param>
    public BoardGeometryAnalyzer(
        byte darkChannelThreshold,
        byte minimumAlpha)
    {
        DarkChannelThreshold = darkChannelThreshold;
        MinimumAlpha = minimumAlpha;
    }

    /// <summary>
    /// Obtiene el umbral aplicado al canal RGB más oscuro.
    /// </summary>
    public byte DarkChannelThreshold { get; }

    /// <summary>
    /// Obtiene el canal alfa mínimo considerado visible.
    /// </summary>
    public byte MinimumAlpha { get; }

    /// <summary>
    /// Calcula el rectángulo mínimo que contiene los píxeles clasificados
    /// como geometría de placa.
    /// </summary>
    /// <param name="pixelData">
    /// Píxeles de la imagen en formato BGRA32.
    /// </param>
    /// <param name="pixelWidth">Ancho de la imagen en píxeles.</param>
    /// <param name="pixelHeight">Alto de la imagen en píxeles.</param>
    /// <param name="stride">Cantidad de bytes ocupada por cada fila.</param>
    /// <returns>
    /// Resultado del análisis. Cuando no se detecta geometría,
    /// <see cref="BoardGeometryAnalysisResult.HasGeometry"/> es falso.
    /// </returns>
    public BoardGeometryAnalysisResult Analyze(
        byte[] pixelData,
        int pixelWidth,
        int pixelHeight,
        int stride)
    {
        ArgumentNullException.ThrowIfNull(pixelData);

        ValidateImageArguments(
            pixelData,
            pixelWidth,
            pixelHeight,
            stride);

        int left = pixelWidth;
        int top = pixelHeight;
        int right = -1;
        int bottom = -1;
        long matchingPixelCount = 0L;

        for (int y = 0; y < pixelHeight; y++)
        {
            int rowOffset = checked(y * stride);

            for (int x = 0; x < pixelWidth; x++)
            {
                int pixelOffset = checked(
                    rowOffset +
                    (x * 4));

                byte blue = pixelData[pixelOffset];
                byte green = pixelData[pixelOffset + 1];
                byte red = pixelData[pixelOffset + 2];
                byte alpha = pixelData[pixelOffset + 3];

                if (!IsGeometryPixel(
                        blue,
                        green,
                        red,
                        alpha))
                {
                    continue;
                }

                matchingPixelCount++;

                if (x < left)
                {
                    left = x;
                }

                if (x > right)
                {
                    right = x;
                }

                if (y < top)
                {
                    top = y;
                }

                if (y > bottom)
                {
                    bottom = y;
                }
            }
        }

        if (matchingPixelCount == 0L)
        {
            return BoardGeometryAnalysisResult.Empty;
        }

        var bounds = new BoardGeometryBounds(
            left,
            top,
            checked(right - left + 1),
            checked(bottom - top + 1));

        return new BoardGeometryAnalysisResult(
            bounds,
            matchingPixelCount);
    }

    /// <summary>
    /// Determina si un píxel pertenece a la geometría útil.
    /// </summary>
    private bool IsGeometryPixel(
        byte blue,
        byte green,
        byte red,
        byte alpha)
    {
        if (alpha < MinimumAlpha)
        {
            return false;
        }

        byte darkestChannel =
            Math.Min(
                red,
                Math.Min(green, blue));

        return darkestChannel <= DarkChannelThreshold;
    }

    /// <summary>
    /// Valida el contrato de memoria de una imagen BGRA32.
    /// </summary>
    private static void ValidateImageArguments(
        byte[] pixelData,
        int pixelWidth,
        int pixelHeight,
        int stride)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelWidth),
                pixelWidth,
                "El ancho de la imagen debe ser mayor que cero.");
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelHeight),
                pixelHeight,
                "El alto de la imagen debe ser mayor que cero.");
        }

        int minimumStride = checked(pixelWidth * 4);

        if (stride < minimumStride)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                stride,
                "El stride es menor que el requerido por una imagen BGRA32.");
        }

        long requiredLength = checked(
            (long)stride *
            pixelHeight);

        if (pixelData.LongLength < requiredLength)
        {
            throw new ArgumentException(
                "El búfer no contiene suficientes bytes para las dimensiones indicadas.",
                nameof(pixelData));
        }
    }
}

/// <summary>
/// Rectángulo entero que delimita la geometría detectada.
/// </summary>
public readonly record struct BoardGeometryBounds(
    int Left,
    int Top,
    int Width,
    int Height)
{
    /// <summary>
    /// Coordenada exclusiva del límite derecho.
    /// </summary>
    public int Right => checked(Left + Width);

    /// <summary>
    /// Coordenada exclusiva del límite inferior.
    /// </summary>
    public int Bottom => checked(Top + Height);
}

/// <summary>
/// Resultado inmutable producido por <see cref="BoardGeometryAnalyzer"/>.
/// </summary>
public readonly record struct BoardGeometryAnalysisResult(
    BoardGeometryBounds Bounds,
    long MatchingPixelCount)
{
    /// <summary>
    /// Resultado utilizado cuando la imagen no contiene geometría.
    /// </summary>
    public static BoardGeometryAnalysisResult Empty { get; } =
        new(default, 0L);

    /// <summary>
    /// Indica si se detectó al menos un píxel de geometría.
    /// </summary>
    public bool HasGeometry => MatchingPixelCount > 0L;
}