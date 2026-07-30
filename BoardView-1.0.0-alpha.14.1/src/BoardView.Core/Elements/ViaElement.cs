using BoardView.Core.Geometry;

namespace BoardView.Core.Elements;

/// <summary>Conexión metalizada entre capas.</summary>
public sealed class ViaElement : BoardElement
{
    public ViaElement(
        string id,
        string layerId,
        Point2D position,
        double diameter,
        double drillDiameter,
        string? netId = null)
        : base(id, layerId, CreateBounds(position, diameter), netId)
    {
        if (diameter <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(diameter), "El diámetro debe ser mayor que cero.");
        }

        if (drillDiameter < 0D || drillDiameter > diameter)
        {
            throw new ArgumentOutOfRangeException(
                nameof(drillDiameter),
                "El taladro debe estar entre cero y el diámetro exterior.");
        }

        Position = position;
        Diameter = diameter;
        DrillDiameter = drillDiameter;
    }

    public Point2D Position { get; }
    public double Diameter { get; }
    public double DrillDiameter { get; }

    private static Bounds2D CreateBounds(Point2D position, double diameter)
    {
        double radius = Math.Max(diameter, 0D) / 2D;
        return new Bounds2D(
            position.X - radius,
            position.Y - radius,
            position.X + radius,
            position.Y + radius);
    }
}
