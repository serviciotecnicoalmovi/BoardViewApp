using System.Globalization;
using BoardView.Core.Contracts.Documents;
using BoardView.Core.Documents;
using BoardView.Core.Documents.Common;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Core.Graphics;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Convierte la extracción técnica de un PDF al modelo interno normalizado de BoardView.
/// Cada página se ubica en una superficie independiente y se divide en capas de texto,
/// vectores e imágenes para mantener una selección y visibilidad predecibles.
/// </summary>
public sealed class PdfBoardDocumentConverter : IBoardDocumentConverter
{
    private const double PageGapMillimeters = 10D;

    /// <inheritdoc />
    public string SourceFormatId => "pdf";

    /// <inheritdoc />
    public bool CanConvert(TechnicalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Info.Kind == TechnicalDocumentKind.Pdf;
    }

    /// <inheritdoc />
    public BoardDocument Convert(TechnicalDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!CanConvert(document))
        {
            throw new NotSupportedException(
                $"El conversor PDF no admite documentos de tipo '{document.Info.Kind}'.");
        }

        BoardDocument board = new(document.Info.Name, document.Info.SourcePath);
        board.Properties.Set("source.format", SourceFormatId);
        board.Properties.Set("source.kind", document.Info.Kind.ToString());
        board.Properties.Set("source.normalized-unit", document.NormalizedUnit.ToString());
        CopyMetadata(document.Metadata, board.Metadata);

        double unitScale = UnitConverter.Convert(
            1D,
            document.NormalizedUnit,
            MeasurementUnit.Millimeter);
        double pageOffsetY = 0D;
        foreach (DocumentPage page in document.Pages.OrderBy(static item => item.Number))
        {
            PageLayerSet layers = CreatePageLayers(board, page.Number);
            Point2D offset = new(0D, pageOffsetY);
            double pageWidth = page.Width * unitScale;
            double pageHeight = page.Height * unitScale;
            board.AddPage(new BoardDocumentPage(
                page.Number,
                pageWidth,
                pageHeight,
                offset,
                [layers.VectorLayerId, layers.TextLayerId, layers.ImageLayerId]));

            PdfGeometryNormalizer geometryNormalizer = new();
            foreach (GraphicObject sourceGraphic in page.Graphics)
            {
                GraphicObject graphic = geometryNormalizer.Normalize(sourceGraphic);
                BoardElement element = ConvertGraphic(
                    graphic,
                    page.Number,
                    layers,
                    offset,
                    unitScale);
                element.Properties.Set("source.format", SourceFormatId);
                element.Properties.Set("source.page", page.Number.ToString(CultureInfo.InvariantCulture));
                element.Properties.Set("source.graphic-id", graphic.Id);
                CopyMetadata(graphic.Metadata, element.Properties);
                board.AddElement(element);
            }

            CopyPageMetadata(page, board, page.Number, pageWidth, pageHeight);
            pageOffsetY += pageHeight + PageGapMillimeters;
        }

        board.Metadata.Set("core.page-count", board.Pages.Count.ToString(CultureInfo.InvariantCulture));
        board.Metadata.Set("core.layer-count", board.Layers.Count.ToString(CultureInfo.InvariantCulture));
        board.Metadata.Set("core.element-count", board.Elements.Count.ToString(CultureInfo.InvariantCulture));

        var validation = board.Validate();
        if (!validation.IsValid)
        {
            string details = string.Join(
                Environment.NewLine,
                validation.Issues.Select(static issue => $"{issue.Code}: {issue.Message}"));
            throw new InvalidDataException(
                $"El documento PDF convertido no superó la validación del modelo interno.{Environment.NewLine}{details}");
        }

