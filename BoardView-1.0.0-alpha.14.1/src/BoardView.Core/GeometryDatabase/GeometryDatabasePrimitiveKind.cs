namespace BoardView.Core.GeometryDatabase;

/// <summary>Tipos físicos almacenados en la base de datos geométrica normalizada.</summary>
public enum GeometryDatabasePrimitiveKind
{
    Unknown = 0,
    Line,
    Polyline,
    Bezier,
    Rectangle,
    Ellipse,
    Polygon,
    Arc,
    Text,
    RasterImage,
    Pad,
    Via,
    DrillHole,
    Track,
}
