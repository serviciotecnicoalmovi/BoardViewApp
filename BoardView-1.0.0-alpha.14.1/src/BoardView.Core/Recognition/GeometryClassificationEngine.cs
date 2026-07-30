using System.Diagnostics;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Core.GeometryDatabase;

namespace BoardView.Core.Recognition;

/// <summary>
/// Convierte geometría genérica en primitivas clasificadas mediante forma, relleno,
/// repetición y alineación. No modifica el documento ni crea entidades electrónicas.
/// </summary>
public sealed class GeometryClassificationEngine : IGeometryClassificationEngine
{
    /// <inheritdoc />
    public GeometryClassificationResult Analyze(
        BoardDocument document,
        GeometryClassificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        GeometryDatabaseSnapshot database = new GeometryDatabaseBuilder().Build(document);
        return Analyze(document, database, options);
    }

    /// <inheritdoc />
    public GeometryClassificationResult Analyze(
        BoardDocument document,
        GeometryDatabaseSnapshot geometryDatabase,
        GeometryClassificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(geometryDatabase);
        options ??= new GeometryClassificationOptions();
        options.Validate();

        Stopwatch stopwatch = Stopwatch.StartNew();
        List<RawPrimitive> raw = ExtractRawPrimitives(document, geometryDatabase, options);
        if (raw.Count == 0)
        {
            return GeometryClassificationResult.Empty;
        }

        ApplyDonutClassification(raw);
        IReadOnlyList<PrimitiveGroup> groups = BuildEquivalentSizeGroups(raw, options.SizeToleranceRatio);
        List<ClassifiedGeometryPrimitive> classified = new(raw.Count);

        foreach (RawPrimitive primitive in raw)
        {
            PrimitiveGroup group = groups.First(candidate => candidate.Items.Contains(primitive));
            int alignedNeighbors = CountAlignedNeighbors(primitive, group.Items, options.AlignmentDistanceFactor);
            double confidence = CalculateConfidence(primitive, group.Items.Count, alignedNeighbors);
            classified.Add(new ClassifiedGeometryPrimitive(
                primitive.Element.Id,
                primitive.Element.LayerId,
                primitive.Kind,
                primitive.Bounds,
                primitive.Bounds.Center,
                primitive.Shape,
                primitive.IsFilled,
                group.Items.Count,
                alignedNeighbors,
                confidence));
        }

        stopwatch.Stop();
        return new GeometryClassificationResult(
            classified
                .OrderBy(static item => item.Center.Y)
                .ThenBy(static item => item.Center.X)
                .ToArray(),
            stopwatch.Elapsed);
    }

    private static List<RawPrimitive> ExtractRawPrimitives(
        BoardDocument document,
        GeometryDatabaseSnapshot geometryDatabase,
        GeometryClassificationOptions options)
    {
        List<RawPrimitive> result = [];
        foreach (GeometryDatabaseEntry entry in geometryDatabase.Entries)
        {
            if (!document.TryGetElement(entry.SourceElementId, out BoardElement? element) || element is null)
            {
                continue;
            }

            RawPrimitive? primitive = ClassifyElement(element, options);
            if (primitive is not null && !primitive.Bounds.IsEmpty)
            {
                result.Add(primitive);
            }
        }

        return result;
    }

    private static RawPrimitive? ClassifyElement(
        BoardElement element,
        GeometryClassificationOptions options)
    {
        if (element is PadElement pad)
        {
            return new RawPrimitive(
                pad,
                GeometryPrimitiveKind.ExplicitPad,
                pad.Bounds,
                pad.Shape,
                true);
        }

        if (element is DrillHoleElement hole)
        {
            return new RawPrimitive(
                hole,
                GeometryPrimitiveKind.ExplicitHole,
                hole.Bounds,
                PadShape.Circle,
                false);
        }

        if (element is VectorRectangleElement rectangle &&
            IsUsableRectangle(rectangle.Bounds, options.MaximumRectangleAspectRatio))
        {
            return new RawPrimitive(
                rectangle,
                rectangle.IsFilled
                    ? GeometryPrimitiveKind.FilledRectangle
                    : GeometryPrimitiveKind.OutlineRectangle,
                rectangle.Bounds,
                PadShape.Rectangle,
                rectangle.IsFilled);
        }

        if (element is VectorEllipseElement ellipse)
        {
            return ClassifyEllipse(ellipse);
        }

        if (element is VectorPolylineElement polyline &&
            TryGetRectangularBounds(polyline, out Bounds2D polylineBounds) &&
            IsUsableRectangle(polylineBounds, options.MaximumRectangleAspectRatio))
        {
            return new RawPrimitive(
                polyline,
                GeometryPrimitiveKind.OutlineRectangle,
                polylineBounds,
                PadShape.Rectangle,
                false);
        }

        if (element is PolygonElement polygon &&
            IsUsableRectangle(polygon.Bounds, options.MaximumRectangleAspectRatio))
        {
            return new RawPrimitive(
                polygon,
                polygon.IsFilled
                    ? GeometryPrimitiveKind.FilledPolygon
                    : GeometryPrimitiveKind.OutlinePolygon,
                polygon.Bounds,
                PadShape.Polygon,
                polygon.IsFilled);
        }

        return null;
    }

