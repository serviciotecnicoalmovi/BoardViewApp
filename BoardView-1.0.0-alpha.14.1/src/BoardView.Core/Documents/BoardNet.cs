using BoardView.Core.Model;

namespace BoardView.Core.Documents;

/// <summary>Representa una red eléctrica identificable dentro de la placa.</summary>
public sealed class BoardNet
{
    private readonly List<string> elementIds = [];

    public BoardNet(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id.Trim();
        Name = name.Trim();
    }

    public string Id { get; }
    public string Name { get; }
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
}
