using System;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Recorta imágenes BGRA32 utilizando límites geométricos previamente
/// calculados.
/// </summary>
/// <remarks>
/// Esta clase no interpreta el contenido de la imagen. Su única
/// responsabilidad es copiar una región rectangular válida desde un
/// búfer BGRA32 hacia un nuevo búfer independiente.
///
/// La detección de los límites debe realizarse previamente mediante
/// <see cref="BoardGeometryAnalyzer"/> o una máscara equivalente.
/// </remarks>
public sealed class BoardGeometryCropper
{
    private const int BytesPerPixel = 4;

    /// <summary>
    /// Recorta una imagen BGRA32 utilizando el resultado de un análisis
    /// geométrico.
    /// </summary>
    /// <param name="pixelData">
    /// Píxeles de la imagen original en formato BGRA32.
    /// </param>
    /// <param name="pixelWidth">
    /// Ancho de la imagen original en píxeles.
    /// </param>
    /// <param name="pixelHeight">
    /// Alto de la imagen original en píxeles.
    /// </param>
    /// <param name="stride">
    /// Cantidad de bytes ocupada por cada fila de la imagen original.
    /// </param>
    /// <param name="analysisResult">
    /// Resultado que contiene los límites geométricos a recortar.
    /// </param>
    /// <returns>
    /// Imagen recortada con memoria independiente.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// El análisis no contiene geometría.
    /// </exception>
    public BoardGeometryCropResult Crop(
        byte[] pixelData,
        int pixelWidth,
        int pixelHeight,
        int stride,
        BoardGeometryAnalysisResult analysisResult)
    {
        if (!analysisResult.HasGeometry)
        {
            throw new InvalidOperationException(
                "No es posible recortar una imagen sin geometría detectada.");
        }

        return Crop(
            pixelData,
            pixelWidth,
            pixelHeight,
            stride,
            analysisResult.Bounds);
    }

    /// <summary>
    /// Recorta una imagen BGRA32 utilizando límites explícitos.
    /// </summary>
    /// <param name="pixelData">
    /// Píxeles de la imagen original en formato BGRA32.
    /// </param>
    /// <param name="pixelWidth">
    /// Ancho de la imagen original en píxeles.
    /// </param>
    /// <param name="pixelHeight">
    /// Alto de la imagen original en píxeles.
    /// </param>
    /// <param name="stride">
    /// Cantidad de bytes ocupada por cada fila de la imagen original.
    /// </param>
    /// <param name="bounds">
    /// Rectángulo que se copiará desde la imagen original.
    /// </param>
    /// <returns>
    /// Imagen recortada con memoria independiente.
    /// </returns>
    public BoardGeometryCropResult Crop(
        byte[] pixelData,
        int pixelWidth,
        int pixelHeight,
        int stride,
        BoardGeometryBounds bounds)
    {
        ArgumentNullException.ThrowIfNull(pixelData);

        ValidateSourceImage(
            pixelData,
            pixelWidth,
            pixelHeight,
            stride);

        ValidateBounds(
            bounds,
            pixelWidth,
            pixelHeight);

        int destinationStride = checked(
            bounds.Width *
            BytesPerPixel);

        int destinationLength = checked(
            destinationStride *
            bounds.Height);

        var destination = new byte[destinationLength];

        int sourceRowByteOffset = checked(
            bounds.Left *
            BytesPerPixel);

        for (int destinationY = 0;
             destinationY < bounds.Height;
             destinationY++)
        {
            int sourceY = checked(
                bounds.Top +
                destinationY);

            int sourceOffset = checked(
                (sourceY * stride) +
                sourceRowByteOffset);

            int destinationOffset = checked(
                destinationY *
                destinationStride);

            Buffer.BlockCopy(
                pixelData,
                sourceOffset,
                destination,
                destinationOffset,
                destinationStride);
        }

        return new BoardGeometryCropResult(
            destination,
            bounds.Width,
            bounds.Height,
            destinationStride,
            bounds);
    }

    /// <summary>
    /// Recorta automáticamente una imagen BGRA32.
    /// </summary>
    /// <remarks>
    /// Este método combina el análisis de límites con la copia de la
    /// región detectada. Los umbrales se aplican únicamente durante la
    /// detección; los píxeles del resultado se copian sin modificarlos.
    /// </remarks>
    /// <param name="pixelData">
    /// Píxeles de la imagen original en formato BGRA32.
    /// </param>
    /// <param name="pixelWidth">
    /// Ancho de la imagen original en píxeles.
    /// </param>
    /// <param name="pixelHeight">
    /// Alto de la imagen original en píxeles.
    /// </param>
    /// <param name="stride">
    /// Cantidad de bytes ocupada por cada fila de la imagen original.
    /// </param>
    /// <param name="darkChannelThreshold">
    /// Umbral máximo aplicado al canal RGB más oscuro.
    /// </param>
    /// <param name="minimumAlpha">
    /// Canal alfa mínimo requerido para considerar visible un píxel.
    /// </param>
    /// <returns>
    /// Resultado que indica si se detectó geometría y, cuando existe,
    /// contiene la imagen recortada.
    /// </returns>
    public BoardGeometryAutoCropResult AutoCrop(
        byte[] pixelData,
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte darkChannelThreshold =
            BoardGeometryAnalyzer.DefaultDarkChannelThreshold,
        byte minimumAlpha =
            BoardGeometryAnalyzer.DefaultMinimumAlpha)
    {
        ArgumentNullException.ThrowIfNull(pixelData);

        var analyzer = new BoardGeometryAnalyzer(
            darkChannelThreshold,
            minimumAlpha);

        BoardGeometryAnalysisResult analysis =
            analyzer.Analyze(
                pixelData,
                pixelWidth,
                pixelHeight,
                stride);

        if (!analysis.HasGeometry)
        {
            return new BoardGeometryAutoCropResult(
            analysis,
            null);
        }

        BoardGeometryCropResult cropResult =
            Crop(
                pixelData,
                pixelWidth,
                pixelHeight,
                stride,
                analysis.Bounds);

        return new BoardGeometryAutoCropResult(
            analysis,
            cropResult);
    }

