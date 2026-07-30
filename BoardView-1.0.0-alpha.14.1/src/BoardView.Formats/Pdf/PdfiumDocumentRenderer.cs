using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Renderiza páginas y regiones de documentos PDF mediante PDFium.
/// </summary>
/// <remarks>
/// Cada instancia mantiene abierto un único documento PDF.
/// La clase es segura para llamadas concurrentes porque serializa
/// el acceso al documento nativo mediante un bloqueo interno.
/// </remarks>
public sealed class PdfiumDocumentRenderer : IDisposable
{
    private const int RenderAnnotationsFlag = 0x01;
    private const int RenderLcdTextFlag = 0x02;

    private readonly object _syncRoot = new();

    private IntPtr _document;
    private bool _disposed;

    /// <summary>
    /// Inicializa una instancia y abre el documento indicado.
    /// </summary>
    /// <param name="filePath">
    /// Ruta absoluta o relativa del documento PDF.
    /// </param>
    /// <exception cref="ArgumentException">
    /// La ruta está vacía.
    /// </exception>
    /// <exception cref="FileNotFoundException">
    /// El documento no existe.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// PDFium no pudo abrir el documento.
    /// </exception>
    public PdfiumDocumentRenderer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "La ruta del documento PDF no puede estar vacía.",
                nameof(filePath));
        }

        string absolutePath = Path.GetFullPath(filePath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException(
                "No se encontró el documento PDF.",
                absolutePath);
        }

        PdfiumRuntime.EnsureInitialized();

        _document = LoadDocument(absolutePath);

        if (_document == IntPtr.Zero)
        {
            uint errorCode = PdfiumNative.GetLastError();

            throw new InvalidOperationException(
                $"PDFium no pudo abrir el documento. Código de error: {errorCode}.");
        }

        PageCount = PdfiumNative.GetPageCount(_document);

        if (PageCount <= 0)
        {
            Dispose();

            throw new InvalidOperationException(
                "El documento PDF no contiene páginas renderizables.");
        }
    }

    /// <summary>
    /// Obtiene la cantidad total de páginas.
    /// </summary>
    public int PageCount { get; }

    /// <summary>
    /// Obtiene las dimensiones originales de una página en puntos PDF.
    /// </summary>
    /// <param name="pageIndex">
    /// Índice de página basado en cero.
    /// </param>
    public PdfiumPageSize GetPageSize(int pageIndex)
    {
        lock (_syncRoot)
        {
            ThrowIfDisposed();
            ValidatePageIndex(pageIndex);

            IntPtr page = PdfiumNative.LoadPage(
                _document,
                pageIndex);

            if (page == IntPtr.Zero)
            {
                throw CreatePageLoadException(pageIndex);
            }

            try
            {
                double width = PdfiumNative.GetPageWidth(page);
                double height = PdfiumNative.GetPageHeight(page);

                if (width <= 0D || height <= 0D)
                {
                    throw new InvalidOperationException(
                        $"La página {pageIndex} tiene dimensiones no válidas.");
                }

                return new PdfiumPageSize(width, height);
            }
            finally
            {
                PdfiumNative.ClosePage(page);
            }
        }
    }

    /// <summary>
    /// Renderiza una región de una página en formato BGRA de 32 bits.
    /// </summary>
    /// <param name="pageIndex">
    /// Índice de página basado en cero.
    /// </param>
    /// <param name="pagePixelWidth">
    /// Ancho total de la página en el nivel de zoom solicitado.
    /// </param>
    /// <param name="pagePixelHeight">
    /// Alto total de la página en el nivel de zoom solicitado.
    /// </param>
    /// <param name="regionX">
    /// Posición horizontal de la región dentro de la página.
    /// </param>
    /// <param name="regionY">
    /// Posición vertical de la región dentro de la página.
    /// </param>
    /// <param name="regionWidth">
    /// Ancho de la región en píxeles.
    /// </param>
    /// <param name="regionHeight">
    /// Alto de la región en píxeles.
    /// </param>
    /// <param name="cancellationToken">
    /// Permite cancelar antes de iniciar o copiar el resultado.
    /// </param>
    /// <returns>
    /// Resultado BGRA administrado, independiente de la memoria nativa.
    /// </returns>
    public PdfiumRenderResult RenderRegion(
        int pageIndex,
        int pagePixelWidth,
        int pagePixelHeight,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight,
        CancellationToken cancellationToken = default)
    {
        ValidateRenderArguments(
            pageIndex,
            pagePixelWidth,
            pagePixelHeight,
            regionX,
            regionY,
            regionWidth,
            regionHeight);

        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            IntPtr page = PdfiumNative.LoadPage(
                _document,
                pageIndex);

            if (page == IntPtr.Zero)
            {
                throw CreatePageLoadException(pageIndex);
            }

            IntPtr bitmap = IntPtr.Zero;

            try
            {
                bitmap = PdfiumNative.CreateBitmap(
                    regionWidth,
                    regionHeight,
                    alpha: 1);

                if (bitmap == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "PDFium no pudo crear el bitmap de renderizado.");
                }

                /*
                 * Fondo blanco opaco en formato ARGB.
                 */
                int fillResult =
                    PdfiumNative.FillBitmapRectangle(
                        bitmap,
                        left: 0,
                        top: 0,
                        width: regionWidth,
                        height: regionHeight,
                        color: 0xFFFFFFFF);

                if (fillResult == 0)
                {
                    throw new InvalidOperationException(
                        "PDFium no pudo limpiar el bitmap de renderizado.");
                }

                /*
                 * La página completa se desplaza en sentido contrario
                 * a la región solicitada. Solo la parte visible cae
                 * dentro del pequeño bitmap de la tesela.
                 */
                PdfiumNative.RenderPageBitmap(
                    bitmap,
                    page,
                    startX: -regionX,
                    startY: -regionY,
                    sizeX: pagePixelWidth,
                    sizeY: pagePixelHeight,
                    rotate: 0,
                    flags: RenderAnnotationsFlag | RenderLcdTextFlag);

                cancellationToken.ThrowIfCancellationRequested();

                IntPtr buffer =
                    PdfiumNative.GetBitmapBuffer(bitmap);

                int stride =
                    PdfiumNative.GetBitmapStride(bitmap);

                if (buffer == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        "PDFium devolvió un búfer de imagen nulo.");
                }

                if (stride <= 0)
                {
                    throw new InvalidOperationException(
                        "PDFium devolvió un stride no válido.");
                }

                int byteCount = checked(stride * regionHeight);
                byte[] pixels = new byte[byteCount];

                Marshal.Copy(
                    buffer,
                    pixels,
                    startIndex: 0,
                    length: byteCount);

                cancellationToken.ThrowIfCancellationRequested();

                return new PdfiumRenderResult(
                    regionWidth,
                    regionHeight,
                    stride,
                    pixels);
            }
            finally
            {
                if (bitmap != IntPtr.Zero)
                {
                    PdfiumNative.DestroyBitmap(bitmap);
                }

                PdfiumNative.ClosePage(page);
            }
        }
    }

    /// <summary>
    /// Cierra el documento nativo.
    /// </summary>
    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            if (_document != IntPtr.Zero)
            {
                PdfiumNative.CloseDocument(_document);
                _document = IntPtr.Zero;
            }

            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private static IntPtr LoadDocument(string absolutePath)
    {
        byte[] pathBytes =
            Encoding.UTF8.GetBytes(absolutePath + '\0');

        IntPtr pathPointer = Marshal.AllocHGlobal(
            pathBytes.Length);

        try
        {
            Marshal.Copy(
                pathBytes,
                startIndex: 0,
                pathPointer,
                pathBytes.Length);

            return PdfiumNative.LoadDocument(
                pathPointer,
                IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(pathPointer);
        }
    }

    private void ValidateRenderArguments(
        int pageIndex,
        int pagePixelWidth,
        int pagePixelHeight,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight)
    {
        ValidatePageIndex(pageIndex);

        if (pagePixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelWidth),
                pagePixelWidth,
                "El ancho total de la página debe ser positivo.");
        }

        if (pagePixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pagePixelHeight),
                pagePixelHeight,
                "El alto total de la página debe ser positivo.");
        }

        if (regionX < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionX),
                regionX,
                "La coordenada X no puede ser negativa.");
        }

        if (regionY < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionY),
                regionY,
                "La coordenada Y no puede ser negativa.");
        }

        if (regionWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionWidth),
                regionWidth,
                "El ancho de la región debe ser positivo.");
        }

        if (regionHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionHeight),
                regionHeight,
                "El alto de la región debe ser positivo.");
        }

        if (regionX >= pagePixelWidth ||
            regionY >= pagePixelHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(regionX),
                "La región comienza fuera de la página.");
        }

        if ((long)regionX + regionWidth > pagePixelWidth ||
            (long)regionY + regionHeight > pagePixelHeight)
        {
            throw new ArgumentException(
                "La región solicitada excede los límites de la página.");
        }
    }

    private void ValidatePageIndex(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"El índice debe estar entre 0 y {PageCount - 1}.");
        }
    }

    private static InvalidOperationException CreatePageLoadException(
        int pageIndex)
    {
        uint errorCode = PdfiumNative.GetLastError();

        return new InvalidOperationException(
            $"PDFium no pudo cargar la página {pageIndex}. " +
            $"Código de error: {errorCode}.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }
}

/// <summary>
/// Dimensiones originales de una página PDF expresadas en puntos.
/// </summary>
public readonly record struct PdfiumPageSize(
    double Width,
    double Height);

/// <summary>
/// Imagen renderizada en formato BGRA de 32 bits.
/// </summary>
public sealed class PdfiumRenderResult
{
    /// <summary>
    /// Inicializa un resultado de renderizado.
    /// </summary>
    public PdfiumRenderResult(
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixelData)
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

        if (stride < pixelWidth * PdfiumNative.BytesPerPixel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stride));
        }

        int expectedLength =
            checked(stride * pixelHeight);

        if (pixelData.Length != expectedLength)
        {
            throw new ArgumentException(
                "La longitud del búfer no coincide con las dimensiones.",
                nameof(pixelData));
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Stride = stride;
        PixelData = pixelData;
    }

    /// <summary>
    /// Ancho de la imagen en píxeles.
    /// </summary>
    public int PixelWidth { get; }

    /// <summary>
    /// Alto de la imagen en píxeles.
    /// </summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Cantidad de bytes por fila.
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// Píxeles BGRA de 32 bits.
    /// </summary>
    public byte[] PixelData { get; }
}
