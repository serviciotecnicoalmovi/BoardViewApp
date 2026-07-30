using BoardView.Core.Model;

namespace BoardView.Core.Documents;

/// <summary>Describe una capa lógica del documento de placa.</summary>
public sealed class BoardLayer
{
    public BoardLayer(string id, string name, LayerType type, BoardSide side, int order)
    {
        Id = RequireText(id, nameof(id));
        Name = RequireText(name, nameof(name));
        Type = type;
        Side = side;
        Order = order;
    }

    public string Id { get; }
    public string Name { get; }
    public LayerType Type { get; }
    public BoardSide Side { get; }
    public int Order { get; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public double Opacity { get; set; } = 1D;
    public PropertyBag Properties { get; } = new();

    private static string RequireText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }
}
