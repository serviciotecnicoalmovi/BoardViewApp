namespace BoardView.Core.Geometry;

/// <summary>
/// Matriz afín bidimensional. La transformación usa la convención:
/// x' = M11*x + M21*y + OffsetX; y' = M12*x + M22*y + OffsetY.
/// </summary>
public readonly record struct Matrix2D(
    double M11,
    double M12,
    double M21,
    double M22,
    double OffsetX,
    double OffsetY)
{
    public static Matrix2D Identity { get; } = new(1D, 0D, 0D, 1D, 0D, 0D);

    public static Matrix2D CreateTranslation(double x, double y) =>
        new(1D, 0D, 0D, 1D, x, y);

    public static Matrix2D CreateScale(double scaleX, double scaleY) =>
        new(scaleX, 0D, 0D, scaleY, 0D, 0D);

    public static Matrix2D CreateRotation(double angleDegrees)
    {
        double radians = angleDegrees * (Math.PI / 180D);
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        return new Matrix2D(cosine, sine, -sine, cosine, 0D, 0D);
    }

    public Point2D Transform(Point2D point) =>
        new(
            (M11 * point.X) + (M21 * point.Y) + OffsetX,
            (M12 * point.X) + (M22 * point.Y) + OffsetY);

    public Matrix2D Append(Matrix2D next) =>
        new(
            (next.M11 * M11) + (next.M21 * M12),
            (next.M12 * M11) + (next.M22 * M12),
            (next.M11 * M21) + (next.M21 * M22),
            (next.M12 * M21) + (next.M22 * M22),
            (next.M11 * OffsetX) + (next.M21 * OffsetY) + next.OffsetX,
            (next.M12 * OffsetX) + (next.M22 * OffsetY) + next.OffsetY);
}
