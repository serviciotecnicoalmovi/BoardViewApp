namespace BoardView.Core.Recognition;

/// <summary>
/// Clasificación geométrica normalizada utilizada por los motores de reconocimiento.
/// La clasificación describe la forma observada y no implica todavía semántica eléctrica.
/// </summary>
public enum GeometryPrimitiveKind
{
    Unknown = 0,
    FilledRectangle = 1,
    OutlineRectangle = 2,
    FilledEllipse = 3,
    OutlineEllipse = 4,
    Donut = 5,
    Slot = 6,
    FilledPolygon = 7,
    OutlinePolygon = 8,
    ExplicitPad = 9,
    ExplicitHole = 10,
}
