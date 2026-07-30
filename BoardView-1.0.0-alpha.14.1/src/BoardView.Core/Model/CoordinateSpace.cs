using BoardView.Core.Documents.Common;
using BoardView.Core.Geometry;

namespace BoardView.Core.Model;

/// <summary>Describe la unidad, el origen y la transformación de un sistema de coordenadas.</summary>
public sealed class CoordinateSpace
{
    public CoordinateSpace(string name, MeasurementUnit unit, Point2D origin, Matrix2D transform, bool yAxisUp = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Unit = unit;
        Origin = origin;
        Transform = transform;
        YAxisUp = yAxisUp;
    }

    public string Name { get; }
    public MeasurementUnit Unit { get; }
    public Point2D Origin { get; }
    public Matrix2D Transform { get; }
    public bool YAxisUp { get; }

    public Point2D ToWorld(Point2D point)
    {
        Point2D source = new(point.X + Origin.X, point.Y + Origin.Y);
        Point2D transformed = Transform.Transform(source);
        double factor = UnitConverter.Convert(1D, Unit, MeasurementUnit.Millimeter);
        return new Point2D(transformed.X * factor, transformed.Y * factor);
    }

    public static CoordinateSpace CreateMillimeterWorld() =>
        new("Board world", MeasurementUnit.Millimeter, Point2D.Zero, Matrix2D.Identity);
}
