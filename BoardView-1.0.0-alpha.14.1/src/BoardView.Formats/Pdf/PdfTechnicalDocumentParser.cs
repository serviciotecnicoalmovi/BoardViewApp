using BoardView.Core.Contracts.Documents;
using BoardView.Core.Documents.Common;
using BoardView.Core.Geometry;
using BoardView.Core.Graphics;
using UglyToad.PdfPig;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Convierte el contenido textual posicionado de un PDF al modelo documental común.
/// La geometría se normaliza a milímetros y conserva las coordenadas originales
/// del sistema PDF, cuyo origen se encuentra en la esquina inferior izquierda.
/// </summary>
public sealed class PdfTechnicalDocumentParser : IDocumentParser
{
    public string FormatId => "pdf";

    public bool CanParse(string filePath) =>
        !string.IsNullOrWhiteSpace(filePath) &&
        string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase);

    public ValueTask<TechnicalDocument> ParseAsync(DocumentParseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new ValueTask<TechnicalDocument>(Task.Run(
            () => Parse(request.FilePath, request.CancellationToken),
            request.CancellationToken));
    }

    private static TechnicalDocument Parse(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("No se encontró el documento PDF.", filePath);
        }

        string absolutePath = Path.GetFullPath(filePath);
        DocumentInfo info = new(
            Path.GetFileName(absolutePath),
            absolutePath,
            TechnicalDocumentKind.Pdf);
        TechnicalDocument result = new(info, MeasurementUnit.Millimeter);
        result.Metadata.Set("parser.id", "pdfpig-text-vector");
        result.Metadata.Set("pdf.coordinate-origin", "bottom-left");
        result.Metadata.Set("pdf.source-unit", "point");

        using PdfDocument document = PdfDocument.Open(absolutePath, PdfParsingOptionsFactory.CreateResilient());
        result.Metadata.Set("pdf.page-count", document.NumberOfPages.ToString());

        foreach (UglyToad.PdfPig.Content.Page sourcePage in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            double widthMillimeters = ToMillimeters(Convert.ToDouble(sourcePage.Width));
            double heightMillimeters = ToMillimeters(Convert.ToDouble(sourcePage.Height));
            DocumentPage page = new(
                sourcePage.Number,
                widthMillimeters,
                heightMillimeters,
                MeasurementUnit.Millimeter);

            page.Metadata.Set("pdf.original-width-points", Convert.ToString(sourcePage.Width, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            page.Metadata.Set("pdf.original-height-points", Convert.ToString(sourcePage.Height, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
            page.Metadata.Set("pdf.operation-count", sourcePage.Operations.Count.ToString());

            int textIndex = 0;
            foreach (UglyToad.PdfPig.Content.Word word in sourcePage.GetWords())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(word.Text))
                {
                    continue;
                }

                double left = ToMillimeters(word.BoundingBox.Left);
                double bottom = ToMillimeters(word.BoundingBox.Bottom);
                double width = Math.Max(ToMillimeters(word.BoundingBox.Width), double.Epsilon);
                double height = Math.Max(ToMillimeters(word.BoundingBox.Height), double.Epsilon);
                Bounds2D bounds = new(left, bottom, left + width, bottom + height);

                TextGraphic graphic = new(
                    $"page-{sourcePage.Number}-text-{++textIndex}",
                    word.Text,
                    new Point2D(left, bottom),
                    bounds,
                    height);
                graphic.Metadata.Set("source.format", "pdf");
                graphic.Metadata.Set("source.kind", "word");
                page.AddGraphic(graphic);
            }

            PdfVectorExtractionResult vectorResult = PdfVectorPathExtractor.Extract(
                sourcePage,
                page,
                cancellationToken);

            page.Metadata.Set("pdf.word-count", textIndex.ToString());
            page.Metadata.Set("pdf.path-count", vectorResult.PathCount.ToString());
            page.Metadata.Set("pdf.vector-graphic-count", vectorResult.GraphicCount.ToString());
            page.Metadata.Set("pdf.line-count", vectorResult.LineCount.ToString());
            page.Metadata.Set("pdf.polyline-count", vectorResult.PolylineCount.ToString());
            page.Metadata.Set("pdf.rectangle-count", vectorResult.RectangleCount.ToString());
            page.Metadata.Set("pdf.circle-count", vectorResult.CircleCount.ToString());
            page.Metadata.Set("pdf.bezier-count", vectorResult.BezierCount.ToString());
            result.AddPage(page);
        }

        int textGraphicCount = result.Pages.Sum(static page =>
            page.Graphics.Count(static graphic => graphic is TextGraphic));
        int vectorGraphicCount = result.Pages.Sum(static page =>
            page.Graphics.Count(static graphic => graphic is not TextGraphic));
        result.Metadata.Set("pdf.text-graphic-count", textGraphicCount.ToString());
        result.Metadata.Set("pdf.vector-graphic-count", vectorGraphicCount.ToString());
        result.Metadata.Set("pdf.graphic-count", (textGraphicCount + vectorGraphicCount).ToString());
        return result;
    }

    private static double ToMillimeters(double pdfPoints) =>
        UnitConverter.Convert(pdfPoints, MeasurementUnit.PdfPoint, MeasurementUnit.Millimeter);
}
