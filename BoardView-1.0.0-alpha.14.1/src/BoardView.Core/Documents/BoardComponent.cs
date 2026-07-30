using BoardView.Core.Geometry;
using BoardView.Core.Model;

namespace BoardView.Core.Documents;

/// <summary>Componente electrónico colocado sobre la placa.</summary>
public sealed class BoardComponent
{
    private readonly List<string> elementIds = [];

    public BoardComponent(
        string id,
        string reference,
        string value,
        Point2D position,
        double rotationDegrees,
        BoardSide side)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        Id = id.Trim();
        Reference = reference.Trim();
        Value = value?.Trim() ?? string.Empty;
        Position = position;
        RotationDegrees = NormalizeRotation(rotationDegrees);
        Side = side;
    }

    public string Id { get; }
    public string Reference { get; }
    public string Value { get; }
    public Point2D Position { get; }
    public double RotationDegrees { get; }
    public BoardSide Side { get; }
    public string Footprint { get; set; } = string.Empty;
    public IReadOnlyList<string> ElementIds => elementIds;
    public PropertyBag Properties { get; } = new();

    public void AttachElement(string elementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        string normalized = elementId.Trim();
        if (!elementIds.Contains(normalized, StringComparer.Ordinal))
        {
            elementIds.Add(normalized);
        }
    }

    /// <summary>Detaches an element identifier when the element is removed from the document.</summary>
    public bool DetachElement(string elementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        return elementIds.Remove(elementId.Trim());
    }

    private static double NormalizeRotation(double rotationDegrees)
    {
        double normalized = rotationDegrees % 360D;
        return normalized < 0D ? normalized + 360D : normalized;
    }
}
