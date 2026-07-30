using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Taladro metalizado o mecánico definido por centro y diámetro.</summary>
public sealed class DrillHoleElement : BoardElement
{
    public DrillHoleElement(string id, string layerId, Point2D center, double diameter, bool plated, string? netId = null, string? componentId = null)
        : base(id, layerId, CreateBounds(center, diameter), netId, componentId)
    {
        if (diameter <= 0D) throw new ArgumentOutOfRangeException(nameof(diameter));
        Center = center;
        Diameter = diameter;
        IsPlated = plated;
    }

    public Point2D Center { get; }
    public double Diameter { get; }
    public bool IsPlated { get; }

    private static Bounds2D CreateBounds(Point2D center, double diameter)
    {
        double radius = diameter / 2D;
        return new Bounds2D(center.X - radius, center.Y - radius, center.X + radius, center.Y + radius);
    }
}
