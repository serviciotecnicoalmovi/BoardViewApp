using System;

namespace BoardView.Rendering.Tiles;

/// <summary>
/// Representa una tesela ya renderizada de una página PDF.
///
/// Cada instancia contiene:
/// - La clave única que identifica la región renderizada.
/// - Las dimensiones de la imagen en píxeles.
/// - La cantidad de bytes por fila.
/// - Los datos de píxeles generados por el motor PDF.
/// - La fecha en que la tesela fue creada.
///
/// Esta clase todavía no modifica el visor actual.
/// Será almacenada posteriormente dentro de TileCache.
/// </summary>
public sealed class Tile
{
    /// <summary>
    /// Inicializa una nueva tesela renderizada.
    /// </summary>
    /// <param name="key">
    /// Clave que identifica la página, zoom y posición de la tesela.
    /// </param>
    /// <param name="pixelWidth">
    /// Ancho de la tesela en píxeles.
    /// </param>
    /// <param name="pixelHeight">
    /// Alto de la tesela en píxeles.
    /// </param>
    /// <param name="stride">
    /// Cantidad de bytes utilizada por cada fila de píxeles.
    /// </param>
    /// <param name="pixelData">
    /// Datos binarios de la imagen renderizada.
    ///
    /// El motor transferirá la propiedad de este arreglo a la tesela.
    /// No deberá modificarse después de crear la instancia.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Se produce si las dimensiones o el stride no son válidos.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Se produce si pixelData es null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Se produce si el arreglo no contiene suficientes datos.
    /// </exception>
    public Tile(
        TileKey key,
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixelData)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelWidth),
                pixelWidth,
                "El ancho de la tesela debe ser mayor que cero.");
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelHeight),
                pixelHeight,
                "El alto de la tesela debe ser mayor que cero.");
        }

        if (stride <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride),
                stride,
                "El stride debe ser mayor que cero.");
        }

        ArgumentNullException.ThrowIfNull(pixelData);

        int requiredLength;

        try
        {
            /*
             * Calcula la cantidad mínima de bytes necesaria para almacenar
             * todas las filas de píxeles de la tesela.
             *
             * checked provoca una excepción si la multiplicación supera
             * el límite admitido por un entero de 32 bits.
             */
            requiredLength = checked(stride * pixelHeight);
        }
        catch (OverflowException)
        {
            /*
             * ArgumentOutOfRangeException no dispone de un constructor
             * que acepte nombre, valor, mensaje y excepción interna.
             *
             * Se informa directamente que las dimensiones recibidas
             * producen un tamaño fuera del rango permitido.
             */
            throw new ArgumentOutOfRangeException(
                nameof(pixelHeight),
                pixelHeight,
                "Las dimensiones y el stride de la tesela generan un tamaño inválido.");
        }

        if (pixelData.Length < requiredLength)
        {
            throw new ArgumentException(
                $"El arreglo contiene {pixelData.Length} bytes, " +
                $"pero la tesela necesita al menos {requiredLength} bytes.",
                nameof(pixelData));
        }

        Key = key;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Stride = stride;
        PixelData = pixelData;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Obtiene la clave única de la tesela.
    /// </summary>
    public TileKey Key { get; }

    /// <summary>
    /// Obtiene el ancho de la imagen en píxeles.
    /// </summary>
    public int PixelWidth { get; }

    /// <summary>
    /// Obtiene el alto de la imagen en píxeles.
    /// </summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Obtiene la cantidad de bytes utilizada por cada fila.
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// Obtiene los datos de píxeles de la tesela.
    ///
    /// El arreglo se conserva sin realizar una copia para evitar
    /// duplicar innecesariamente el consumo de memoria.
    /// </summary>
    public byte[] PixelData { get; }

    /// <summary>
    /// Obtiene la fecha UTC en que se generó la tesela.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Obtiene el tamaño ocupado por los datos de imagen.
    /// Esta propiedad será utilizada posteriormente por TileCache
    /// para controlar el consumo máximo de memoria.
    /// </summary>
    public long SizeInBytes => PixelData.LongLength;

    /// <summary>
    /// Devuelve una descripción útil para diagnóstico y depuración.
    /// </summary>
    public override string ToString()
    {
        return $"{Key} " +
               $"Size={PixelWidth}x{PixelHeight} " +
               $"Stride={Stride} " +
               $"Bytes={SizeInBytes}";
    }
}
