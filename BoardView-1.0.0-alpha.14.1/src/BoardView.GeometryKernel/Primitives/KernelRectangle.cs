using BoardView.Core.Geometry;

namespace BoardView.GeometryKernel.Primitives;

/// <summary>Rectángulo reconstruido a partir de cuatro aristas conectadas.</summary>
public sealed record KernelRectangle(
    string Id,
    IReadOnlyList<Point2D> Corners,
    IReadOnlyList<string> SourceSegmentIds,
    string GroupKey,
    bool IsAxisAligned)
{
    /// <summary>Límites alineados con los ejes que contienen el rectángulo.</summary>
    public Bounds2D Bounds { get; } = Bounds2D.FromPoints(Corners);
}
