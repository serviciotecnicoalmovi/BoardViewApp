using System;
using System.Collections.Generic;

namespace BoardView.Rendering.Geometry;

/// <summary>
/// Representa una máscara binaria inmutable de geometría.
/// </summary>
/// <remarks>
/// Cada píxel de la imagen original se clasifica como:
///
/// <list type="bullet">
/// <item><c>0</c>: fondo.</item>
/// <item><c>1</c>: geometría.</item>
/// </list>
///
/// La máscara utiliza un byte por píxel para mantener un acceso directo,
/// predecible y compatible con futuras operaciones de análisis,
/// recorte, contornos y componentes conectados.
/// </remarks>
public sealed class BoardGeometryMask
{
    private const byte BackgroundValue = 0;
    private const byte GeometryValue = 1;

    private readonly byte[] _data;

    /// <summary>
    /// Inicializa una máscara binaria a partir de datos ya clasificados.
    /// </summary>
    /// <param name="width">Ancho de la máscara en píxeles.</param>
    /// <param name="height">Alto de la máscara en píxeles.</param>
    /// <param name="data">
    /// Datos binarios organizados por filas. Cada elemento debe ser
    /// <c>0</c> para fondo o <c>1</c> para geometría.
    /// </param>
    /// <param name="geometryPixelCount">
    /// Cantidad total de píxeles clasificados como geometría.
    /// </param>
    private BoardGeometryMask(
        int width,
        int height,
        byte[] data,
        long geometryPixelCount)
    {
        Width = width;
        Height = height;
        _data = data;
        GeometryPixelCount = geometryPixelCount;
    }

    /// <summary>
    /// Obtiene el ancho de la máscara en píxeles.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Obtiene el alto de la máscara en píxeles.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Obtiene la cantidad total de píxeles almacenados.
    /// </summary>
    public int PixelCount => checked(Width * Height);

    /// <summary>
    /// Obtiene la cantidad de píxeles clasificados como geometría.
    /// </summary>
    public long GeometryPixelCount { get; }

    /// <summary>
    /// Indica si la máscara contiene al menos un píxel de geometría.
    /// </summary>
    public bool HasGeometry => GeometryPixelCount > 0L;

    /// <summary>
    /// Obtiene el valor binario almacenado en una coordenada.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// La coordenada se encuentra fuera de la máscara.
    /// </exception>
    public byte this[int x, int y]
    {
        get
        {
            ValidateCoordinates(x, y);

            return _data[GetOffsetUnchecked(x, y)];
        }
    }

    /// <summary>
    /// Crea una máscara binaria a partir de una imagen BGRA32.
    /// </summary>
    /// <param name="pixelData">
    /// Píxeles de la imagen en formato BGRA32.
    /// </param>
    /// <param name="pixelWidth">Ancho de la imagen en píxeles.</param>
    /// <param name="pixelHeight">Alto de la imagen en píxeles.</param>
    /// <param name="stride">Cantidad de bytes ocupada por cada fila.</param>
    /// <param name="darkChannelThreshold">
    /// Valor máximo permitido para el canal RGB más oscuro de un píxel.
    /// Los valores más bajos conservan únicamente trazos más oscuros.
    /// </param>
    /// <param name="minimumAlpha">
    /// Canal alfa mínimo requerido para considerar visible un píxel.
    /// </param>
    /// <returns>
    /// Máscara binaria independiente del búfer de imagen original.
    /// </returns>
    public static BoardGeometryMask CreateFromBgra32(
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

        ValidateImageArguments(
            pixelData,
            pixelWidth,
            pixelHeight,
            stride);

        int maskLength = checked(
            pixelWidth *
            pixelHeight);

        var maskData = new byte[maskLength];

        long geometryPixelCount = 0L;

        for (int y = 0; y < pixelHeight; y++)
        {
            int sourceRowOffset = checked(y * stride);
            int maskRowOffset = checked(y * pixelWidth);

            for (int x = 0; x < pixelWidth; x++)
            {
                int sourceOffset = checked(
                    sourceRowOffset +
                    (x * 4));

                byte blue = pixelData[sourceOffset];
                byte green = pixelData[sourceOffset + 1];
                byte red = pixelData[sourceOffset + 2];
                byte alpha = pixelData[sourceOffset + 3];

                if (!IsGeometryPixel(
                        blue,
                        green,
                        red,
                        alpha,
                        darkChannelThreshold,
                        minimumAlpha))
                {
                    continue;
                }

                maskData[maskRowOffset + x] =
                    GeometryValue;

                geometryPixelCount++;
            }
        }

        return new BoardGeometryMask(
            pixelWidth,
            pixelHeight,
            maskData,
            geometryPixelCount);
    }

