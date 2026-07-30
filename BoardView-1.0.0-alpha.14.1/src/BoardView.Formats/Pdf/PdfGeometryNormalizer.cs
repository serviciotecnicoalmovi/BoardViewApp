using BoardView.Core.Geometry;
using BoardView.Core.Graphics;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Normaliza primitivas PDF de bajo nivel antes de convertirlas al modelo interno.
/// El normalizador elimina vértices duplicados y colineales, y reconoce rectángulos
/// alineados con los ejes aunque el archivo los describa mediante más de cuatro puntos.
/// </summary>
public sealed class PdfGeometryNormalizer
{
    private const double AbsolutePointTolerance = 0.0001D;
    private const double RelativeBoundaryTolerance = 0.015D;

    /// <summary>
    /// Normaliza una primitiva conservando su identificador, estilo y metadatos.
    /// Las primitivas que no admiten una normalización segura se devuelven sin cambios.
    /// </summary>
    /// <param name="graphic">Primitiva técnica extraída del PDF.</param>
    /// <returns>Primitiva normalizada o la instancia original cuando no existe una conversión segura.</returns>
    public GraphicObject Normalize(GraphicObject graphic)
    {
        ArgumentNullException.ThrowIfNull(graphic);
        if (graphic is not PolylineGraphic polyline ||
            !TryGetAxisAlignedRectangle(polyline.Points, polyline.IsClosed, out Bounds2D bounds))
        {
            return graphic;
        }

        bool isFilled = ReadBooleanMetadata(polyline, "pdf.is-filled");
        RectangleGraphic rectangle = new(
            polyline.Id,
            bounds,
            polyline.Width,
            isFilled)
        {
            IsVisible = polyline.IsVisible,
            LayerId = polyline.LayerId,
        };
        CopyMetadata(polyline, rectangle);
        rectangle.Metadata.Set("source.kind", "rectangle");
        rectangle.Metadata.Set("pdf.normalized-from", nameof(PolylineGraphic));
        return rectangle;
    }

    /// <summary>
    /// Intenta construir un rectángulo normalizado a partir de una ruta PDF.
    /// Tolera un punto final repetido, vértices consecutivos duplicados y puntos
    /// intermedios colineales generados por aplicaciones CAD o impresoras PDF.
    /// </summary>
    public bool TryCreateRectangle(
        string id,
        IReadOnlyList<Point2D> points,
        double strokeWidth,
        bool isFilled,
        bool isClosed,
        out RectangleGraphic rectangle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(points);
        if (strokeWidth < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(strokeWidth));
        }

        if (TryGetAxisAlignedRectangle(points, isClosed, out Bounds2D bounds))
        {
            rectangle = new RectangleGraphic(id, bounds, strokeWidth, isFilled);
            return true;
        }

