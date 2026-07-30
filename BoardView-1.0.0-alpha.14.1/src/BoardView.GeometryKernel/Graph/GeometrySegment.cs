using BoardView.Core.Geometry;

namespace BoardView.GeometryKernel.Graph;

/// <summary>
/// Segmento lineal de entrada para el núcleo geométrico.
/// El grupo evita conectar geometrías con estilos o semánticas incompatibles.
/// </summary>
public sealed record GeometrySegment(
    string Id,
    Point2D Start,
    Point2D End,
    string GroupKey)
{
    /// <summary>Longitud euclidiana del segmento.</summary>
    public double Length => Start.DistanceTo(End);
}
