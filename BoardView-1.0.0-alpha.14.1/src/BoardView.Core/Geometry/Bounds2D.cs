namespace BoardView.Core.Geometry;

/// <summary>
/// Rectángulo alineado con los ejes que delimita una geometría bidimensional.
/// </summary>
public readonly record struct Bounds2D
{
    /// <summary>Límites vacíos.</summary>
    public static Bounds2D Empty { get; } = default;
    /// <summary>Inicializa límites normalizados.</summary>
    public Bounds2D(double left, double top, double right, double bottom)
    {
        Left = Math.Min(left, right);
        Top = Math.Min(top, bottom);
        Right = Math.Max(left, right);
        Bottom = Math.Max(top, bottom);
    }

    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public Point2D Center => new((Left + Right) / 2D, (Top + Bottom) / 2D);
    public bool IsEmpty => Width <= 0D || Height <= 0D;

    /// <summary>Comprueba si un punto pertenece a los límites, incluidos sus bordes.</summary>
    public bool Contains(Point2D point) =>
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    /// <summary>Comprueba si estos límites intersectan otros.</summary>
    public bool Intersects(Bounds2D other) =>
        Left <= other.Right && Right >= other.Left && Top <= other.Bottom && Bottom >= other.Top;

    /// <summary>Expande los límites uniformemente.</summary>
    public Bounds2D Inflate(double amount)
    {
        if (amount < 0D) throw new ArgumentOutOfRangeException(nameof(amount));
        return new Bounds2D(Left - amount, Top - amount, Right + amount, Bottom + amount);
    }

    /// <summary>Combina estos límites con otros.</summary>
    public Bounds2D Union(Bounds2D other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return new Bounds2D(
            Math.Min(Left, other.Left),
            Math.Min(Top, other.Top),
            Math.Max(Right, other.Right),
            Math.Max(Bottom, other.Bottom));
    }

    /// <summary>Crea límites a partir de una colección de puntos.</summary>
    public static Bounds2D FromPoints(IEnumerable<Point2D> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        using IEnumerator<Point2D> enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return default;
        }

        Point2D first = enumerator.Current;
        double left = first.X;
        double right = first.X;
        double top = first.Y;
        double bottom = first.Y;

        while (enumerator.MoveNext())
        {
            Point2D point = enumerator.Current;
            left = Math.Min(left, point.X);
            right = Math.Max(right, point.X);
            top = Math.Min(top, point.Y);
            bottom = Math.Max(bottom, point.Y);
        }

        return new Bounds2D(left, top, right, bottom);
    }
}
