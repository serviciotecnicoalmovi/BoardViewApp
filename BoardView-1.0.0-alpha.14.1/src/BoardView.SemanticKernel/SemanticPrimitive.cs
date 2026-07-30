using BoardView.Core.Geometry;
using BoardView.Core.GeometryDatabase;

namespace BoardView.SemanticKernel;

/// <summary>Primitiva geométrica enriquecida con una clasificación semántica trazable.</summary>
public sealed record SemanticPrimitive(
    string SourceElementId,
    string LayerId,
    GeometryDatabasePrimitiveKind GeometryKind,
    PrimitiveSemantic Semantic,
    Bounds2D Bounds,
    double Confidence,
    string Rule)
{
    /// <summary>Centro geométrico utilizado por selección y diagnóstico.</summary>
    public Point2D Center => Bounds.Center;
}
