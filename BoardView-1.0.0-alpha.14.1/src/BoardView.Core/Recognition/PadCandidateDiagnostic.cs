using BoardView.Core.Geometry;

namespace BoardView.Core.Recognition;

/// <summary>Traza el resultado de evaluar una primitiva geométrica como posible pad.</summary>
public sealed record PadCandidateDiagnostic(
    string SourceElementId,
    GeometryPrimitiveKind Kind,
    Bounds2D Bounds,
    bool Accepted,
    PadCandidateRejectionReason RejectionReason,
    double Confidence,
    int RepetitionCount,
    int AlignedNeighborCount);
