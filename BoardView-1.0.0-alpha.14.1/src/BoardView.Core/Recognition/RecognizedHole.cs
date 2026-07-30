using BoardView.Core.Geometry;

namespace BoardView.Core.Recognition;

/// <summary>Agujero mecánico o metalizado detectado en el modelo normalizado.</summary>
public sealed record RecognizedHole(
    string Id,
    string SourceElementId,
    Point2D Center,
    double Diameter,
    bool IsPlated,
    Bounds2D Bounds,
    double Confidence);
