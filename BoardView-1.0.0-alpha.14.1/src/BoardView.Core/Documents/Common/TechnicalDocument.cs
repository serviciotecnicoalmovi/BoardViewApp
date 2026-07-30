namespace BoardView.Core.Documents.Common;

/// <summary>
/// Raíz del modelo documental común consumido por parsers, buscadores y renderizadores.
/// </summary>
public sealed class TechnicalDocument
{
    private readonly List<DocumentPage> pages = [];

    public TechnicalDocument(DocumentInfo info, MeasurementUnit normalizedUnit = MeasurementUnit.Millimeter)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        if (normalizedUnit == MeasurementUnit.Pixel)
        {
            throw new ArgumentException(
                "La unidad normalizada debe representar una escala física.",
                nameof(normalizedUnit));
        }

        NormalizedUnit = normalizedUnit;
    }

    public DocumentInfo Info { get; }
    public MeasurementUnit NormalizedUnit { get; }
    public IReadOnlyList<DocumentPage> Pages => pages;
    public DocumentMetadata Metadata { get; } = new();

    public void AddPage(DocumentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (pages.Any(item => item.Number == page.Number))
        {
            throw new InvalidOperationException($"Ya existe la página número {page.Number}.");
        }

        pages.Add(page);
        pages.Sort(static (left, right) => left.Number.CompareTo(right.Number));
    }
}
