namespace BoardView.Core.Pdf;

/// <summary>Resultado de una indexación PDF tolerante a errores.</summary>
public sealed class SafePdfIndexResult
{
    public SafePdfIndexResult(
        PdfDocumentIndex index,
        int indexedPageCount,
        bool usedSanitizedCopy,
        IReadOnlyList<string> warnings)
    {
        Index = index ?? throw new ArgumentNullException(nameof(index));
        IndexedPageCount = Math.Max(0, indexedPageCount);
        UsedSanitizedCopy = usedSanitizedCopy;
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    /// <summary>Índice utilizable, incluso cuando algunas páginas fueron omitidas.</summary>
    public PdfDocumentIndex Index { get; }

    /// <summary>Número de páginas cuya extracción textual terminó correctamente.</summary>
    public int IndexedPageCount { get; }

    /// <summary>Indica que se utilizó una copia temporal sin destinos ni anotaciones.</summary>
    public bool UsedSanitizedCopy { get; }

    /// <summary>Advertencias no fatales generadas durante la lectura.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Indica si el documento produjo al menos una palabra indexable.</summary>
    public bool HasSearchableText => Index.WordCount > 0;
}