    /// <summary>
    /// Indica si una coordenada contiene geometría.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// La coordenada se encuentra fuera de la máscara.
    /// </exception>
    public bool IsGeometry(int x, int y)
    {
        return this[x, y] == GeometryValue;
    }

    /// <summary>
    /// Indica si una coordenada pertenece al fondo.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// La coordenada se encuentra fuera de la máscara.
    /// </exception>
    public bool IsBackground(int x, int y)
    {
        return this[x, y] == BackgroundValue;
    }

    /// <summary>
    /// Intenta obtener el valor binario de una coordenada sin lanzar
    /// una excepción cuando está fuera de los límites.
    /// </summary>
    public bool TryGetValue(
        int x,
        int y,
        out byte value)
    {
        if ((uint)x >= (uint)Width ||
            (uint)y >= (uint)Height)
        {
            value = BackgroundValue;
            return false;
        }

        value = _data[GetOffsetUnchecked(x, y)];
        return true;
    }

    /// <summary>
    /// Devuelve una copia independiente de los datos binarios.
    /// </summary>
    /// <remarks>
    /// La copia evita que código externo pueda modificar el estado
    /// interno de la máscara.
    /// </remarks>
    public byte[] ToArray()
    {
        return (byte[])_data.Clone();
    }

    /// <summary>
    /// Enumera las coordenadas clasificadas como geometría.
    /// </summary>
    /// <remarks>
    /// Este método evita asignaciones adicionales para píxeles de fondo,
    /// pero puede recorrer toda la máscara.
    /// </remarks>
    public IEnumerable<BoardGeometryPoint> EnumerateGeometryPixels()
    {
        for (int y = 0; y < Height; y++)
        {
            int rowOffset = checked(y * Width);

            for (int x = 0; x < Width; x++)
            {
                if (_data[rowOffset + x] != GeometryValue)
                {
                    continue;
                }

                yield return new BoardGeometryPoint(x, y);
            }
        }
    }

    /// <summary>
    /// Determina si un píxel BGRA32 pertenece a la geometría útil.
    /// </summary>
    private static bool IsGeometryPixel(
        byte blue,
        byte green,
        byte red,
        byte alpha,
        byte darkChannelThreshold,
        byte minimumAlpha)
    {
        if (alpha < minimumAlpha)
        {
            return false;
        }

        byte darkestChannel =
            Math.Min(
                red,
                Math.Min(green, blue));

        return darkestChannel <= darkChannelThreshold;
    }

    /// <summary>
    /// Obtiene la posición lineal de una coordenada ya validada.
    /// </summary>
    private int GetOffsetUnchecked(int x, int y)
    {
        return checked(
            (y * Width) +
            x);
    }

    /// <summary>
    /// Valida una coordenada de la máscara.
    /// </summary>
    private void ValidateCoordinates(int x, int y)
    {
        if ((uint)x >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                x,
                "La coordenada X se encuentra fuera de la máscara.");
        }

        if ((uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y),
                y,
                "La coordenada Y se encuentra fuera de la máscara.");
        }
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
/// Coordenada entera dentro de una máscara geométrica.
/// </summary>
public readonly record struct BoardGeometryPoint(
    int X,
    int Y);
