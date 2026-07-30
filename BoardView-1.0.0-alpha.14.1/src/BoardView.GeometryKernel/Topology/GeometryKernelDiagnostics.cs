namespace BoardView.GeometryKernel.Topology;

/// <summary>Métricas de construcción y reconocimiento producidas por el núcleo geométrico.</summary>
public sealed record GeometryKernelDiagnostics(
    int InputSegmentCount,
    int DiscardedDegenerateSegmentCount,
    int NodeCount,
    int EdgeCount,
    int FourEdgeCycleCount,
    int AcceptedRectangleCount,
    int RejectedCycleCount,
    int ConsumedSegmentCount,
    int RemainingSegmentCount);