    /// <summary>
    /// Valida el contrato de memoria de la imagen original.
    /// </summary>
    private static void ValidateSourceImage(
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

        int minimumStride = checked(
            pixelWidth *
            BytesPerPixel);

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

    /// <summary>
    /// Valida que el rectángulo de recorte pertenezca completamente a la
    /// imagen original.
    /// </summary>
    private static void ValidateBounds(
        BoardGeometryBounds bounds,
        int pixelWidth,
        int pixelHeight)
    {
        if (bounds.Left < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds.Left,
                "El límite izquierdo no puede ser negativo.");
        }

        if (bounds.Top < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds.Top,
                "El límite superior no puede ser negativo.");
        }

        if (bounds.Width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds.Width,
                "El ancho del recorte debe ser mayor que cero.");
        }

        if (bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bounds.Height,
                "El alto del recorte debe ser mayor que cero.");
        }

        long right = checked(
            (long)bounds.Left +
            bounds.Width);

        long bottom = checked(
            (long)bounds.Top +
            bounds.Height);

        if (right > pixelWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                right,
                "El recorte supera el límite derecho de la imagen.");
        }

        if (bottom > pixelHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                bottom,
                "El recorte supera el límite inferior de la imagen.");
        }
    }
}

/// <summary>
/// Imagen BGRA32 recortada y su posición dentro de la imagen original.
/// </summary>
public sealed class BoardGeometryCropResult
{
    private readonly byte[] _pixelData;

    /// <summary>
    /// Inicializa un resultado de recorte.
    /// </summary>
    public BoardGeometryCropResult(
        byte[] pixelData,
        int pixelWidth,
        int pixelHeight,
        int stride,
        BoardGeometryBounds sourceBounds)
    {
        ArgumentNullException.ThrowIfNull(pixelData);

        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelWidth));
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelHeight));
        }

        int requiredStride = checked(
            pixelWidth *
            4);

        if (stride != requiredStride)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                stride,
                "El resultado de recorte debe utilizar un stride BGRA32 compacto.");
        }

        int requiredLength = checked(
            stride *
            pixelHeight);

        if (pixelData.Length != requiredLength)
        {
            throw new ArgumentException(
                "El tamaño del búfer no coincide con las dimensiones del recorte.",
                nameof(pixelData));
        }

        _pixelData = pixelData;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Stride = stride;
        SourceBounds = sourceBounds;
    }

    /// <summary>
    /// Obtiene el ancho del resultado en píxeles.
    /// </summary>
    public int PixelWidth { get; }

    /// <summary>
    /// Obtiene el alto del resultado en píxeles.
    /// </summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Obtiene la cantidad de bytes ocupada por cada fila.
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// Obtiene el rectángulo original desde el que se produjo el recorte.
    /// </summary>
    public BoardGeometryBounds SourceBounds { get; }

    /// <summary>
    /// Obtiene la cantidad total de bytes del resultado.
    /// </summary>
    public int SizeInBytes => _pixelData.Length;

    /// <summary>
    /// Devuelve una copia independiente de los píxeles BGRA32.
    /// </summary>
    public byte[] ToArray()
    {
        return (byte[])_pixelData.Clone();
    }

    /// <summary>
    /// Copia los píxeles hacia un búfer proporcionado por el llamador.
    /// </summary>
    /// <param name="destination">
    /// Búfer de destino.
    /// </param>
    /// <param name="destinationOffset">
    /// Posición inicial dentro del búfer de destino.
    /// </param>
    public void CopyTo(
        byte[] destination,
        int destinationOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destinationOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOffset));
        }

        int requiredLength = checked(
            destinationOffset +
            _pixelData.Length);

        if (destination.Length < requiredLength)
        {
            throw new ArgumentException(
                "El búfer de destino no tiene capacidad suficiente.",
                nameof(destination));
        }

        Buffer.BlockCopy(
            _pixelData,
            0,
            destination,
            destinationOffset,
            _pixelData.Length);
    }
}

/// <summary>
/// Resultado de una operación de recorte automático.
/// </summary>
public readonly record struct BoardGeometryAutoCropResult(
    BoardGeometryAnalysisResult Analysis,
    BoardGeometryCropResult? CropResult)
{
    /// <summary>
    /// Indica si se detectó y recortó geometría.
    /// </summary>
    public bool HasGeometry =>
        Analysis.HasGeometry &&
        CropResult is not null;
}
