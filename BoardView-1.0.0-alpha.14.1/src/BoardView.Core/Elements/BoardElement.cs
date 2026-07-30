using BoardView.Core.Geometry;
using BoardView.Core.Model;

namespace BoardView.Core.Elements;

/// <summary>Clase base de todos los objetos geométricos del modelo interno.</summary>
public abstract class BoardElement
{
    protected BoardElement(
        string id,
        string layerId,
        Bounds2D bounds,
        string? netId = null,
        string? componentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        Id = id.Trim();
        LayerId = layerId.Trim();
        NetId = NormalizeOptional(netId);
        ComponentId = NormalizeOptional(componentId);
        Bounds = bounds;
    }

    public string Id { get; }
    public string LayerId { get; }
    public string? NetId { get; }
    public string? ComponentId { get; }
    public Bounds2D Bounds { get; protected set; }
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public PropertyBag Properties { get; } = new();

    /// <summary>Updates the spatial bounds after a controlled geometry mutation.</summary>
    public void UpdateBounds(Bounds2D bounds)
    {
        if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
            !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        Bounds = bounds;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
