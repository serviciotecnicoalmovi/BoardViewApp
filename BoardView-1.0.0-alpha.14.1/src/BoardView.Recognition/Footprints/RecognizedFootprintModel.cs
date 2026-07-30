using BoardView.Core.Geometry;
using BoardView.Core.Recognition;
using BoardView.Recognition.Clustering;
using BoardView.Recognition.Templates;

namespace BoardView.Recognition.Footprints;

/// <summary>Footprint resuelto con métricas físicas, pads asociados y coincidencia auditable de plantilla.</summary>
public sealed record RecognizedFootprintModel(
    string Id,
    FootprintKind Kind,
    string Name,
    PadCluster Cluster,
    FootprintMetrics Metrics,
    IReadOnlyList<RecognizedPad> Pads,
    Bounds2D Bounds,
    Point2D Center,
    double Confidence,
    FootprintTemplateMatch TemplateMatch);
