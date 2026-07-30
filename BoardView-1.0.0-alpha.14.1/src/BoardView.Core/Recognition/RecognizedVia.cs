using BoardView.Core.Geometry;

namespace BoardView.Core.Recognition;

/// <summary>Vía o taladro circular inferido por tamaño y forma.</summary>
public sealed record RecognizedVia(
    string Id,
    string SourceElementId,
    Point2D Center,
    double Diameter,
    Bounds2D Bounds,
    double Confidence);
