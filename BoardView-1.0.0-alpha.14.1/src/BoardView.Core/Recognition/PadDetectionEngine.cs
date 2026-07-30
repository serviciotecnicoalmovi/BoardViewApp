using System.Diagnostics;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Core.GeometryDatabase;

namespace BoardView.Core.Recognition;

/// <summary>
/// Detecta primitivas electrónicas de bajo nivel mediante reglas geométricas conservadoras.
/// El motor no interpreta textos, no crea componentes y no modifica el documento original.
/// </summary>
public sealed class PadDetectionEngine : IPadDetectionEngine
{
    private readonly IGeometryClassificationEngine geometryClassificationEngine;
    private readonly IGeometryDatabaseBuilder geometryDatabaseBuilder;

    /// <summary>Inicializa el detector con el clasificador geométrico predeterminado.</summary>
    public PadDetectionEngine()
        : this(new GeometryClassificationEngine(), new GeometryDatabaseBuilder())
    {
    }

    /// <summary>Inicializa el detector con un clasificador geométrico explícito.</summary>
    public PadDetectionEngine(IGeometryClassificationEngine geometryClassificationEngine)
        : this(geometryClassificationEngine, new GeometryDatabaseBuilder())
    {
    }

    /// <summary>Inicializa el detector con los servicios geométricos explícitos.</summary>
    public PadDetectionEngine(
        IGeometryClassificationEngine geometryClassificationEngine,
        IGeometryDatabaseBuilder geometryDatabaseBuilder)
    {
        this.geometryClassificationEngine = geometryClassificationEngine ??
            throw new ArgumentNullException(nameof(geometryClassificationEngine));
        this.geometryDatabaseBuilder = geometryDatabaseBuilder ??
            throw new ArgumentNullException(nameof(geometryDatabaseBuilder));
    }

    /// <inheritdoc />
    public RecognitionResult Analyze(BoardDocument document, PadDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new PadDetectionOptions();
        options.Validate();

        Stopwatch stopwatch = Stopwatch.StartNew();
        if (document.Elements.Count == 0 || document.Bounds.IsEmpty)
        {
            return RecognitionResult.Empty;
        }

        // BoardDocument garantiza coordenadas normalizadas en milímetros. Los límites combinan
        // una referencia relativa con límites físicos para impedir errores de escala documental.
        double referenceDimension = Math.Max(1D, Math.Min(document.Bounds.Width, document.Bounds.Height));
        double minimumSize = Math.Max(
            options.MinimumPadSizeMillimeters,
            Math.Min(0.35D, referenceDimension * options.MinimumPadSizeRatio));
        double maximumSize = Math.Min(
            options.MaximumPadSizeMillimeters,
            Math.Max(2D, referenceDimension * options.MaximumPadSizeRatio));

        GeometryDatabaseSnapshot geometryDatabase = geometryDatabaseBuilder.Build(document);
        GeometryClassificationResult classification = geometryClassificationEngine.Analyze(
            document,
            geometryDatabase,
            new GeometryClassificationOptions
            {
                MaximumRectangleAspectRatio = options.MaximumPadAspectRatio,
            });
        PadEvaluation padEvaluation = DetectPads(
            classification,
            minimumSize,
            maximumSize,
            options.MaximumPadAspectRatio);
        List<RecognizedPad> pads = RemoveNestedDuplicates(padEvaluation.AcceptedPads);
        List<RecognizedVia> vias = DetectVias(pads);
        HashSet<string> viaSources = vias.Select(static item => item.SourceElementId).ToHashSet(StringComparer.Ordinal);
        List<RecognizedHole> holes = DetectHoles(document, pads, minimumSize, maximumSize);
        List<RecognizedFootprint> footprints = BuildFootprints(
            pads.Where(pad => !viaSources.Contains(pad.SourceElementId)).ToArray(),
            document.Bounds,
            options);

        stopwatch.Stop();
        IReadOnlyDictionary<GeometryPrimitiveKind, int> primitiveCounts = classification.Primitives
            .GroupBy(static primitive => primitive.Kind)
            .ToDictionary(static group => group.Key, static group => group.Count());
        PadDetectionDiagnostics diagnostics = new(
            document.Elements.Count,
            classification.Primitives.Count,
            minimumSize,
            maximumSize,
            padEvaluation.Diagnostics,
            primitiveCounts);

        return new RecognitionResult(
            pads,
            vias,
            holes,
            footprints,
            geometryDatabase,
            classification,
            diagnostics,
            stopwatch.Elapsed);
    }

