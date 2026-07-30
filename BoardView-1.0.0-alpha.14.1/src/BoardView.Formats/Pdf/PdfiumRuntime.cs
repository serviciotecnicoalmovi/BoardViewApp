using System;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Administra la inicialización global de la biblioteca nativa PDFium.
///
/// PDFium debe inicializarse una sola vez durante la vida del proceso.
/// Esta clase permite que varios servicios, como el indexador textual
/// y el futuro renderizador gráfico, compartan la misma inicialización.
/// </summary>
internal static class PdfiumRuntime
{
    private static readonly object InitializationLock = new();

    private static bool isInitialized;

    /// <summary>
    /// Garantiza que PDFium haya sido inicializado.
    /// </summary>
    /// <remarks>
    /// El método es seguro para llamadas concurrentes.
    /// Las llamadas posteriores a la primera no vuelven a inicializar
    /// la biblioteca nativa.
    /// </remarks>
    internal static void EnsureInitialized()
    {
        if (isInitialized)
        {
            return;
        }

        lock (InitializationLock)
        {
            if (isInitialized)
            {
                return;
            }

            PdfiumNative.InitLibrary();

            /*
             * Esta asignación debe realizarse después de que la llamada
             * nativa finalice correctamente. Si PDFium genera una excepción,
             * el estado permanecerá sin inicializar y podrá volver a intentarse.
             */
            isInitialized = true;
        }
    }
}
