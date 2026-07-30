using System.Diagnostics;
using BoardView.Core.Documents;
using BoardView.Core.Elements;

namespace BoardView.Core.GeometryDatabase;

/// <summary>
/// Materializa todos los elementos de <see cref="BoardDocument"/> en registros geométricos
/// estables. Esta etapa no clasifica pads ni elimina candidatos.
/// </summary>
public sealed class GeometryDatabaseBuilder : IGeometryDatabaseBuilder
{
    /// <inheritdoc />
    public GeometryDatabaseSnapshot Build(BoardDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Stopwatch stopwatch = Stopwatch.StartNew();
        GeometryDatabaseEntry[] entries = document.Elements
            .Select(CreateEntry)
            .OrderBy(static entry => entry.LayerId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Bounds.Top)
            .ThenBy(static entry => entry.Bounds.Left)
            .ToArray();
        stopwatch.Stop();
        return new GeometryDatabaseSnapshot(entries, document.Bounds, stopwatch.Elapsed);
    }

    private static GeometryDatabaseEntry CreateEntry(BoardElement element)
    {
        GeometryDatabasePrimitiveKind kind = element switch
        {
            VectorLineElement => GeometryDatabasePrimitiveKind.Line,
            VectorPolylineElement => GeometryDatabasePrimitiveKind.Polyline,
            VectorBezierElement => GeometryDatabasePrimitiveKind.Bezier,
            VectorRectangleElement => GeometryDatabasePrimitiveKind.Rectangle,
            VectorEllipseElement => GeometryDatabasePrimitiveKind.Ellipse,
            PolygonElement => GeometryDatabasePrimitiveKind.Polygon,
            ArcElement => GeometryDatabasePrimitiveKind.Arc,
            TextElement => GeometryDatabasePrimitiveKind.Text,
            RasterImageElement => GeometryDatabasePrimitiveKind.RasterImage,
            PadElement => GeometryDatabasePrimitiveKind.Pad,
            ViaElement => GeometryDatabasePrimitiveKind.Via,
            DrillHoleElement => GeometryDatabasePrimitiveKind.DrillHole,
            TrackElement => GeometryDatabasePrimitiveKind.Track,
            _ => GeometryDatabasePrimitiveKind.Unknown,
        };

        bool isClosed = element switch
        {
            VectorPolylineElement polyline => polyline.IsClosed,
            VectorRectangleElement => true,
            VectorEllipseElement => true,
            PolygonElement => true,
            PadElement => true,
            ViaElement => true,
            DrillHoleElement => true,
            _ => false,
        };
        bool isFilled = element switch
        {
            VectorRectangleElement rectangle => rectangle.IsFilled,
            VectorEllipseElement ellipse => ellipse.IsFilled,
            PolygonElement polygon => polygon.IsFilled,
            PadElement => true,
            ViaElement => true,
            _ => false,
        };

        return new GeometryDatabaseEntry(
            element.Id,
            element.LayerId,
            kind,
            element.GetType().Name,
            element.Bounds,
            isClosed,
            isFilled);
    }
}