    private static PadEvaluation DetectPads(
        GeometryClassificationResult classification,
        double minimumSize,
        double maximumSize,
        double maximumAspectRatio)
    {
        List<RecognizedPad> accepted = [];
        List<PadCandidateDiagnostic> diagnostics = [];

        foreach (ClassifiedGeometryPrimitive primitive in classification.Primitives)
        {
            double width = primitive.Bounds.Width;
            double height = primitive.Bounds.Height;
            bool explicitPad = primitive.Kind == GeometryPrimitiveKind.ExplicitPad;
            PadCandidateRejectionReason reason = PadCandidateRejectionReason.None;

            if (!IsSupportedPadKind(primitive.Kind))
            {
                reason = PadCandidateRejectionReason.UnsupportedGeometry;
            }
            else if (!explicitPad && (width < minimumSize || height < minimumSize))
            {
                reason = PadCandidateRejectionReason.TooSmall;
            }
            else if (!explicitPad && (width > maximumSize || height > maximumSize))
            {
                reason = PadCandidateRejectionReason.TooLarge;
            }
            else if (!explicitPad && primitive.AspectRatio > maximumAspectRatio)
            {
                reason = PadCandidateRejectionReason.InvalidAspectRatio;
            }
            else
            {
                bool repeatedOutline = !primitive.IsFilled &&
                                       primitive.RepetitionCount >= 2 &&
                                       primitive.AlignedNeighborCount >= 1;
                if (!explicitPad && !primitive.IsFilled &&
                    primitive.Kind != GeometryPrimitiveKind.Donut &&
                    !repeatedOutline)
                {
                    reason = PadCandidateRejectionReason.OutlineWithoutPattern;
                }
                else
                {
                    // Los contornos repetidos reciben un umbral menor porque muchos PDF de
                    // servicio dibujan los terminales sin relleno. La repetición y alineación
                    // aportan la evidencia que sustituye al relleno ausente.
                    double minimumConfidence = explicitPad
                        ? 0D
                        : primitive.IsFilled
                            ? 0.68D
                            : 0.60D;
                    if (primitive.Confidence < minimumConfidence)
                    {
                        reason = PadCandidateRejectionReason.LowConfidence;
                    }
                }
            }

            bool isAccepted = reason == PadCandidateRejectionReason.None;
            diagnostics.Add(new PadCandidateDiagnostic(
                primitive.SourceElementId,
                primitive.Kind,
                primitive.Bounds,
                isAccepted,
                reason,
                primitive.Confidence,
                primitive.RepetitionCount,
                primitive.AlignedNeighborCount));

            if (isAccepted)
            {
                accepted.Add(new RecognizedPad(
                    $"pad-{accepted.Count + 1}",
                    primitive.SourceElementId,
                    primitive.Center,
                    primitive.Bounds,
                    primitive.SuggestedPadShape,
                    primitive.Confidence));
            }
        }

        return new PadEvaluation(accepted, diagnostics);
    }

    private static bool IsSupportedPadKind(GeometryPrimitiveKind kind) => kind is
        GeometryPrimitiveKind.ExplicitPad or
        GeometryPrimitiveKind.FilledRectangle or
        GeometryPrimitiveKind.OutlineRectangle or
        GeometryPrimitiveKind.FilledEllipse or
        GeometryPrimitiveKind.OutlineEllipse or
        GeometryPrimitiveKind.Donut or
        GeometryPrimitiveKind.Slot or
        GeometryPrimitiveKind.FilledPolygon or
        GeometryPrimitiveKind.OutlinePolygon;

    private static List<RecognizedPad> RemoveNestedDuplicates(IReadOnlyList<RecognizedPad> pads)
    {
        List<RecognizedPad> ordered = pads
            .OrderByDescending(static pad => pad.Confidence)
            .ThenBy(static pad => pad.Bounds.Width * pad.Bounds.Height)
            .ToList();
        List<RecognizedPad> result = [];

        foreach (RecognizedPad pad in ordered)
        {
            bool duplicate = result.Any(existing =>
                existing.Center.DistanceTo(pad.Center) <=
                    Math.Max(0.005D, Math.Min(existing.Bounds.Width, pad.Bounds.Width) * 0.18D) &&
                SizeSimilarity(existing.Bounds, pad.Bounds) >= 0.78D);
            if (!duplicate)
            {
                result.Add(pad);
            }
        }

        return result
            .OrderBy(static pad => pad.Center.Y)
            .ThenBy(static pad => pad.Center.X)
            .Select((pad, index) => pad with { Id = $"pad-{index + 1}" })
            .ToList();
    }

