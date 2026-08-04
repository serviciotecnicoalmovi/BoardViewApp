namespace BoardView.Formats.Pdf;

/// <summary>
/// Serializa globalmente el acceso a PDFium dentro del proceso.
/// </summary>
/// <remarks>
/// PDFium puede ser utilizado por varios servicios del proyecto:
/// renderizador, indexador textual y renderizador de teselas. Un bloqueo por
/// instancia no protege llamadas realizadas desde instancias diferentes.
///
/// Todas las operaciones que abran, consulten, rendericen o cierren recursos
/// nativos de PDFium deben ejecutarse mediante esta compuerta.
/// </remarks>
internal static class PdfiumNativeExecutionGate
{
    private static readonly SemaphoreSlim Gate =
        new(
            initialCount: 1,
            maxCount: 1);

    /// <summary>
    /// Ejecuta una operación síncrona manteniendo acceso exclusivo a PDFium.
    /// </summary>
    public static T Run<T>(
        Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(
            operation);

        Gate.Wait();

        try
        {
            return operation();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Ejecuta una acción síncrona manteniendo acceso exclusivo a PDFium.
    /// </summary>
    public static void Run(
        Action operation)
    {
        ArgumentNullException.ThrowIfNull(
            operation);

        Gate.Wait();

        try
        {
            operation();
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Ejecuta una operación en segundo plano manteniendo acceso exclusivo a
    /// PDFium durante toda la vida de los recursos nativos utilizados.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            operation);

        await Gate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return await Task
                .Run(
                    operation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }
}