        rectangle = null!;
        return false;
    }

    /// <summary>
    /// Determina si una secuencia cerrada describe exactamente el perímetro de un
    /// rectángulo alineado con los ejes y devuelve sus límites geométricos sin incluir
    /// la expansión producida por el grosor del trazo.
    /// </summary>
    public bool TryGetAxisAlignedRectangle(
        IReadOnlyList<Point2D> points,
        bool isClosed,
        out Bounds2D bounds)
    {
        ArgumentNullException.ThrowIfNull(points);
        bounds = default;
        if (points.Count < 4)
        {
            return false;
        }

        bool hasExplicitClosure = points.Count > 1 &&
                                  AreEqual(points[0], points[^1], AbsolutePointTolerance);
        if (!isClosed && !hasExplicitClosure)
        {
            return false;
        }

        List<Point2D> normalized = RemoveConsecutiveDuplicates(points);
        if (normalized.Count > 1 && AreEqual(normalized[0], normalized[^1], AbsolutePointTolerance))
        {
            normalized.RemoveAt(normalized.Count - 1);
        }

        normalized = RemoveCollinearVertices(normalized);
        if (normalized.Count != 4)
        {
            return false;
        }

        Bounds2D candidateBounds = Bounds2D.FromPoints(normalized);
        if (candidateBounds.IsEmpty || candidateBounds.Width <= 0D || candidateBounds.Height <= 0D)
        {
            return false;
        }

        double tolerance = Math.Max(
            AbsolutePointTolerance,
            Math.Min(candidateBounds.Width, candidateBounds.Height) * RelativeBoundaryTolerance);

        bool[] corners = new bool[4];
        for (int index = 0; index < normalized.Count; index++)
        {
            Point2D current = normalized[index];
            Point2D next = normalized[(index + 1) % normalized.Count];
            bool horizontal = Math.Abs(current.Y - next.Y) <= tolerance;
            bool vertical = Math.Abs(current.X - next.X) <= tolerance;
            if (horizontal == vertical)
            {
                return false;
            }

            int corner = GetCornerIndex(current, candidateBounds, tolerance);
            if (corner < 0 || corners[corner])
            {
                return false;
            }

            corners[corner] = true;
        }

        if (corners.Any(static present => !present))
        {
            return false;
        }

        bounds = candidateBounds;
        return true;
    }

    private static List<Point2D> RemoveConsecutiveDuplicates(IReadOnlyList<Point2D> points)
    {
        List<Point2D> result = new(points.Count);
        foreach (Point2D point in points)
        {
            if (result.Count == 0 || !AreEqual(result[^1], point, AbsolutePointTolerance))
            {
                result.Add(point);
            }
        }

        return result;
    }

    private static List<Point2D> RemoveCollinearVertices(List<Point2D> points)
    {
        if (points.Count <= 4)
        {
            return points;
        }

        List<Point2D> result = new(points);
        bool removed;
        do
        {
            removed = false;
            for (int index = 0; index < result.Count && result.Count > 4; index++)
            {
                Point2D previous = result[(index - 1 + result.Count) % result.Count];
                Point2D current = result[index];
                Point2D next = result[(index + 1) % result.Count];
                if (!IsCollinearAndBetween(previous, current, next))
                {
                    continue;
                }

                result.RemoveAt(index);
                removed = true;
                break;
            }
        }
        while (removed);

        return result;
    }

    private static bool IsCollinearAndBetween(Point2D first, Point2D middle, Point2D last)
    {
        double dx1 = middle.X - first.X;
        double dy1 = middle.Y - first.Y;
        double dx2 = last.X - middle.X;
        double dy2 = last.Y - middle.Y;
        double cross = (dx1 * dy2) - (dy1 * dx2);
        double scale = Math.Max(1D, Math.Abs(dx1) + Math.Abs(dy1) + Math.Abs(dx2) + Math.Abs(dy2));
        if (Math.Abs(cross) > AbsolutePointTolerance * scale)
        {
            return false;
        }

        double dot = ((middle.X - first.X) * (middle.X - last.X)) +
                     ((middle.Y - first.Y) * (middle.Y - last.Y));
        return dot <= AbsolutePointTolerance;
    }

    private static int GetCornerIndex(Point2D point, Bounds2D bounds, double tolerance)
    {
        bool left = Math.Abs(point.X - bounds.Left) <= tolerance;
        bool right = Math.Abs(point.X - bounds.Right) <= tolerance;
        bool top = Math.Abs(point.Y - bounds.Top) <= tolerance;
        bool bottom = Math.Abs(point.Y - bounds.Bottom) <= tolerance;
        return (left, right, top, bottom) switch
        {
            (true, false, true, false) => 0,
            (false, true, true, false) => 1,
            (false, true, false, true) => 2,
            (true, false, false, true) => 3,
            _ => -1,
        };
    }

    private static bool ReadBooleanMetadata(GraphicObject graphic, string key)
    {
        return graphic.Metadata.TryGetValue(key, out string? value) &&
               bool.TryParse(value, out bool result) &&
               result;
    }

    private static void CopyMetadata(GraphicObject source, GraphicObject destination)
    {
        foreach ((string key, string value) in source.Metadata.Values)
        {
            destination.Metadata.Set(key, value);
        }
    }

    private static bool AreEqual(Point2D first, Point2D second, double tolerance) =>
        Math.Abs(first.X - second.X) <= tolerance &&
        Math.Abs(first.Y - second.Y) <= tolerance;
}