    private static List<RecognizedVia> DetectVias(IReadOnlyList<RecognizedPad> pads)
    {
        RecognizedPad[] circular = pads
            .Where(static pad => pad.Shape == PadShape.Circle)
            .OrderBy(static pad => Math.Max(pad.Bounds.Width, pad.Bounds.Height))
            .ToArray();
        if (circular.Length < 3)
        {
            return [];
        }

        double threshold = Math.Max(
            circular[0].Bounds.Width,
            Math.Max(circular[circular.Length / 3].Bounds.Width, circular[circular.Length / 3].Bounds.Height));

        return circular
            .Where(pad => Math.Max(pad.Bounds.Width, pad.Bounds.Height) <= threshold * 1.08D)
            .Select((pad, index) => new RecognizedVia(
                $"via-{index + 1}",
                pad.SourceElementId,
                pad.Center,
                Math.Max(pad.Bounds.Width, pad.Bounds.Height),
                pad.Bounds,
                Math.Min(0.92D, pad.Confidence)))
            .ToList();
    }

    private static List<RecognizedHole> DetectHoles(
        BoardDocument document,
        IReadOnlyList<RecognizedPad> pads,
        double minimumSize,
        double maximumSize)
    {
        List<RecognizedHole> result = [];
        foreach (DrillHoleElement hole in document.Elements.OfType<DrillHoleElement>())
        {
            result.Add(new RecognizedHole(
                $"hole-{result.Count + 1}",
                hole.Id,
                hole.Center,
                hole.Diameter,
                hole.IsPlated,
                hole.Bounds,
                1D));
        }

        HashSet<string> padSources = pads.Select(static pad => pad.SourceElementId).ToHashSet(StringComparer.Ordinal);
        foreach (VectorEllipseElement ellipse in document.Elements.OfType<VectorEllipseElement>())
        {
            double diameter = Math.Max(ellipse.RadiusX, ellipse.RadiusY) * 2D;
            double aspect = Math.Max(ellipse.RadiusX, ellipse.RadiusY) /
                            Math.Max(0.000001D, Math.Min(ellipse.RadiusX, ellipse.RadiusY));
            if (ellipse.IsFilled || padSources.Contains(ellipse.Id) || aspect > 1.15D ||
                diameter < minimumSize || diameter > maximumSize * 2D)
            {
                continue;
            }

            result.Add(new RecognizedHole(
                $"hole-{result.Count + 1}",
                ellipse.Id,
                ellipse.Center,
                diameter,
                false,
                ellipse.Bounds,
                0.74D));
        }

        return result;
    }

    private static List<RecognizedFootprint> BuildFootprints(
        IReadOnlyList<RecognizedPad> pads,
        Bounds2D documentBounds,
        PadDetectionOptions options)
    {
        if (pads.Count < 2)
        {
            return [];
        }

        double medianSize = Median(pads.Select(static pad =>
            Math.Max(pad.Bounds.Width, pad.Bounds.Height)).ToArray());
        double neighborDistance = Math.Max(
            medianSize * options.FootprintNeighborFactor,
            Math.Min(documentBounds.Width, documentBounds.Height) * 0.0015D);
        double maximumSpan = Math.Max(neighborDistance * 12D,
            Math.Min(documentBounds.Width, documentBounds.Height) * 0.06D);

        HashSet<string> visited = new(StringComparer.Ordinal);
        List<RecognizedFootprint> result = [];
        foreach (RecognizedPad seed in pads.OrderBy(static pad => pad.Center.Y).ThenBy(static pad => pad.Center.X))
        {
            if (!visited.Add(seed.Id))
            {
                continue;
            }

            List<RecognizedPad> cluster = [seed];
            Queue<RecognizedPad> pending = new();
            pending.Enqueue(seed);
            Bounds2D clusterBounds = seed.Bounds;

            while (pending.Count > 0 && cluster.Count < options.MaximumPadsPerFootprint)
            {
                RecognizedPad current = pending.Dequeue();
                foreach (RecognizedPad candidate in pads)
                {
                    if (visited.Contains(candidate.Id) || current.Center.DistanceTo(candidate.Center) > neighborDistance)
                    {
                        continue;
                    }

                    Bounds2D proposed = clusterBounds.Union(candidate.Bounds);
                    if (proposed.Width > maximumSpan || proposed.Height > maximumSpan ||
                        !HasCompatibleSize(seed, candidate))
                    {
                        continue;
                    }

                    visited.Add(candidate.Id);
                    cluster.Add(candidate);
                    pending.Enqueue(candidate);
                    clusterBounds = proposed;
                }
            }

            if (cluster.Count < 2)
            {
                continue;
            }

            double padding = Math.Max(0.02D, medianSize * 0.35D);
            Bounds2D bounds = cluster
                .Select(static pad => pad.Bounds)
                .Aggregate(static (current, next) => current.Union(next))
                .Inflate(padding);
            string classification = ClassifyFootprint(cluster);
            double confidence = Math.Min(0.97D,
                0.68D + (Math.Min(cluster.Count, 12) * 0.018D) + SizeConsistency(cluster) * 0.12D);

            result.Add(new RecognizedFootprint(
                $"footprint-{result.Count + 1}",
                classification,
                bounds,
                bounds.Center,
                EstimateRotation(cluster),
                cluster.Select(static pad => pad.Id).ToArray(),
                confidence));
        }

        return result;
    }

