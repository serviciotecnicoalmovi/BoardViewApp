namespace BoardView.Core.Geometry;

/// <summary>
/// Representa un desplazamiento bidimensional expresado en milímetros.
/// </summary>
public readonly record struct Vector2D(double X, double Y)
{
    public static Vector2D Zero { get; } = new(0D, 0D);
    /// <summary>Longitud del vector.</summary>
    public double Length => Math.Sqrt((X * X) + (Y * Y));

    /// <summary>Devuelve un vector con la misma dirección y longitud uno.</summary>
    public Vector2D Normalize()
    {
        double length = Length;
        return length <= double.Epsilon ? default : new Vector2D(X / length, Y / length);
    }

    public static Vector2D operator +(Vector2D left, Vector2D right) => new(left.X + right.X, left.Y + right.Y);

    public static Vector2D operator -(Vector2D left, Vector2D right) => new(left.X - right.X, left.Y - right.Y);

    /// <summary>Multiplica el vector por un factor escalar.</summary>
    public static Vector2D operator *(Vector2D vector, double factor) =>
        new(vector.X * factor, vector.Y * factor);
}
