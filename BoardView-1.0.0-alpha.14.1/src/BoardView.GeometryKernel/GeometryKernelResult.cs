using BoardView.GeometryKernel.Graph;
using BoardView.GeometryKernel.Primitives;
using BoardView.GeometryKernel.Topology;

namespace BoardView.GeometryKernel;

/// <summary>Resultado inmutable de una ejecución del núcleo geométrico.</summary>
public sealed record GeometryKernelResult(
    IReadOnlyList<KernelRectangle> Rectangles,
    IReadOnlyList<GeometrySegment> RemainingSegments,
    GeometryKernelDiagnostics Diagnostics);