    private static bool HasCompatibleSize(RecognizedPad first, RecognizedPad second)
    {
        double firstSize = Math.Sqrt(first.Bounds.Width * first.Bounds.Height);
        double secondSize = Math.Sqrt(second.Bounds.Width * second.Bounds.Height);
        double ratio = Math.Max(firstSize, secondSize) / Math.Max(0.000001D, Math.Min(firstSize, secondSize));
        return ratio <= 2.75D;
    }

    private static string ClassifyFootprint(IReadOnlyList<RecognizedPad> pads)
    {
        if (pads.Count == 2)
        {
            return "2-PAD";
        }

        bool circular = pads.All(static pad => pad.Shape == PadShape.Circle);
        if (circular)
        {
            return $"CIRCULAR-{pads.Count}";
        }

        int rows = CountCoordinateBands(pads.Select(static pad => pad.Center.Y).ToArray());
        int columns = CountCoordinateBands(pads.Select(static pad => pad.Center.X).ToArray());
        if (rows >= 3 && columns >= 3 && rows * columns >= pads.Count * 0.70D)
        {
            return $"ARRAY-{rows}x{columns}";
        }

        return $"MULTIPAD-{pads.Count}";
    }

    private static int CountCoordinateBands(double[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        Array.Sort(values);
        double span = Math.Max(0.000001D, values[^1] - values[0]);
        double tolerance = Math.Max(0.00001D, span / Math.Max(20D, values.Length * 3D));
        int bands = 1;
        double current = values[0];
        for (int index = 1; index < values.Length; index++)
        {
            if (Math.Abs(values[index] - current) > tolerance)
            {
                bands++;
                current = values[index];
            }
        }

        return bands;
    }

    private static double EstimateRotation(IReadOnlyList<RecognizedPad> pads)
    {
        if (pads.Count < 2)
        {
            return 0D;
        }

        Point2D center = new(pads.Average(static pad => pad.Center.X), pads.Average(static pad => pad.Center.Y));
        double xx = 0D;
        double yy = 0D;
        double xy = 0D;
        foreach (RecognizedPad pad in pads)
        {
            double x = pad.Center.X - center.X;
            double y = pad.Center.Y - center.Y;
            xx += x * x;
            yy += y * y;
            xy += x * y;
        }

        return Math.Atan2(2D * xy, xx - yy) * 90D / Math.PI;
    }

    private static double SizeConsistency(IReadOnlyList<RecognizedPad> pads)
    {
        double[] areas = pads.Select(static pad => pad.Bounds.Width * pad.Bounds.Height).ToArray();
        double median = Median(areas);
        if (median <= 0D)
        {
            return 0D;
        }

        double deviation = areas.Average(area => Math.Abs(area - median) / median);
        return Math.Max(0D, 1D - Math.Min(1D, deviation));
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0D;
        }

        Array.Sort(values);
        int middle = values.Length / 2;
        return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2D : values[middle];
    }

    private static double SizeSimilarity(Bounds2D left, Bounds2D right)
    {
        double width = Math.Min(left.Width, right.Width) / Math.Max(0.000001D, Math.Max(left.Width, right.Width));
        double height = Math.Min(left.Height, right.Height) / Math.Max(0.000001D, Math.Max(left.Height, right.Height));
        return width * height;
    }

    private sealed record PadEvaluation(
        List<RecognizedPad> AcceptedPads,
        List<PadCandidateDiagnostic> Diagnostics);

}
