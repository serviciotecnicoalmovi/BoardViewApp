using BoardView.Core.Geometry;
using BoardView.Recognition.Footprints;

namespace BoardView.Recognition.Components;

/// <summary>Componente electrónico inferido y preparado para búsqueda, selección y cross-probe.</summary>
public sealed record RecognizedComponentModel(
    string Id,
    string Reference,
    RecognizedFootprintModel Footprint,
    Point2D Center,
    Bounds2D Bounds,
    double RotationDegrees,
    double Confidence);
