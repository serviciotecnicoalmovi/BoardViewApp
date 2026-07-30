namespace BoardView.Core.Pdf;

/// <summary>
/// Índice técnico de un PDF. Conserva páginas, palabras y coordenadas sin depender de WPF.
/// </summary>
public sealed class PdfDocumentIndex
{
    public PdfDocumentIndex(string filePath, IReadOnlyList<PdfPage> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        Pages = pages ?? throw new ArgumentNullException(nameof(pages));
    }

    public string FilePath { get; }

    public IReadOnlyList<PdfPage> Pages { get; }

    public int PageCount => Pages.Count;

    public int WordCount => Pages.Sum(page => page.Words.Count);
}