        return board;
    }

    private static PageLayerSet CreatePageLayers(BoardDocument board, int pageNumber)
    {
        string prefix = $"pdf-page-{pageNumber}";
        string vectorLayerId = $"{prefix}-vectors";
        string textLayerId = $"{prefix}-text";
        string imageLayerId = $"{prefix}-images";
        int orderBase = (pageNumber - 1) * 10;

        BoardLayer vectorLayer = new(
            vectorLayerId,
            $"Página {pageNumber} · Vectores",
            LayerType.Document,
            BoardSide.None,
            orderBase + 1);
        vectorLayer.Properties.Set("source.format", "pdf");
        vectorLayer.Properties.Set("source.page", pageNumber.ToString(CultureInfo.InvariantCulture));
        vectorLayer.Properties.Set("content.kind", "vector");
        board.AddLayer(vectorLayer);

        BoardLayer textLayer = new(
            textLayerId,
            $"Página {pageNumber} · Texto",
            LayerType.Document,
            BoardSide.None,
            orderBase + 2);
        textLayer.Properties.Set("source.format", "pdf");
        textLayer.Properties.Set("source.page", pageNumber.ToString(CultureInfo.InvariantCulture));
        textLayer.Properties.Set("content.kind", "text");
        board.AddLayer(textLayer);

        BoardLayer imageLayer = new(
            imageLayerId,
            $"Página {pageNumber} · Imágenes",
            LayerType.Document,
            BoardSide.None,
            orderBase + 3);
        imageLayer.Properties.Set("source.format", "pdf");
        imageLayer.Properties.Set("source.page", pageNumber.ToString(CultureInfo.InvariantCulture));
        imageLayer.Properties.Set("content.kind", "image");
        board.AddLayer(imageLayer);

        return new PageLayerSet(vectorLayerId, textLayerId, imageLayerId);
    }

    private static BoardElement ConvertGraphic(
        GraphicObject graphic,
        int pageNumber,
        PageLayerSet layers,
        Point2D offset,
        double unitScale)
    {
        ArgumentNullException.ThrowIfNull(graphic);
        string id = $"pdf-p{pageNumber}-{graphic.Id}";

        return graphic switch
        {
            TextGraphic text => new TextElement(
                id,
                layers.TextLayerId,
                text.Text,
                Translate(text.Origin, offset, unitScale),
                text.FontSize * unitScale,
                text.RotationDegrees),

            LineGraphic line => new VectorLineElement(
                id,
                layers.VectorLayerId,
                Translate(line.Start, offset, unitScale),
                Translate(line.End, offset, unitScale),
                line.Width * unitScale),

            PolylineGraphic polyline => new VectorPolylineElement(
                id,
                layers.VectorLayerId,
                polyline.Points.Select(point => Translate(point, offset, unitScale)),
                polyline.Width * unitScale,
                polyline.IsClosed),

            BezierGraphic bezier => new VectorBezierElement(
                id,
                layers.VectorLayerId,
                Translate(bezier.Start, offset, unitScale),
                Translate(bezier.Control1, offset, unitScale),
                Translate(bezier.Control2, offset, unitScale),
                Translate(bezier.End, offset, unitScale),
                bezier.Width * unitScale),

            CircleGraphic circle => new VectorEllipseElement(
                id,
                layers.VectorLayerId,
                Translate(circle.Center, offset, unitScale),
                circle.Radius * unitScale,
                circle.Radius * unitScale,
                circle.StrokeWidth * unitScale,
                circle.IsFilled),

            RectangleGraphic rectangle => new VectorRectangleElement(
                id,
                layers.VectorLayerId,
                Translate(rectangle.Rectangle, offset, unitScale),
                rectangle.StrokeWidth * unitScale,
                rectangle.IsFilled),

            ImageGraphic image => new RasterImageElement(
                id,
                layers.ImageLayerId,
                Translate(image.Bounds, offset, unitScale),
                image.ResourceId,
                image.MediaType),

            _ => throw new NotSupportedException(
                $"No existe una conversión registrada para el gráfico '{graphic.GetType().FullName}'."),
        };
    }

    private static Point2D Translate(Point2D point, Point2D offset, double unitScale) =>
        new((point.X * unitScale) + offset.X, (point.Y * unitScale) + offset.Y);

    private static Bounds2D Translate(Bounds2D bounds, Point2D offset, double unitScale) =>
        new(
            (bounds.Left * unitScale) + offset.X,
            (bounds.Top * unitScale) + offset.Y,
            (bounds.Right * unitScale) + offset.X,
            (bounds.Bottom * unitScale) + offset.Y);

    private static void CopyMetadata(DocumentMetadata source, DocumentMetadata destination)
    {
        foreach ((string key, string value) in source.Values)
        {
            destination.Set(key, value);
        }
    }

    private static void CopyMetadata(DocumentMetadata source, BoardView.Core.Model.PropertyBag destination)
    {
        foreach ((string key, string value) in source.Values)
        {
            destination.Set(key, value);
        }
    }

    private static void CopyPageMetadata(
        DocumentPage page,
        BoardDocument board,
        int pageNumber,
        double widthMillimeters,
        double heightMillimeters)
    {
        string prefix = $"page.{pageNumber}.";
        board.Metadata.Set(prefix + "width-mm", widthMillimeters.ToString(CultureInfo.InvariantCulture));
        board.Metadata.Set(prefix + "height-mm", heightMillimeters.ToString(CultureInfo.InvariantCulture));
        board.Metadata.Set(prefix + "graphic-count", page.Graphics.Count.ToString(CultureInfo.InvariantCulture));
        foreach ((string key, string value) in page.Metadata.Values)
        {
            board.Metadata.Set(prefix + key, value);
        }
    }

    private sealed record PageLayerSet(
        string VectorLayerId,
        string TextLayerId,
        string ImageLayerId);
}
