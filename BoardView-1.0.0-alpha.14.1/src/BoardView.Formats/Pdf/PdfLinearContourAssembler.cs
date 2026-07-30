using BoardView.Core.Geometry;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Contorno lineal de bajo nivel leído desde una subruta PDF.
/// </summary>
/// <param name="Points">Puntos ordenados del contorno.</param>
/// <param name="IsClosed">Indica si el archivo declaró el cierre del contorno.</param>
public sealed record PdfLinearContour(IReadOnlyList<Point2D> Points, bool IsClosed);

/// <summary>
/// Contorno reconstruido antes de su clasificación geométrica.
/// </summary>
/// <param name="Points">Puntos ordenados del contorno reconstruido.</param>
/// <param name="IsClosed">Indica si el último punto vuelve al primero.</param>
public sealed record PdfAssembledContour(IReadOnlyList<Point2D> Points, bool IsClosed);

/// <summary>
/// Reconstruye contornos que un proveedor PDF expone como varias subrutas abiertas.
/// Solo enlaza extremos cuya conectividad es inequívoca; los nodos con bifurcaciones
/// se conservan como rutas abiertas para evitar inventar geometría.
/// </summary>
public sealed class PdfLinearContourAssembler
{
    private readonly double tolerance;

    /// <summary>
    /// Inicializa el ensamblador con una tolerancia física en milímetros.
    /// </summary>
    public PdfLinearContourAssembler(double tolerance = 0.0001D)
    {
        if (tolerance <= 0D || double.IsNaN(tolerance) || double.IsInfinity(tolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        this.tolerance = tolerance;
    }

    /// <summary>
    /// Une segmentos conectados del mismo PdfPath antes de clasificarlos como
    /// rectángulos, polígonos o polilíneas abiertas.
    /// </summary>
    public IReadOnlyList<PdfAssembledContour> Assemble(IEnumerable<PdfLinearContour> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<PdfLinearContour> contours = source
            .Select(Normalize)
            .Where(static item => item.Points.Count >= 2)
            .ToList();
        if (contours.Count == 0)
        {
            return Array.Empty<PdfAssembledContour>();
        }

        Dictionary<PointKey, int> endpointDegrees = BuildEndpointDegrees(contours);
        bool[] consumed = new bool[contours.Count];
        List<PdfAssembledContour> result = [];

        for (int index = 0; index < contours.Count; index++)
        {
            if (consumed[index])
            {
                continue;
            }

            PdfLinearContour seed = contours[index];
            consumed[index] = true;
            if (seed.IsClosed || AreEqual(seed.Points[0], seed.Points[^1]))
            {
                result.Add(new PdfAssembledContour(EnsureExplicitClosure(seed.Points), true));
                continue;
            }

            List<Point2D> chain = [.. seed.Points];
            ExtendChain(chain, contours, consumed, endpointDegrees, append: true);
            ExtendChain(chain, contours, consumed, endpointDegrees, append: false);

            bool isClosed = chain.Count > 2 && AreEqual(chain[0], chain[^1]);
            IReadOnlyList<Point2D> points = isClosed ? EnsureExplicitClosure(chain) : chain;
            result.Add(new PdfAssembledContour(points, isClosed));
        }

        return result;
    }

    private void ExtendChain(
        List<Point2D> chain,
        IReadOnlyList<PdfLinearContour> contours,
        bool[] consumed,
        IReadOnlyDictionary<PointKey, int> endpointDegrees,
        bool append)
    {
        while (chain.Count >= 2)
        {
            Point2D endpoint = append ? chain[^1] : chain[0];
            if (AreEqual(chain[0], chain[^1]))
            {
                return;
            }

            PointKey key = CreateKey(endpoint);
            if (!endpointDegrees.TryGetValue(key, out int degree) || degree != 2)
            {
                return;
            }

            int candidateIndex = -1;
            bool reverseCandidate = false;
            for (int index = 0; index < contours.Count; index++)
            {
                if (consumed[index])
                {
                    continue;
                }

                IReadOnlyList<Point2D> points = contours[index].Points;
                bool matchesStart = AreEqual(endpoint, points[0]);
                bool matchesEnd = AreEqual(endpoint, points[^1]);
                if (!matchesStart && !matchesEnd)
                {
                    continue;
                }

                if (candidateIndex >= 0)
                {
                    // Más de un candidato implicaría una bifurcación no segura.
                    return;
                }

                candidateIndex = index;
                reverseCandidate = append ? matchesEnd : matchesStart;
            }

            if (candidateIndex < 0)
            {
                return;
            }

            consumed[candidateIndex] = true;
            IReadOnlyList<Point2D> candidate = contours[candidateIndex].Points;
            IEnumerable<Point2D> ordered = reverseCandidate ? candidate.Reverse() : candidate;
            Point2D[] materialized = ordered.ToArray();

            if (append)
            {
                chain.AddRange(materialized.Skip(1));
            }
            else
            {
                chain.InsertRange(0, materialized.Take(materialized.Length - 1));
            }
        }
    }

    private Dictionary<PointKey, int> BuildEndpointDegrees(IReadOnlyList<PdfLinearContour> contours)
    {
        Dictionary<PointKey, int> degrees = [];
        foreach (PdfLinearContour contour in contours)
        {
            if (contour.IsClosed || AreEqual(contour.Points[0], contour.Points[^1]))
            {
                continue;
            }

            Increment(degrees, CreateKey(contour.Points[0]));
            Increment(degrees, CreateKey(contour.Points[^1]));
        }

        return degrees;
    }

    private PdfLinearContour Normalize(PdfLinearContour contour)
    {
        ArgumentNullException.ThrowIfNull(contour);
        ArgumentNullException.ThrowIfNull(contour.Points);

        List<Point2D> points = [];
        foreach (Point2D point in contour.Points)
        {
            if (points.Count == 0 || !AreEqual(points[^1], point))
            {
                points.Add(point);
            }
        }

        bool closed = contour.IsClosed || (points.Count > 2 && AreEqual(points[0], points[^1]));
        return new PdfLinearContour(closed ? EnsureExplicitClosure(points) : points, closed);
    }

    private IReadOnlyList<Point2D> EnsureExplicitClosure(IReadOnlyList<Point2D> points)
    {
        if (points.Count == 0 || AreEqual(points[0], points[^1]))
        {
            return points.ToArray();
        }

        return [.. points, points[0]];
    }

    private PointKey CreateKey(Point2D point) =>
        new(
            checked((long)Math.Round(point.X / tolerance)),
            checked((long)Math.Round(point.Y / tolerance)));

    private bool AreEqual(Point2D first, Point2D second) =>
        Math.Abs(first.X - second.X) <= tolerance &&
        Math.Abs(first.Y - second.Y) <= tolerance;

    private static void Increment(IDictionary<PointKey, int> values, PointKey key) =>
        values[key] = values.TryGetValue(key, out int count) ? count + 1 : 1;

    private readonly record struct PointKey(long X, long Y);
}
