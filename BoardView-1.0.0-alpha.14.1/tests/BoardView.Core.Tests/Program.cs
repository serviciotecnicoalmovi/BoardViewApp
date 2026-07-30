using BoardView.Core.Documents.Common;
using BoardView.Core.Geometry;
using BoardView.Core.Graphics;
using BoardView.Core.Pdf;
using BoardView.Core.Search;
using BoardView.Core.Spatial;
using BoardView.Core.Tools;
using BoardView.Formats.Pdf;
using BoardView.Rendering.Viewport;
using BoardView.Core.Recognition;
using BoardView.Core.GeometryDatabase;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.GeometryKernel;
using BoardView.GeometryKernel.Graph;
using BoardView.SemanticKernel;
using BoardView.Recognition;
using BoardView.Recognition.Footprints;
using System.Windows;

namespace BoardView.Core.Tests;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyBounds();
            VerifyUnits();
            VerifyTransform();
            VerifyDocumentValidation();
            VerifyBezierGraphic();
            VerifyPdfInspection();
            VerifySpatialIndex();
            VerifyMeasurement();
            VerifySearch();
            VerifyCoreEngine();
            VerifySpatialIndexMutation();
            VerifyPdfToCoreIntegration();
            VerifyAdvancedSpatialQueries();
            VerifyIncrementalDocumentIndex();
            VerifySpatialStatistics();
            VerifyViewportCamera();
            VerifyGeometryClassification();
            VerifyGeometryDatabase();
            VerifyPdfGeometryNormalizer();
            VerifyPdfLinearContourAssembler();
            VerifyPadDetection();
            VerifyGeometryKernel();
            VerifySemanticKernel();
            VerifyRecognitionEngine();
            VerifyFootprintTemplateEngine();
            VerifyPdfReferenceSearch();
            Console.WriteLine("BoardView.Core: todas las verificaciones finalizaron correctamente.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }


    private static void VerifyRecognitionEngine()
    {
        BoardDocument board = new("Recognition", "recognition.pcb");
        board.AddLayer(new BoardLayer("top", "Top", LayerType.Copper, BoardSide.Top, 1));
        for (int i = 0; i < 4; i++)
        {
            board.AddElement(new PadElement($"p-top-{i}", "top", new Point2D(i * 1.27D, 0D), 0.6D, 1.2D, PadShape.Rectangle));
            board.AddElement(new PadElement($"p-bottom-{i}", "top", new Point2D(i * 1.27D, 4D), 0.6D, 1.2D, PadShape.Rectangle));
        }
        board.AddElement(new TextElement("ref", "top", "U1", new Point2D(1.9D, 2D), 1D));
        RecognitionResult low = new PadDetectionEngine().Analyze(board);
        SemanticAnalysisResult semantic = new SemanticKernelEngine().Analyze(board, low);
        RecognitionAnalysis result = new RecognitionEngine().Analyze(board, low, semantic);
        if (result.Components.Count == 0) throw new InvalidOperationException("Recognition Engine no construyó componentes.");
        if (result.Components[0].Footprint.Metrics.PadCount < 2) throw new InvalidOperationException("Footprint inválido.");
    }

    private static void VerifySemanticKernel()
    {
        BoardDocument board = new("Semantic", "semantic.pcb");
        board.AddLayer(new BoardLayer("top", "Top Copper", LayerType.Copper, BoardSide.Top, 1));
        board.AddLayer(new BoardLayer("doc", "Document", LayerType.Document, BoardSide.None, 2));
        board.AddElement(new PadElement("pad-1", "top", new Point2D(2D, 2D), 1D, 1D, PadShape.Rectangle));
        board.AddElement(new ViaElement("via-1", "top", new Point2D(4D, 2D), 1D, 0.4D));
        board.AddElement(new TextElement("text-1", "doc", "U1", new Point2D(1D, 6D), 1D));

        RecognitionResult recognition = new PadDetectionEngine().Analyze(board);
        SemanticAnalysisResult semantic = new SemanticKernelEngine().Analyze(board, recognition);

        AssertEqual(1D, semantic.Count(PrimitiveSemantic.Pad), "Semantic Kernel: pads");
        AssertEqual(1D, semantic.Count(PrimitiveSemantic.Via), "Semantic Kernel: vías");
        AssertEqual(1D, semantic.Count(PrimitiveSemantic.Text), "Semantic Kernel: textos");
    }

    private static void VerifyBounds()
    {
        Bounds2D bounds = Bounds2D.FromPoints([new Point2D(5D, 9D), new Point2D(-2D, 3D)]);
        AssertEqual(-2D, bounds.Left, nameof(bounds.Left));
        AssertEqual(3D, bounds.Top, nameof(bounds.Top));
        AssertEqual(5D, bounds.Right, nameof(bounds.Right));
        AssertEqual(9D, bounds.Bottom, nameof(bounds.Bottom));
    }

    private static void VerifyUnits()
    {
        double millimeters = UnitConverter.Convert(1D, MeasurementUnit.Inch, MeasurementUnit.Millimeter);
        AssertEqual(25.4D, millimeters, "Conversión pulgada-milímetro");

        double points = UnitConverter.Convert(25.4D, MeasurementUnit.Millimeter, MeasurementUnit.PdfPoint);
        AssertEqual(72D, points, "Conversión milímetro-punto PDF");
    }

    private static void VerifyTransform()
    {
        Matrix2D matrix = Matrix2D.CreateTranslation(10D, 20D)
            .Append(Matrix2D.CreateScale(2D, 3D));
        Point2D result = matrix.Transform(new Point2D(1D, 1D));
        AssertEqual(22D, result.X, "Transformación X");
        AssertEqual(63D, result.Y, "Transformación Y");
    }

    private static void VerifyDocumentValidation()
    {
        DocumentInfo info = new("Prueba", "sample.pdf", TechnicalDocumentKind.Pdf);
        TechnicalDocument document = new(info);
        DocumentPage page = new(1, 210D, 297D, MeasurementUnit.Millimeter);
        page.AddGraphic(new LineGraphic("line-1", Point2D.Zero, new Point2D(10D, 10D), 0.2D));
        document.AddPage(page);

        AssertEqual(1D, document.Pages.Count, "Cantidad de páginas");
        AssertEqual(1D, document.Pages[0].Graphics.Count, "Cantidad de gráficos");
    }

    private static void VerifyBezierGraphic()
    {
        BezierGraphic bezier = new(
            "bezier-1",
            new Point2D(0D, 0D),
            new Point2D(2D, 5D),
            new Point2D(8D, 5D),
            new Point2D(10D, 0D),
            0.2D);

        AssertEqual(10.2D, bezier.Bounds.Width, "Ancho de límites Bézier");
        AssertEqual(5.2D, bezier.Bounds.Height, "Alto de límites Bézier");
    }


    private static void VerifyPdfInspection()
    {
        PdfDocumentInspection inspection = new(
            "sample.pdf",
            PdfDocumentType.XfaDynamic,
            0,
            0,
            0,
            true,
            true,
            false,
            "Documento XFA de prueba.");

        if (inspection.CanUseIntegratedViewer || inspection.CanAnalyzeTechnically)
        {
            throw new InvalidOperationException("Un PDF XFA dinámico no debe enviarse al visor ni al analizador técnico estándar.");
        }
    }


    private static void VerifySpatialIndex()
    {
        SpatialIndex<string> index = new(5D);
        index.Add("A", new Bounds2D(0D, 0D, 2D, 2D));
        if (!index.Query(new Point2D(1D, 1D)).Contains("A")) throw new InvalidOperationException("El índice espacial no devolvió el elemento esperado.");
    }

    private static void VerifyMeasurement()
    {
        MeasurementResult result = new MeasurementTool().Measure(Point2D.Zero, new Point2D(3D, 4D));
        AssertEqual(5D, result.Distance, "Distancia de medición");
    }

    private static void VerifySearch()
    {
        BoardView.Core.Documents.BoardDocument board = new("Test", "test.pcb");
        board.AddLayer(new BoardView.Core.Documents.BoardLayer("top", "Top", BoardView.Core.Documents.LayerType.Copper, BoardView.Core.Documents.BoardSide.Top, 1));
        board.AddComponent(new BoardView.Core.Documents.BoardComponent("c1", "U1", "MCU", new Point2D(1D, 2D), 0D, BoardView.Core.Documents.BoardSide.Top));
        IReadOnlyList<SearchHit> hits = new DocumentSearchEngine().Search(board, new SearchRequest("U1", SearchField.Reference));
        AssertEqual(1D, hits.Count, "Resultados de búsqueda");
    }


    private static void VerifyCoreEngine()
    {
        var board = new BoardView.Core.Documents.BoardDocument("Core", "core.pcb");
        board.AddLayer(new BoardView.Core.Documents.BoardLayer("top", "Top", BoardView.Core.Documents.LayerType.Copper, BoardView.Core.Documents.BoardSide.Top, 1));
        board.AddNet(new BoardView.Core.Documents.BoardNet("gnd", "GND"));
        board.AddComponent(new BoardView.Core.Documents.BoardComponent("u1", "U1", "MCU", new Point2D(5D, 5D), 0D, BoardView.Core.Documents.BoardSide.Top));
        var pad = new BoardView.Core.Elements.DrillHoleElement("p1", "top", new Point2D(5D, 5D), 1D, true, "gnd", "u1");
        board.AddElement(pad);

        if (!board.TryGetElement("p1", out _))
        {
            throw new InvalidOperationException("El índice por identificador no devolvió el elemento.");
        }

        if (!board.Query(new Point2D(5D, 5D)).Contains(pad))
        {
            throw new InvalidOperationException("La consulta espacial del documento no devolvió el elemento.");
        }

        if (!board.Validate().IsValid)
        {
            throw new InvalidOperationException("El documento válido fue rechazado por el validador.");
        }
    }

    private static void VerifySpatialIndexMutation()
    {
        SpatialIndex<string> index = new(2D);
        index.Add("A", new Bounds2D(0D, 0D, 1D, 1D));
        index.Update("A", new Bounds2D(10D, 10D, 11D, 11D));
        if (index.Query(new Point2D(0.5D, 0.5D)).Contains("A"))
        {
            throw new InvalidOperationException("El índice mantuvo una ubicación obsoleta después de actualizar.");
        }

        if (!index.Remove("A") || index.Count != 0)
        {
            throw new InvalidOperationException("El índice no eliminó correctamente el elemento.");
        }
    }

    private static void VerifyPdfToCoreIntegration()
    {
        DocumentInfo info = new("Integración PDF", "integration.pdf", TechnicalDocumentKind.Pdf);
        TechnicalDocument source = new(info);
        source.Metadata.Set("author", "BoardView Tests");

        DocumentPage firstPage = new(1, 100D, 50D, MeasurementUnit.Millimeter);
        firstPage.Metadata.Set("pdf.word-count", "1");
        firstPage.AddGraphic(new TextGraphic(
            "text-1",
            "U1",
            new Point2D(10D, 10D),
            new Bounds2D(10D, 8D, 14D, 10D),
            2D));
        firstPage.AddGraphic(new LineGraphic(
            "line-1",
            new Point2D(0D, 0D),
            new Point2D(20D, 20D),
            0.2D));
        firstPage.AddGraphic(new CircleGraphic(
            "circle-1",
            new Point2D(30D, 20D),
            3D,
            0.2D));
        source.AddPage(firstPage);

        DocumentPage secondPage = new(2, 80D, 40D, MeasurementUnit.Millimeter);
        secondPage.AddGraphic(new RectangleGraphic(
            "rectangle-1",
            new Bounds2D(5D, 5D, 25D, 15D),
            0.1D));
        secondPage.AddGraphic(new BezierGraphic(
            "bezier-1",
            new Point2D(0D, 0D),
            new Point2D(3D, 4D),
            new Point2D(7D, 4D),
            new Point2D(10D, 0D),
            0.1D));
        source.AddPage(secondPage);

        var converter = new PdfBoardDocumentConverter();
        BoardView.Core.Documents.BoardDocument board = converter.Convert(source);

        AssertEqual(2D, board.Pages.Count, "Páginas convertidas");
        AssertEqual(6D, board.Layers.Count, "Capas PDF normalizadas");
        AssertEqual(5D, board.Elements.Count, "Elementos PDF normalizados");
        AssertEqual(60D, board.Pages[1].Offset.Y, "Separación de páginas");

        if (!board.Validate().IsValid)
        {
            throw new InvalidOperationException("La integración PDF produjo un BoardDocument inválido.");
        }

        if (!board.Metadata.TryGetValue("author", out string? author) || author != "BoardView Tests")
        {
            throw new InvalidOperationException("Los metadatos del PDF no se conservaron.");
        }

        if (!board.Query(new Point2D(10D, 10D), 0.5D).Any())
        {
            throw new InvalidOperationException("El índice espacial no contiene la geometría convertida de la primera página.");
        }

        if (!board.Query(new Point2D(5D, 65D), 0.5D).Any())
        {
            throw new InvalidOperationException("La geometría de la segunda página no fue desplazada correctamente.");
        }
    }


    private static void VerifyAdvancedSpatialQueries()
    {
        var board = new BoardView.Core.Documents.BoardDocument("Spatial", "spatial.pcb");
        board.AddLayer(new BoardView.Core.Documents.BoardLayer("top", "Top", BoardView.Core.Documents.LayerType.Copper, BoardView.Core.Documents.BoardSide.Top, 1));
        board.AddLayer(new BoardView.Core.Documents.BoardLayer("bottom", "Bottom", BoardView.Core.Documents.LayerType.Copper, BoardView.Core.Documents.BoardSide.Bottom, 2));
        board.AddNet(new BoardView.Core.Documents.BoardNet("gnd", "GND"));
        board.AddNet(new BoardView.Core.Documents.BoardNet("vcc", "VCC"));
        board.AddComponent(new BoardView.Core.Documents.BoardComponent("u1", "U1", "MCU", Point2D.Zero, 0D, BoardView.Core.Documents.BoardSide.Top));

        var topPad = new BoardView.Core.Elements.DrillHoleElement("top-pad", "top", new Point2D(2D, 2D), 1D, true, "gnd", "u1");
        var bottomPad = new BoardView.Core.Elements.DrillHoleElement("bottom-pad", "bottom", new Point2D(4D, 2D), 1D, true, "vcc");
        board.AddElement(topPad);
        board.AddElement(bottomPad);

        SpatialQueryResult<BoardView.Core.Elements.BoardElement> result = board.Query(
            BoardElementQuery.InArea(new Bounds2D(0D, 0D, 10D, 10D)) with
            {
                LayerIds = new HashSet<string>(StringComparer.Ordinal) { "top" },
                NetIds = new HashSet<string>(StringComparer.Ordinal) { "gnd" },
                ComponentIds = new HashSet<string>(StringComparer.Ordinal) { "u1" },
                ElementTypes = new HashSet<Type> { typeof(BoardView.Core.Elements.DrillHoleElement) },
            });

        AssertEqual(1D, result.Hits.Count, "Consulta espacial filtrada");
        if (!ReferenceEquals(topPad, result.Hits[0].Item))
        {
            throw new InvalidOperationException("La consulta espacial filtrada devolvió un elemento incorrecto.");
        }

        SpatialQueryResult<BoardView.Core.Elements.BoardElement> nearest = board.Query(
            BoardElementQuery.Near(new Point2D(2D, 2D), 10D) with { MaximumResults = 1 });
        if (!ReferenceEquals(topPad, nearest.Hits[0].Item))
        {
            throw new InvalidOperationException("La consulta de proximidad no ordenó por distancia.");
        }
    }

    private static void VerifyIncrementalDocumentIndex()
    {
        var board = new BoardView.Core.Documents.BoardDocument("Incremental", "incremental.pcb");
        board.AddLayer(new BoardView.Core.Documents.BoardLayer("top", "Top", BoardView.Core.Documents.LayerType.Copper, BoardView.Core.Documents.BoardSide.Top, 1));

        _ = board.SpatialIndex.Count;
        var element = new BoardView.Core.Elements.DrillHoleElement("hole", "top", Point2D.Zero, 1D, false);
        board.AddElement(element);
        AssertEqual(1D, board.SpatialIndex.Count, "Inserción incremental");

        board.UpdateElementBounds("hole", new Bounds2D(20D, 20D, 22D, 22D));
        if (board.Query(Point2D.Zero, 1D).Contains(element))
        {
            throw new InvalidOperationException("La actualización incremental conservó límites obsoletos.");
        }

        if (!board.Query(new Point2D(21D, 21D)).Contains(element))
        {
            throw new InvalidOperationException("La actualización incremental no registró los límites nuevos.");
        }

        if (!board.RemoveElement("hole") || board.SpatialIndex.Count != 0)
        {
            throw new InvalidOperationException("La eliminación incremental no sincronizó el índice.");
        }
    }

    private static void VerifySpatialStatistics()
    {
        using SpatialIndex<int> index = new(4D);
        index.AddRange(Enumerable.Range(0, 100).Select(value =>
            (value, new Bounds2D(value, value, value + 1D, value + 1D))));
        _ = index.Query(new Bounds2D(0D, 0D, 20D, 20D));
        SpatialStatistics statistics = index.GetStatistics();

        AssertEqual(100D, statistics.ItemCount, "Estadísticas: elementos");
        AssertEqual(1D, statistics.QueryCount, "Estadísticas: consultas");
        if (statistics.CellCount == 0 || statistics.CandidateCount == 0 || statistics.HitCount == 0)
        {
            throw new InvalidOperationException("Las estadísticas espaciales no registraron la actividad esperada.");
        }
    }


    private static void VerifyViewportCamera()
    {
        ViewportCamera camera = new();
        Bounds2D bounds = new(0D, 0D, 100D, 50D);
        ViewportTransform initial = camera.CreateTransform(bounds, new Size(1000D, 600D), 50D);
        Point2D source = new(25D, 20D);
        Point screen = initial.ToScreen(source);
        Point2D restored = initial.ToWorld(screen);
        AssertEqual(source.X, restored.X, "Transformación inversa de cámara X");
        AssertEqual(source.Y, restored.Y, "Transformación inversa de cámara Y");

        camera.ZoomAt(screen, 2D, bounds, new Size(1000D, 600D), 50D);
        ViewportTransform zoomed = camera.CreateTransform(bounds, new Size(1000D, 600D), 50D);
        Point anchored = zoomed.ToScreen(source);
        AssertEqual(screen.X, anchored.X, "Anclaje de zoom X");
        AssertEqual(screen.Y, anchored.Y, "Anclaje de zoom Y");

        camera.SetPan(camera.Pan + new Vector(40D, -25D));
        ViewportTransform panned = camera.CreateTransform(bounds, new Size(1000D, 600D), 50D);
        Point moved = panned.ToScreen(source);
        AssertEqual(40D, moved.X - zoomed.ToScreen(source).X, "Paneo X");
        AssertEqual(-25D, moved.Y - zoomed.ToScreen(source).Y, "Paneo Y");
    }

    private static void VerifyGeometryClassification()
    {
        BoardDocument board = new("Geometry classification", "classification.pdf");
        board.AddLayer(new BoardLayer("vector", "Vector", LayerType.Document, BoardSide.None, 1));
        board.AddElement(new VectorPolylineElement(
            "outline-pad-1",
            "vector",
            [
                new Point2D(10D, 10D),
                new Point2D(12D, 10D),
                new Point2D(12D, 11D),
                new Point2D(10D, 11D),
                new Point2D(10D, 10D),
            ],
            0.05D,
            true));
        board.AddElement(new VectorPolylineElement(
            "outline-pad-2",
            "vector",
            [
                new Point2D(14D, 10D),
                new Point2D(16D, 10D),
                new Point2D(16D, 11D),
                new Point2D(14D, 11D),
                new Point2D(14D, 10D),
            ],
            0.05D,
            true));
        board.AddElement(new VectorLineElement(
            "extent", "vector", Point2D.Zero, new Point2D(100D, 100D), 0.1D));

        GeometryClassificationResult classification = new GeometryClassificationEngine().Analyze(board);
        AssertEqual(2D, classification.Primitives.Count, "Primitivas rectangulares clasificadas");
        if (classification.Primitives.Any(static primitive =>
                primitive.Kind != GeometryPrimitiveKind.OutlineRectangle ||
                primitive.RepetitionCount < 2 ||
                primitive.AlignedNeighborCount < 1))
        {
            throw new InvalidOperationException(
                "Las primitivas repetidas y alineadas deben clasificarse como rectángulos candidatos.");
        }

        RecognitionResult recognition = new PadDetectionEngine().Analyze(board);
        AssertEqual(2D, recognition.Pads.Count, "Pads delineados detectados por patrón");
        AssertEqual(1D, recognition.Footprints.Count, "Footprint delineado inferido");
    }


    private static void VerifyGeometryDatabase()
    {
        BoardDocument board = new("Geometry database", "geometry-database.pdf");
        board.AddLayer(new BoardLayer("vector", "Vector", LayerType.Document, BoardSide.None, 1));
        board.AddElement(new VectorRectangleElement(
            "rectangle", "vector", new Bounds2D(1D, 2D, 4D, 6D), 0.1D, true));
        board.AddElement(new VectorLineElement(
            "line", "vector", Point2D.Zero, new Point2D(10D, 10D), 0.1D));
        board.AddElement(new TextElement(
            "text", "vector", "R1", new Point2D(5D, 5D), 1D));

        GeometryDatabaseSnapshot database = new GeometryDatabaseBuilder().Build(board);
        AssertEqual(3D, database.TotalCount, "Registros de la base geométrica");
        AssertEqual(1D, database.Count(GeometryDatabasePrimitiveKind.Rectangle), "Rectángulos geométricos");
        AssertEqual(1D, database.Count(GeometryDatabasePrimitiveKind.Line), "Líneas geométricas");
        AssertEqual(1D, database.Count(GeometryDatabasePrimitiveKind.Text), "Textos geométricos");
    }

    private static void VerifyPdfGeometryNormalizer()
    {
        PdfGeometryNormalizer normalizer = new();
        Point2D[] subdividedRectangle =
        [
            new Point2D(0D, 0D),
            new Point2D(5D, 0D),
            new Point2D(10D, 0D),
            new Point2D(10D, 4D),
            new Point2D(10D, 8D),
            new Point2D(5D, 8D),
            new Point2D(0D, 8D),
            new Point2D(0D, 4D),
            new Point2D(0D, 0D),
        ];

        if (!normalizer.TryCreateRectangle(
                "normalized-rectangle",
                subdividedRectangle,
                0.2D,
                isFilled: true,
                isClosed: true,
                out RectangleGraphic rectangle))
        {
            throw new InvalidOperationException(
                "El normalizador PDF no reconoció un rectángulo con vértices colineales intermedios.");
        }

        AssertEqual(10D, rectangle.Rectangle.Width, "Ancho del rectángulo PDF normalizado");
        AssertEqual(8D, rectangle.Rectangle.Height, "Alto del rectángulo PDF normalizado");
        if (!rectangle.IsFilled)
        {
            throw new InvalidOperationException("El normalizador PDF perdió el estado de relleno.");
        }

        PolylineGraphic source = new(
            "polyline-rectangle",
            subdividedRectangle.Take(subdividedRectangle.Length - 1),
            0.2D,
            isClosed: true);
        source.Metadata.Set("pdf.is-filled", "True");
        GraphicObject normalized = normalizer.Normalize(source);
        if (normalized is not RectangleGraphic normalizedRectangle || !normalizedRectangle.IsFilled)
        {
            throw new InvalidOperationException(
                "La normalización de una PolylineGraphic cerrada no produjo un RectangleGraphic relleno.");
        }
    }

    private static void VerifyPdfLinearContourAssembler()
    {
        PdfLinearContourAssembler assembler = new(0.001D);
        PdfLinearContour[] segments =
        [
            new([new Point2D(0D, 0D), new Point2D(10D, 0D)], false),
            new([new Point2D(10D, 8D), new Point2D(0D, 8D)], false),
            new([new Point2D(10D, 0D), new Point2D(10D, 8D)], false),
            new([new Point2D(0D, 8D), new Point2D(0D, 0D)], false),
        ];

        IReadOnlyList<PdfAssembledContour> result = assembler.Assemble(segments);
        if (result.Count != 1 || !result[0].IsClosed)
        {
            throw new InvalidOperationException(
                "El ensamblador PDF no reconstruyó cuatro segmentos independientes como un contorno cerrado.");
        }

        PdfGeometryNormalizer normalizer = new();
        if (!normalizer.TryCreateRectangle(
                "assembled-rectangle",
                result[0].Points,
                0.1D,
                isFilled: false,
                isClosed: result[0].IsClosed,
                out RectangleGraphic rectangle))
        {
            throw new InvalidOperationException(
                "El contorno reconstruido no pudo clasificarse como rectángulo.");
        }

        AssertEqual(10D, rectangle.Rectangle.Width, "Ancho del rectángulo ensamblado");
        AssertEqual(8D, rectangle.Rectangle.Height, "Alto del rectángulo ensamblado");
    }

    private static void VerifyPadDetection()
    {
        BoardDocument board = new("Pad detection", "pad-detection.pdf");
        board.AddLayer(new BoardLayer("vector", "Vector", LayerType.Document, BoardSide.None, 1));
        board.AddElement(new VectorRectangleElement(
            "pad-geometry-1", "vector", new Bounds2D(9D, 10D, 11D, 12D), 0.1D, true));
        board.AddElement(new VectorRectangleElement(
            "pad-geometry-2", "vector", new Bounds2D(15D, 10D, 17D, 12D), 0.1D, true));
        board.AddElement(new DrillHoleElement(
            "hole-geometry", "vector", new Point2D(30D, 30D), 1.5D, false));
        board.AddElement(new VectorLineElement(
            "extent", "vector", Point2D.Zero, new Point2D(100D, 100D), 0.1D));

        RecognitionResult result = new PadDetectionEngine().Analyze(board);
        AssertEqual(4D, result.GeometryDatabase.TotalCount, "Elementos preservados en la base geométrica");
        AssertEqual(2D, result.Pads.Count, "Pads detectados");
        AssertEqual(1D, result.Holes.Count, "Agujeros detectados");
        AssertEqual(1D, result.Footprints.Count, "Footprints inferidos");
        AssertEqual(2D, result.Footprints[0].PadIds.Count, "Pads del footprint");
        AssertEqual(3D, result.Diagnostics.ClassifiedPrimitiveCount, "Primitivas diagnosticadas");
        AssertEqual(3D, result.Diagnostics.CandidateCount, "Candidatos diagnosticados");
        AssertEqual(2D, result.Diagnostics.AcceptedBeforeDeduplication, "Candidatos aceptados");
        AssertEqual(1D, result.Diagnostics.CountRejected(PadCandidateRejectionReason.UnsupportedGeometry),
            "Geometría no soportada diagnosticada");
        if (result.Footprints.Any(static footprint => footprint.PadIds.Count < 2))
        {
            throw new InvalidOperationException("No se permiten footprints sin dos pads como mínimo.");
        }
    }


    private static void VerifyGeometryKernel()
    {
        GeometrySegment[] segments =
        [
            new("a", new Point2D(0D, 0D), new Point2D(10D, 0D), "default"),
            new("b", new Point2D(10D, 5D), new Point2D(0D, 5D), "default"),
            new("c", new Point2D(10D, 0D), new Point2D(10D, 5D), "default"),
            new("d", new Point2D(0D, 5D), new Point2D(0D, 0D), "default"),
            new("open", new Point2D(20D, 0D), new Point2D(25D, 0D), "default"),
        ];

        GeometryKernelResult result = new PageGeometryKernel().Build(segments);
        AssertEqual(1D, result.Rectangles.Count, "Rectángulos reconstruidos por Geometry Kernel");
        AssertEqual(1D, result.RemainingSegments.Count, "Segmentos no consumidos por Geometry Kernel");
        AssertEqual(4D, result.Diagnostics.ConsumedSegmentCount, "Segmentos consumidos por Geometry Kernel");
    }


    /// <summary>Verifica que la biblioteca de plantillas sea utilizable sin archivos externos.</summary>
    private static void VerifyFootprintTemplateEngine()
    {
        string missingDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"boardview-no-templates-{Guid.NewGuid():N}");
        var library = new BoardView.Recognition.Templates.JsonFootprintTemplateLibrary(missingDirectory);
        if (library.Templates.Count < 5)
        {
            throw new InvalidOperationException("La biblioteca de respaldo no contiene plantillas suficientes.");
        }

        var template = library.Templates.FirstOrDefault(item => item.Name == "CHIP-2");
        if (template is null || template.MinPads != 2)
        {
            throw new InvalidOperationException("La plantilla CHIP-2 no está disponible.");
        }
    }


    private static void VerifyPdfReferenceSearch()
    {
        PdfDocumentIndex index = new(
            "reference-test.pdf",
            [
                new PdfPage(1, 100D, 100D,
                    [
                        new PdfWord("C3303_E", 10D, 10D, 5D, 2D),
                        new PdfWord("R605", 20D, 10D, 5D, 2D),
                    ]),
                new PdfPage(2, 100D, 100D,
                    [
                        new PdfWord("C3303", 10D, 10D, 5D, 2D),
                        new PdfWord("C33030", 20D, 10D, 5D, 2D),
                    ]),
            ]);

        PdfReferenceSearchService service = new();
        IReadOnlyList<PdfReferenceMatch> matches = service.Search(index, "C3303");

        AssertEqual(2D, matches.Count, "Búsqueda de referencia por páginas");
        AssertEqual(1D, matches[0].Occurrences, "Coincidencias de referencia con sufijo");
        AssertEqual(1D, matches[1].Occurrences, "Coincidencias exactas sin falsos positivos");

        PdfDocumentIndex fragmentedIndex = new(
            "fragmented-reference-test.pdf",
            [
                new PdfPage(1, 100D, 100D,
                    [
                        new PdfWord("L", 10D, 10D, 1D, 2D),
                        new PdfWord("305", 11.2D, 10D, 3D, 2D),
                        new PdfWord("_E", 14.4D, 10D, 2D, 2D),
                    ]),
            ]);

        IReadOnlyList<PdfReferenceMatch> fragmentedMatches =
            service.Search(fragmentedIndex, "L305_E");
        AssertEqual(1D, fragmentedMatches.Count, "Referencia PDFium fragmentada por tokens");
        AssertEqual(1D, fragmentedMatches[0].Occurrences, "Conteo de referencia PDFium fragmentada");
    }

    private static void AssertEqual(double expected, double actual, string name)
    {
        if (Math.Abs(expected - actual) > 0.0000001D)
        {
            throw new InvalidOperationException(
                $"Verificación fallida ({name}). Esperado: {expected}; actual: {actual}.");
        }
    }
}
