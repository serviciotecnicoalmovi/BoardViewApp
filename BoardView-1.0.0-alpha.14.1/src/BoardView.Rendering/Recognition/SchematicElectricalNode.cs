using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Tipo lógico de un nodo del grafo eléctrico esquemático.
/// </summary>
public enum SchematicElectricalNodeKind
{
    Unknown = 0,
    SymbolBody = 1,
    Pin = 2,
    Wire = 3,
    Junction = 4,
    Terminal = 5,
    Ground = 6,
    PowerPort = 7,
    Pad = 8,
    Hole = 9
}

/// <summary>
/// Nodo eléctrico construido a partir de un componente geométrico indexado.
/// </summary>
/// <remarks>
/// El nodo conserva el componente original para permitir que las etapas
/// posteriores consulten clasificación, confianza, densidad y píxeles sin
/// duplicar información.
/// </remarks>
public sealed record SchematicElectricalNode
{
    public SchematicElectricalNode(
        int id,
        SchematicElectricalNodeKind kind,
        BoardGeometryIndexedComponent component)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        ArgumentNullException.ThrowIfNull(component);

        Id = id;
        Kind = kind;
        Component = component;
    }

    /// <summary>Identificador estable del nodo.</summary>
    public int Id { get; }

    /// <summary>Rol eléctrico aproximado del nodo.</summary>
    public SchematicElectricalNodeKind Kind { get; }

    /// <summary>Componente geométrico de origen.</summary>
    public BoardGeometryIndexedComponent Component { get; }

    public BoardGeometryBounds Bounds =>
        Component.Bounds;

    public double CenterX =>
        Component.CenterX;

    public double CenterY =>
        Component.CenterY;

    public double Confidence =>
        Component.Confidence;

    public bool IsWireLike =>
        Kind is
            SchematicElectricalNodeKind.Wire or
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal;

    public bool IsSymbolLike =>
        Kind is
            SchematicElectricalNodeKind.SymbolBody or
            SchematicElectricalNodeKind.Ground or
            SchematicElectricalNodeKind.PowerPort;
}
