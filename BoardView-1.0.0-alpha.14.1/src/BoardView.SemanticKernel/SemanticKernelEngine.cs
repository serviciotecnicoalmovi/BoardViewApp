using System.Diagnostics;
using BoardView.Core.Documents;
using BoardView.Core.GeometryDatabase;
using BoardView.Core.Recognition;

namespace BoardView.SemanticKernel;

/// <summary>
/// Clasifica primitivas normalizadas mediante evidencia explícita, capa, escala física
/// y resultados confirmados por los detectores geométricos de bajo nivel.
/// </summary>
public sealed class SemanticKernelEngine : ISemanticKernel
{
    /// <inheritdoc />
    public SemanticAnalysisResult Analyze(
        BoardDocument document,
        RecognitionResult recognition,
        SemanticKernelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(recognition);
        options ??= new SemanticKernelOptions();
        options.Validate();

        GeometryDatabaseSnapshot database = recognition.GeometryDatabase;
        if (database.TotalCount == 0 || database.Bounds.IsEmpty)
        {
            return SemanticAnalysisResult.Empty;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        HashSet<string> padIds = recognition.Pads
            .Select(static item => item.SourceElementId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> viaIds = recognition.Vias
            .Select(static item => item.SourceElementId)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> holeIds = recognition.Holes
            .Select(static item => item.SourceElementId)
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, ClassifiedGeometryPrimitive> classified = recognition.GeometryClassification.Primitives
            .GroupBy(static item => item.SourceElementId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        Dictionary<string, BoardLayer> layers = document.Layers
            .ToDictionary(static item => item.Id, StringComparer.Ordinal);

        double documentArea = Math.Max(0.000001D, database.Bounds.Width * database.Bounds.Height);
        List<SemanticPrimitive> results = new(database.TotalCount);

        foreach (GeometryDatabaseEntry entry in database.Entries)
        {
            SemanticDecision decision = Classify(
                entry,
                documentArea,
                padIds,
                viaIds,
                holeIds,
                classified,
                layers,
                options);
            results.Add(new SemanticPrimitive(
                entry.SourceElementId,
                entry.LayerId,
                entry.Kind,
                decision.Semantic,
                entry.Bounds,
                decision.Confidence,
                decision.Rule));
        }

        stopwatch.Stop();
        return new SemanticAnalysisResult(results, stopwatch.Elapsed);
    }

    private static SemanticDecision Classify(
        GeometryDatabaseEntry entry,
        double documentArea,
        IReadOnlySet<string> padIds,
        IReadOnlySet<string> viaIds,
        IReadOnlySet<string> holeIds,
        IReadOnlyDictionary<string, ClassifiedGeometryPrimitive> classified,
        IReadOnlyDictionary<string, BoardLayer> layers,
        SemanticKernelOptions options)
    {
        if (holeIds.Contains(entry.SourceElementId) || entry.Kind == GeometryDatabasePrimitiveKind.DrillHole)
        {
            return new(PrimitiveSemantic.Hole, 1D, "ExplicitOrRecognizedHole");
        }

        if (viaIds.Contains(entry.SourceElementId) || entry.Kind == GeometryDatabasePrimitiveKind.Via)
        {
            return new(PrimitiveSemantic.Via, 0.98D, "ExplicitOrRecognizedVia");
        }

        if (padIds.Contains(entry.SourceElementId) || entry.Kind == GeometryDatabasePrimitiveKind.Pad)
        {
            return new(PrimitiveSemantic.Pad, 0.98D, "ExplicitOrRecognizedPad");
        }

        if (entry.Kind == GeometryDatabasePrimitiveKind.Text)
        {
            return new(PrimitiveSemantic.Text, 1D, "TextPrimitive");
        }

        LayerType layerType = layers.TryGetValue(entry.LayerId, out BoardLayer? layer)
            ? layer.Type
            : LayerType.Unknown;
        PrimitiveSemantic? layerSemantic = MapLayer(layerType);
        if (layerSemantic is not null)
        {
            return new(layerSemantic.Value, 0.92D, $"Layer:{layerType}");
        }

        double areaRatio = Math.Max(0D, entry.BoundingArea) / documentArea;
        if (entry.IsClosed && areaRatio >= options.BoardOutlineAreaRatio)
        {
            return new(PrimitiveSemantic.BoardOutline, 0.90D, "ClosedLargeDocumentBoundary");
        }

        if (entry.Kind == GeometryDatabasePrimitiveKind.Track)
        {
            return new(PrimitiveSemantic.Copper, 0.95D, "ExplicitTrack");
        }

        if (classified.TryGetValue(entry.SourceElementId, out ClassifiedGeometryPrimitive? primitive) &&
            primitive.IsConductiveCandidate &&
            Math.Max(entry.Width, entry.Height) <= options.MaximumPadDimensionMillimeters)
        {
            return new(PrimitiveSemantic.Pad, Math.Max(0.55D, primitive.Confidence), "ConductiveGeometryCandidate");
        }

        if (entry.IsClosed &&
            areaRatio >= options.ComponentBodyAreaRatio &&
            entry.Kind is GeometryDatabasePrimitiveKind.Rectangle or
                GeometryDatabasePrimitiveKind.Polygon or
                GeometryDatabasePrimitiveKind.Polyline)
        {
            return new(PrimitiveSemantic.ComponentBody, 0.68D, "ClosedMediumScaleBody");
        }

        if (entry.Kind is GeometryDatabasePrimitiveKind.Line or
            GeometryDatabasePrimitiveKind.Polyline or
            GeometryDatabasePrimitiveKind.Bezier or
            GeometryDatabasePrimitiveKind.Arc)
        {
            return new(PrimitiveSemantic.Silkscreen, 0.52D, "DocumentStroke");
        }

        if (entry.Kind == GeometryDatabasePrimitiveKind.RasterImage)
        {
            return new(PrimitiveSemantic.Mechanical, 0.40D, "RasterReference");
        }

        return new(PrimitiveSemantic.Unknown, 0.20D, "InsufficientEvidence");
    }

    private static PrimitiveSemantic? MapLayer(LayerType layerType) => layerType switch
    {
        LayerType.Copper => PrimitiveSemantic.Copper,
        LayerType.Silkscreen => PrimitiveSemantic.Silkscreen,
        LayerType.Outline => PrimitiveSemantic.BoardOutline,
        LayerType.Mechanical => PrimitiveSemantic.Mechanical,
        LayerType.Drill => PrimitiveSemantic.Hole,
        _ => null,
    };

    private readonly record struct SemanticDecision(
        PrimitiveSemantic Semantic,
        double Confidence,
        string Rule);
}
