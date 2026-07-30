using BoardView.Core.Geometry;

namespace BoardView.Recognition.Footprints;

/// <summary>Métricas normalizadas utilizadas para clasificar un footprint.</summary>
public sealed record FootprintMetrics(
    int PadCount,
    int Rows,
    int Columns,
    double PitchX,
    double PitchY,
    double RotationDegrees,
    double Symmetry,
    Bounds2D Bounds);
