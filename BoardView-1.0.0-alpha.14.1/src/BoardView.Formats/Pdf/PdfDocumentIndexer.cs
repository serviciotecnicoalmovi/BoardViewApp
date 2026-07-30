using BoardView.Core.Pdf;
using UglyToad.PdfPig;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Extrae páginas, palabras y coordenadas de documentos PDF vectoriales.
/// Esta primera etapa no intenta inferir componentes electrónicos ni redes.
/// </summary>
public sealed class PdfDocumentIndexer : IPdfDocumentIndexer
{
    public PdfDocumentIndex BuildIndex(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("No se encontró el documento PDF.", filePath);
        }

        string absolutePath = Path.GetFullPath(filePath);
        PdfRawCompatibilityInfo compatibility = PdfRawCompatibilityProbe.Inspect(absolutePath);
        if (compatibility.HasMalformedDotRetDestination)
        {
            IReadOnlyList<PdfPage> fallbackPages = Enumerable
                .Range(1, compatibility.EstimatedPageCount)
                .Select(static pageNumber => new PdfPage(pageNumber, 0D, 0D, Array.Empty<PdfWord>()))
                .ToArray();

            return new PdfDocumentIndex(absolutePath, fallbackPages);
        }

        List<PdfPage> pages = new();

        using PdfDocument document = PdfDocument.Open(absolutePath, PdfParsingOptionsFactory.CreateResilient());
        foreach (UglyToad.PdfPig.Content.Page sourcePage in document.GetPages())
        {
            List<PdfWord> words = sourcePage
                .GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .Select(word => new PdfWord(
                    word.Text,
                    word.BoundingBox.Left,
                    word.BoundingBox.Bottom,
                    word.BoundingBox.Width,
                    word.BoundingBox.Height))
                .ToList();

            pages.Add(new PdfPage(
                sourcePage.Number,
                Convert.ToDouble(sourcePage.Width),
                Convert.ToDouble(sourcePage.Height),
                words));
        }

        return new PdfDocumentIndex(absolutePath, pages);
    }
}
