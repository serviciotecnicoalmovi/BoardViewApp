namespace BoardView.Core.Pdf;

/// <summary>
/// Construye un índice textual aislado del parser geométrico. La implementación
/// debe tolerar páginas o estructuras PDF defectuosas sin finalizar la aplicación.
/// </summary>
public interface ISafePdfDocumentIndexer
{
    /// <summary>
    /// Indexa el documento de forma asíncrona y devuelve tanto el índice como
    /// las advertencias producidas durante la lectura.
    /// </summary>
    Task<SafePdfIndexResult> BuildIndexAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}
