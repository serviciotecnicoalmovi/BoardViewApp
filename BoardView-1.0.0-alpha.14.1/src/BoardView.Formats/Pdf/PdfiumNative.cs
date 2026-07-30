using System;
using System.Runtime.InteropServices;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Declaraciones nativas de PDFium utilizadas para abrir documentos,
/// consultar páginas, extraer texto y renderizar contenido gráfico.
/// </summary>
internal static partial class PdfiumNative
{
    private const string LibraryName = "pdfium";

    /// <summary>
    /// Cantidad de bytes utilizada por cada píxel BGRA.
    /// </summary>
    internal const int BytesPerPixel = 4;

    #region Estructuras nativas

    /// <summary>
    /// Matriz afín utilizada por PDFium para transformar coordenadas
    /// de página en coordenadas del dispositivo de salida.
    /// </summary>
    /// <remarks>
    /// La transformación aplicada por PDFium es:
    ///
    /// x' = a * x + c * y + e
    /// y' = b * x + d * y + f
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PdfiumMatrix
    {
        /// <summary>
        /// Escala horizontal y componente X de la rotación.
        /// </summary>
        internal float A;

        /// <summary>
        /// Componente Y de la rotación.
        /// </summary>
        internal float B;

        /// <summary>
        /// Componente X de la inclinación o rotación.
        /// </summary>
        internal float C;

        /// <summary>
        /// Escala vertical y componente Y de la rotación.
        /// </summary>
        internal float D;

        /// <summary>
        /// Traslación horizontal.
        /// </summary>
        internal float E;

        /// <summary>
        /// Traslación vertical.
        /// </summary>
        internal float F;

        /// <summary>
        /// Inicializa una matriz afín.
        /// </summary>
        internal PdfiumMatrix(
            float a,
            float b,
            float c,
            float d,
            float e,
            float f)
        {
            A = a;
            B = b;
            C = c;
            D = d;
            E = e;
            F = f;
        }
    }

