using BoardView.Core.Geometry;

namespace BoardView.Core.GeometryDatabase;

/// <summary>Registro inmutable de una entidad dentro de la base de datos geométrica.</summary>
public sealed record GeometryDatabaseEntry(
    string SourceElementId,
    string LayerId,
    GeometryDatabasePrimitiveKind Kind,
    string SourceType,
    Bounds2D Bounds,
    bool IsClosed,
    bool IsFilled)
{
    /// <summary>Ancho normalizado en milímetros.</summary>
    public double Width => Bounds.Width;

    /// <summary>Alto normalizado en milímetros.</summary>
    public double Height => Bounds.Height;

    /// <summary>Área del rectángulo envolvente en milímetros cuadrados.</summary>
    public double BoundingArea => Bounds.Width * Bounds.Height;
}
