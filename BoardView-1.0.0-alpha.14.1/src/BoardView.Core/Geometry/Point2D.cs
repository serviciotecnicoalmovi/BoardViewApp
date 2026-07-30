namespace BoardView.Core.Geometry;

/// <summary>
/// Representa una posición bidimensional expresada en milímetros dentro del modelo interno.
/// </summary>
public readonly record struct Point2D(double X, double Y)
{
    /// <summary>Origen del sistema de coordenadas.</summary>
    public static Point2D Zero { get; } = new(0D, 0D);

    /// <summary>Calcula la distancia euclidiana hasta otro punto.</summary>
    public double DistanceTo(Point2D other)
    {
        double deltaX = other.X - X;
        double deltaY = other.Y - Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    /// <summary>Desplaza el punto mediante un vector.</summary>
    public static Point2D operator +(Point2D point, Vector2D vector) =>
        new(point.X + vector.X, point.Y + vector.Y);

    /// <summary>Obtiene el vector que une dos puntos.</summary>
    public static Vector2D operator -(Point2D end, Point2D start) =>
        new(end.X - start.X, end.Y - start.Y);
}
