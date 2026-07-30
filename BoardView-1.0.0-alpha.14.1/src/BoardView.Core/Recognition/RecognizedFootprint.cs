using BoardView.Core.Geometry;

namespace BoardView.Core.Recognition;

/// <summary>
/// Agrupación geométrica de dos o más pads. Sus límites se calculan exclusivamente
/// desde los pads asociados y nunca desde textos o referencias documentales.
/// </summary>
public sealed record RecognizedFootprint(
    string Id,
    string Classification,
    Bounds2D Bounds,
    Point2D Center,
    double RotationDegrees,
    IReadOnlyList<string> PadIds,
    double Confidence);