    /// <summary>
    /// Rectángulo de recorte expresado en coordenadas del bitmap destino.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PdfiumRectangle
    {
        /// <summary>
        /// Coordenada izquierda.
        /// </summary>
        internal float Left;

        /// <summary>
        /// Coordenada superior.
        /// </summary>
        internal float Top;

        /// <summary>
        /// Coordenada derecha.
        /// </summary>
        internal float Right;

        /// <summary>
        /// Coordenada inferior.
        /// </summary>
        internal float Bottom;

        /// <summary>
        /// Inicializa un rectángulo de recorte.
        /// </summary>
        internal PdfiumRectangle(
            float left,
            float top,
            float right,
            float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    #endregion

    #region Inicialización y errores

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_InitLibrary")]
    internal static partial void InitLibrary();

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_GetLastError")]
    internal static partial uint GetLastError();

    #endregion

    #region Documento

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_LoadDocument")]
    internal static partial IntPtr LoadDocument(
        IntPtr filePathUtf8,
        IntPtr passwordUtf8);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_CloseDocument")]
    internal static partial void CloseDocument(
        IntPtr document);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_GetPageCount")]
    internal static partial int GetPageCount(
        IntPtr document);

    #endregion

    #region Página

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_LoadPage")]
    internal static partial IntPtr LoadPage(
        IntPtr document,
        int pageIndex);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_ClosePage")]
    internal static partial void ClosePage(
        IntPtr page);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_GetPageWidth")]
    internal static partial double GetPageWidth(
        IntPtr page);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDF_GetPageHeight")]
    internal static partial double GetPageHeight(
        IntPtr page);

    #endregion

    #region Renderizado gráfico

    /*
     * Estas funciones utilizan DllImport en lugar de LibraryImport.
     *
     * De esta manera no dependen del generador de código fuente para
     * producir las implementaciones de los métodos partial.
     */

    /// <summary>
    /// Crea un bitmap administrado internamente por PDFium.
    /// </summary>
    /// <param name="width">Ancho en píxeles.</param>
    /// <param name="height">Alto en píxeles.</param>
    /// <param name="alpha">
    /// Uno para habilitar el canal alfa; cero para un bitmap opaco.
    /// </param>
    /// <returns>
    /// Handle del bitmap o <see cref="IntPtr.Zero"/> si no pudo crearse.
    /// </returns>
    [DllImport(
        LibraryName,
        EntryPoint = "FPDFBitmap_Create",
        ExactSpelling = true)]
    internal static extern IntPtr CreateBitmap(
        int width,
        int height,
        int alpha);

    /// <summary>
    /// Destruye un bitmap previamente creado por PDFium.
    /// </summary>
    [DllImport(
        LibraryName,
        EntryPoint = "FPDFBitmap_Destroy",
        ExactSpelling = true)]
    internal static extern void DestroyBitmap(
        IntPtr bitmap);

    /// <summary>
    /// Obtiene el puntero al búfer de píxeles del bitmap.
    /// </summary>
    [DllImport(
        LibraryName,
        EntryPoint = "FPDFBitmap_GetBuffer",
        ExactSpelling = true)]
    internal static extern IntPtr GetBitmapBuffer(
        IntPtr bitmap);

    /// <summary>
    /// Obtiene la cantidad de bytes que ocupa cada fila.
    /// </summary>
    [DllImport(
        LibraryName,
        EntryPoint = "FPDFBitmap_GetStride",
        ExactSpelling = true)]
    internal static extern int GetBitmapStride(
        IntPtr bitmap);

    /// <summary>
    /// Rellena una región del bitmap con un color ARGB.
    /// </summary>
    /// <returns>
    /// Un valor diferente de cero cuando la operación se completa.
    /// </returns>
    [DllImport(
        LibraryName,
        EntryPoint = "FPDFBitmap_FillRect",
        ExactSpelling = true)]
    internal static extern int FillBitmapRectangle(
        IntPtr bitmap,
        int left,
        int top,
        int width,
        int height,
        uint color);

    /// <summary>
    /// Renderiza una página PDF dentro de un bitmap mediante
    /// desplazamiento y tamaño enteros.
    /// </summary>
    [DllImport(
        LibraryName,
        EntryPoint = "FPDF_RenderPageBitmap",
        ExactSpelling = true)]
    internal static extern void RenderPageBitmap(
        IntPtr bitmap,
        IntPtr page,
        int startX,
        int startY,
        int sizeX,
        int sizeY,
        int rotate,
        int flags);

    /// <summary>
    /// Renderiza una página PDF dentro de un bitmap mediante una
    /// transformación afín y un rectángulo de recorte.
    /// </summary>
    /// <param name="bitmap">
    /// Bitmap de destino creado mediante <see cref="CreateBitmap"/>.
    /// </param>
    /// <param name="page">
    /// Página PDF previamente cargada.
    /// </param>
    /// <param name="matrix">
    /// Matriz que transforma las coordenadas PDF en coordenadas
    /// del bitmap destino.
    /// </param>
    /// <param name="clipping">
    /// Rectángulo de recorte expresado en coordenadas del bitmap.
    /// </param>
    /// <param name="flags">
    /// Combinación de opciones de renderizado de PDFium.
    /// </param>
    [DllImport(
        LibraryName,
        EntryPoint = "FPDF_RenderPageBitmapWithMatrix",
        ExactSpelling = true)]
    internal static extern void RenderPageBitmapWithMatrix(
        IntPtr bitmap,
        IntPtr page,
        in PdfiumMatrix matrix,
        in PdfiumRectangle clipping,
        int flags);

    #endregion

    #region Extracción textual

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDFText_LoadPage")]
    internal static partial IntPtr LoadTextPage(
        IntPtr page);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDFText_ClosePage")]
    internal static partial void CloseTextPage(
        IntPtr textPage);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDFText_CountChars")]
    internal static partial int CountChars(
        IntPtr textPage);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDFText_GetUnicode")]
    internal static partial uint GetUnicode(
        IntPtr textPage,
        int index);

    [LibraryImport(
        LibraryName,
        EntryPoint = "FPDFText_GetCharBox")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCharBox(
        IntPtr textPage,
        int index,
        out double left,
        out double right,
        out double bottom,
        out double top);

    #endregion
}