    private static RawPrimitive ClassifyEllipse(VectorEllipseElement ellipse)
    {
        double aspect = Math.Max(ellipse.RadiusX, ellipse.RadiusY) /
                        Math.Max(0.000001D, Math.Min(ellipse.RadiusX, ellipse.RadiusY));
        GeometryPrimitiveKind kind;
        PadShape shape;
        if (aspect >= 1.45D)
        {
            kind = GeometryPrimitiveKind.Slot;
            shape = PadShape.Oval;
        }
        else
        {
            kind = ellipse.IsFilled
                ? GeometryPrimitiveKind.FilledEllipse
                : GeometryPrimitiveKind.OutlineEllipse;
            shape = PadShape.Circle;
        }

        return new RawPrimitive(ellipse, kind, ellipse.Bounds, shape, ellipse.IsFilled);
    }

    private static bool IsUsableRectangle(Bounds2D bounds, double maximumAspectRatio)
    {
        if (bounds.IsEmpty || bounds.Width <= 0D || bounds.Height <= 0D)
        {
            return false;
        }

        double aspect = Math.Max(bounds.Width, bounds.Height) /
                        Math.Max(0.000001D, Math.Min(bounds.Width, bounds.Height));
        return aspect <= maximumAspectRatio;
    }

    private static bool TryGetRectangularBounds(
        VectorPolylineElement polyline,
        out Bounds2D bounds)
    {
        bounds = polyline.Bounds;
        if (!polyline.IsClosed || polyline.Points.Count is < 4 or > 5 || bounds.IsEmpty)
        {
            return false;
        }

        double tolerance = Math.Max(0.001D, Math.Min(bounds.Width, bounds.Height) * 0.08D);
        foreach (Point2D point in polyline.Points)
        {
            bool onVertical = Math.Abs(point.X - bounds.Left) <= tolerance ||
                              Math.Abs(point.X - bounds.Right) <= tolerance;
            bool onHorizontal = Math.Abs(point.Y - bounds.Top) <= tolerance ||
                                Math.Abs(point.Y - bounds.Bottom) <= tolerance;
            if (!onVertical || !onHorizontal)
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplyDonutClassification(List<RawPrimitive> primitives)
    {
        RawPrimitive[] ellipses = primitives
            .Where(static item => item.Shape == PadShape.Circle)
            .ToArray();

        foreach (RawPrimitive outer in ellipses)
        {
            double outerDiameter = Math.Max(outer.Bounds.Width, outer.Bounds.Height);
            RawPrimitive? inner = ellipses.FirstOrDefault(candidate =>
                !ReferenceEquals(candidate, outer) &&
                candidate.Bounds.Width < outer.Bounds.Width &&
                candidate.Bounds.Height < outer.Bounds.Height &&
                candidate.Bounds.Center.DistanceTo(outer.Bounds.Center) <= outerDiameter * 0.08D &&
                Math.Max(candidate.Bounds.Width, candidate.Bounds.Height) >= outerDiameter * 0.20D);

            if (inner is not null)
            {
                outer.Kind = GeometryPrimitiveKind.Donut;
            }
        }
    }

    private static IReadOnlyList<PrimitiveGroup> BuildEquivalentSizeGroups(
        IReadOnlyList<RawPrimitive> primitives,
        double toleranceRatio)
    {
        List<PrimitiveGroup> groups = [];
        foreach (RawPrimitive primitive in primitives
                     .OrderBy(static item => item.Bounds.Width * item.Bounds.Height))
        {
            PrimitiveGroup? group = groups.FirstOrDefault(candidate =>
                AreEquivalentSizes(candidate.Representative.Bounds, primitive.Bounds, toleranceRatio) &&
                candidate.Representative.Shape == primitive.Shape);
            if (group is null)
            {
                groups.Add(new PrimitiveGroup(primitive));
            }
            else
            {
                group.Items.Add(primitive);
            }
        }

        return groups;
    }

    private static bool AreEquivalentSizes(Bounds2D left, Bounds2D right, double toleranceRatio)
    {
        static double Similarity(double first, double second) =>
            Math.Min(first, second) / Math.Max(0.000001D, Math.Max(first, second));

        return Similarity(left.Width, right.Width) >= 1D - toleranceRatio &&
               Similarity(left.Height, right.Height) >= 1D - toleranceRatio;
    }

    private static int CountAlignedNeighbors(
        RawPrimitive source,
        IReadOnlyList<RawPrimitive> candidates,
        double distanceFactor)
    {
        double minor = Math.Max(0.000001D, Math.Min(source.Bounds.Width, source.Bounds.Height));
        double major = Math.Max(source.Bounds.Width, source.Bounds.Height);
        double axisTolerance = Math.Max(minor * 0.70D, 0.01D);
        double maximumDistance = Math.Max(major, minor) * distanceFactor;
        int count = 0;

        foreach (RawPrimitive candidate in candidates)
        {
            if (ReferenceEquals(source, candidate))
            {
                continue;
            }

            double dx = Math.Abs(candidate.Bounds.Center.X - source.Bounds.Center.X);
            double dy = Math.Abs(candidate.Bounds.Center.Y - source.Bounds.Center.Y);
            bool sameRow = dy <= axisTolerance && dx <= maximumDistance;
            bool sameColumn = dx <= axisTolerance && dy <= maximumDistance;
            if (sameRow || sameColumn)
            {
                count++;
            }
        }

        return count;
    }

    private static double CalculateConfidence(
        RawPrimitive primitive,
        int repetitionCount,
        int alignedNeighborCount)
    {
        double confidence = primitive.Kind switch
        {
            GeometryPrimitiveKind.ExplicitPad => 1D,
            GeometryPrimitiveKind.ExplicitHole => 1D,
            GeometryPrimitiveKind.FilledRectangle => 0.90D,
            GeometryPrimitiveKind.FilledEllipse => 0.92D,
            GeometryPrimitiveKind.FilledPolygon => 0.82D,
            GeometryPrimitiveKind.Donut => 0.90D,
            GeometryPrimitiveKind.Slot => primitive.IsFilled ? 0.86D : 0.58D,
            GeometryPrimitiveKind.OutlineRectangle => 0.50D,
            GeometryPrimitiveKind.OutlineEllipse => 0.54D,
            GeometryPrimitiveKind.OutlinePolygon => 0.45D,
            _ => 0.20D,
        };

        if (repetitionCount >= 2)
        {
            confidence += 0.10D;
        }

        if (repetitionCount >= 4)
        {
            confidence += 0.06D;
        }

        if (alignedNeighborCount >= 1)
        {
            confidence += 0.14D;
        }

        if (alignedNeighborCount >= 3)
        {
            confidence += 0.08D;
        }

        return Math.Min(1D, confidence);
    }

    private sealed class RawPrimitive
    {
        public RawPrimitive(
            BoardElement element,
            GeometryPrimitiveKind kind,
            Bounds2D bounds,
            PadShape shape,
            bool isFilled)
        {
            Element = element;
            Kind = kind;
            Bounds = bounds;
            Shape = shape;
            IsFilled = isFilled;
        }

        public BoardElement Element { get; }
        public GeometryPrimitiveKind Kind { get; set; }
        public Bounds2D Bounds { get; }
        public PadShape Shape { get; }
        public bool IsFilled { get; }
    }

    private sealed class PrimitiveGroup
    {
        public PrimitiveGroup(RawPrimitive representative)
        {
            Representative = representative;
            Items = [representative];
        }

        public RawPrimitive Representative { get; }
        public List<RawPrimitive> Items { get; }
    }
}
