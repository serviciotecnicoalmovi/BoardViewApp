using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Core.Recognition;

/// <summary>
/// Primitiva geométrica clasificada y enriquecida con información de repetición y alineación.
/// </summary>
public sealed record ClassifiedGeometryPrimitive(
    string SourceElementId,
    string LayerId,
    GeometryPrimitiveKind Kind,
    Bounds2D Bounds,
    Point2D Center,
    PadShape SuggestedPadShape,
    bool IsFilled,
    int RepetitionCount,
    int AlignedNeighborCount,
    double Confidence)
{
    /// <summary>Relación entre el lado mayor y el lado menor de la primitiva.</summary>
    public double AspectRatio =>
        Math.Max(Bounds.Width, Bounds.Height) /
        Math.Max(0.000001D, Math.Min(Bounds.Width, Bounds.Height));

    /// <summary>Indica si la primitiva posee evidencia suficiente para ser candidata conductiva.</summary>
    public bool IsConductiveCandidate =>
        Kind == GeometryPrimitiveKind.ExplicitPad ||
        IsFilled ||
        Kind == GeometryPrimitiveKind.Donut ||
        (RepetitionCount >= 2 && AlignedNeighborCount >= 1);
}
