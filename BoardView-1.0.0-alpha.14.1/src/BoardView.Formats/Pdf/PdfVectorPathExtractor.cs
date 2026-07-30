using System.Collections;
using System.Globalization;
using System.Reflection;
using BoardView.Core.Documents.Common;
using BoardView.Core.Geometry;
using BoardView.Core.Graphics;
using BoardView.GeometryKernel;
using BoardView.GeometryKernel.Graph;
using BoardView.GeometryKernel.Primitives;
using UglyToad.PdfPig.Content;

namespace BoardView.Formats.Pdf;

/// <summary>
/// Traduce los caminos vectoriales públicos de PdfPig al modelo gráfico común.
/// La extracción divide correctamente una subruta cuando aparecen varios comandos
/// MoveTo y normaliza cada contorno antes de publicarlo en el documento técnico.
/// </summary>
internal static class PdfVectorPathExtractor
{
    private const double PointTolerance = 0.0001D;
    private const double ContourJoinTolerance = 0.002D;

    public static PdfVectorExtractionResult Extract(Page sourcePage, DocumentPage targetPage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePage);
        ArgumentNullException.ThrowIfNull(targetPage);

        int pathCount = 0;
        int lineCount = 0;
        int polylineCount = 0;
        int rectangleCount = 0;
        int circleCount = 0;
        int bezierCount = 0;
        List<GeometrySegment> pageSegments = [];
        Dictionary<string, PathStyle> styles = new(StringComparer.Ordinal);

        foreach (object sourcePath in sourcePage.Paths.Cast<object>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pathCount++;

            PathStyle style = new(
                ToMillimeters(ReadDouble(sourcePath, "LineWidth", "StrokeWidth")),
                ReadBoolean(sourcePath, true, "IsStroked", "Stroked"),
                ReadBoolean(sourcePath, false, "IsFilled", "Filled"),
                ReadColor(sourcePath, "StrokeColor", "StrokingColor"),
                ReadColor(sourcePath, "FillColor", "NonStrokingColor"));
            string styleKey = style.CreateKey();
            styles.TryAdd(styleKey, style);

            List<ContourData> pathContours = [];
            IEnumerable subpaths = ReadEnumerable(sourcePath, "Subpaths", "Paths");
            foreach (object sourceSubpath in subpaths.Cast<object>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadSubpathContours(sourceSubpath, pathContours);
            }

            int contourIndex = 0;
            foreach (ContourData contour in pathContours)
            {
                cancellationToken.ThrowIfCancellationRequested();
                contourIndex++;
                string id = $"page-{sourcePage.Number}-path-{pathCount}-contour-{contourIndex}";

                if (contour.Beziers.Count > 0)
                {
                    ExtractionCounters counters = PublishContour(
                        id,
                        contour,
                        targetPage,
                        style.Width,
                        style.IsStroked,
                        style.IsFilled,
                        style.StrokeColor,
                        style.FillColor);
                    lineCount += counters.Lines;
                    polylineCount += counters.Polylines;
                    rectangleCount += counters.Rectangles;
                    circleCount += counters.Circles;
                    bezierCount += counters.Beziers;
                    continue;
                }

                AddContourSegments(id, contour, styleKey, pageSegments);
            }
        }

        PageGeometryKernel kernel = new(new GeometryKernelOptions
        {
            SnapTolerance = 0.01D,
            AngularTolerance = 0.035D,
            MinimumEdgeLength = 0.005D,
            MinimumRectangleArea = 0.00005D,
        });
        GeometryKernelResult kernelResult = kernel.Build(pageSegments, cancellationToken);

        foreach (KernelRectangle kernelRectangle in kernelResult.Rectangles)
        {
            PathStyle style = styles[kernelRectangle.GroupKey];
            if (kernelRectangle.IsAxisAligned)
            {
                RectangleGraphic rectangle = new(
                    kernelRectangle.Id,
                    kernelRectangle.Bounds,
                    style.Width,
                    style.IsFilled);
                ApplyStyle(rectangle, style.IsStroked, style.IsFilled, style.StrokeColor, style.FillColor, "rectangle");
                rectangle.Metadata.Set("geometry-kernel.source-segments", string.Join(",", kernelRectangle.SourceSegmentIds));
                rectangle.Metadata.Set("geometry-kernel.normalized", "true");
                targetPage.AddGraphic(rectangle);
                rectangleCount++;
            }
            else
            {
                PolylineGraphic rotatedRectangle = new(
                    kernelRectangle.Id,
                    kernelRectangle.Corners,
                    style.Width,
                    true);
                ApplyStyle(rotatedRectangle, style.IsStroked, style.IsFilled, style.StrokeColor, style.FillColor, "rotated-rectangle");
                rotatedRectangle.Metadata.Set("geometry-kernel.source-segments", string.Join(",", kernelRectangle.SourceSegmentIds));
                rotatedRectangle.Metadata.Set("geometry-kernel.normalized", "true");
                targetPage.AddGraphic(rotatedRectangle);
                polylineCount++;
            }
        }

        foreach (GeometrySegment segment in kernelResult.RemainingSegments)
        {
            PathStyle style = styles[segment.GroupKey];
            LineGraphic line = new(segment.Id, segment.Start, segment.End, style.Width);
            ApplyStyle(line, style.IsStroked, style.IsFilled, style.StrokeColor, style.FillColor, "line");
            targetPage.AddGraphic(line);
            lineCount++;
        }

        targetPage.Metadata.Set("geometry-kernel.input-segments", kernelResult.Diagnostics.InputSegmentCount.ToString(CultureInfo.InvariantCulture));
        targetPage.Metadata.Set("geometry-kernel.nodes", kernelResult.Diagnostics.NodeCount.ToString(CultureInfo.InvariantCulture));
        targetPage.Metadata.Set("geometry-kernel.edges", kernelResult.Diagnostics.EdgeCount.ToString(CultureInfo.InvariantCulture));
        targetPage.Metadata.Set("geometry-kernel.cycles", kernelResult.Diagnostics.FourEdgeCycleCount.ToString(CultureInfo.InvariantCulture));
        targetPage.Metadata.Set("geometry-kernel.rectangles", kernelResult.Diagnostics.AcceptedRectangleCount.ToString(CultureInfo.InvariantCulture));
        targetPage.Metadata.Set("geometry-kernel.rejected-cycles", kernelResult.Diagnostics.RejectedCycleCount.ToString(CultureInfo.InvariantCulture));
        targetPage.Metadata.Set("geometry-kernel.remaining-segments", kernelResult.Diagnostics.RemainingSegmentCount.ToString(CultureInfo.InvariantCulture));

        return new PdfVectorExtractionResult(pathCount, lineCount, polylineCount, rectangleCount, circleCount, bezierCount);
    }

    private static void AddContourSegments(
        string contourId,
        ContourData contour,
        string styleKey,
        ICollection<GeometrySegment> destination)
    {
        if (contour.Points.Count < 2)
        {
            return;
        }

        int segmentNumber = 0;
        for (int index = 1; index < contour.Points.Count; index++)
        {
            Point2D start = contour.Points[index - 1];
            Point2D end = contour.Points[index];
            if (AreEqual(start, end))
            {
                continue;
            }

            segmentNumber++;
            destination.Add(new GeometrySegment($"{contourId}-segment-{segmentNumber}", start, end, styleKey));
        }

        if (contour.IsClosed && !AreEqual(contour.Points[0], contour.Points[^1]))
        {
            segmentNumber++;
            destination.Add(new GeometrySegment(
                $"{contourId}-segment-{segmentNumber}",
                contour.Points[^1],
                contour.Points[0],
                styleKey));
        }
    }

    /// <summary>
    /// Lee una subruta completa sin publicar segmentos. La clasificación geométrica se
    /// realiza posteriormente, cuando todas las subrutas del mismo PdfPath están disponibles.
    /// Esto permite reconstruir contornos que PdfPig expone como varios segmentos abiertos.
    /// </summary>
    private static void ReadSubpathContours(object sourceSubpath, ICollection<ContourData> destination)
    {
        ContourData current = new();
        Point2D? currentPoint = null;

        IEnumerable commands = ReadEnumerable(sourceSubpath, "Commands", "Operations");
        foreach (object command in commands.Cast<object>())
        {
            string commandName = command.GetType().Name;

            if (commandName.Contains("Move", StringComparison.OrdinalIgnoreCase) ||
                commandName.Contains("Begin", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadCommandPoint(command, out Point2D movePoint))
                {
                    FlushContour(current, destination);
                    current = new ContourData();
                    current.Points.Add(movePoint);
                    current.StartPoint = movePoint;
                    currentPoint = movePoint;
                }

                continue;
            }

            if (commandName.Contains("Close", StringComparison.OrdinalIgnoreCase))
            {
                current.IsClosed = true;
                if (current.StartPoint.HasValue && current.Points.Count > 1 &&
                    !AreEqual(current.Points[^1], current.StartPoint.Value))
                {
                    current.Points.Add(current.StartPoint.Value);
                }

                currentPoint = current.StartPoint;
                continue;
            }

            if (commandName.Contains("Bezier", StringComparison.OrdinalIgnoreCase) ||
                commandName.Contains("Curve", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadBezier(command, currentPoint, out Point2D curveStart, out Point2D control1, out Point2D control2, out Point2D curveEnd))
                {
                    current.StartPoint ??= curveStart;
                    current.Beziers.Add(new BezierData(curveStart, control1, control2, curveEnd));
                    currentPoint = curveEnd;
                }

                continue;
            }

            if (TryReadLineSegment(command, currentPoint, out Point2D segmentStart, out Point2D segmentEnd))
            {
                if (current.Points.Count == 0)
                {
                    current.Points.Add(segmentStart);
                    current.StartPoint ??= segmentStart;
                }
                else if (!AreEqual(current.Points[^1], segmentStart))
                {
                    current.Points.Add(segmentStart);
                }

                current.Points.Add(segmentEnd);
                currentPoint = segmentEnd;
                continue;
            }

            if (TryReadCommandPoint(command, out Point2D point))
            {
                if (current.Points.Count == 0 && currentPoint.HasValue)
                {
                    current.Points.Add(currentPoint.Value);
                    current.StartPoint ??= currentPoint.Value;
                }

                current.Points.Add(point);
                current.StartPoint ??= point;
                currentPoint = point;
            }
        }

        bool declaredClosed = ReadBoolean(sourceSubpath, false, "IsClosed", "Closed");
        if (declaredClosed)
        {
            current.IsClosed = true;
        }

        FlushContour(current, destination);
    }

    /// <summary>
    /// Reconstruye primero los contornos lineales del PdfPath y solo después permite
    /// clasificarlos. Los contornos Bézier y los ya cerrados se conservan sin cambios.
    /// </summary>
    private static IReadOnlyList<ContourData> AssemblePathContours(
        IReadOnlyList<ContourData> source,
        PdfLinearContourAssembler assembler)
    {
        List<ContourData> result = [];
        List<PdfLinearContour> linearContours = [];

        foreach (ContourData contour in source)
        {
            if (contour.Beziers.Count > 0 || contour.IsClosed)
            {
                result.Add(contour);
                continue;
            }

            if (contour.Points.Count >= 2)
            {
                linearContours.Add(new PdfLinearContour(contour.Points.ToArray(), false));
            }
        }

        foreach (PdfAssembledContour assembled in assembler.Assemble(linearContours))
        {
            ContourData contour = new()
            {
                IsClosed = assembled.IsClosed,
                StartPoint = assembled.Points.Count > 0 ? assembled.Points[0] : null,
            };
            contour.Points.AddRange(assembled.Points);
            result.Add(contour);
        }

        return result;
    }

    private static void FlushContour(ContourData contour, ICollection<ContourData> destination)
    {
        if (contour.Points.Count < 2 && contour.Beziers.Count == 0)
        {
            return;
        }

        if (contour.IsClosed && contour.StartPoint.HasValue && contour.Points.Count > 1 &&
            !AreEqual(contour.Points[^1], contour.StartPoint.Value))
        {
            contour.Points.Add(contour.StartPoint.Value);
        }

        destination.Add(contour);
    }

    private static ExtractionCounters PublishContour(
        string id,
        ContourData contour,
        DocumentPage targetPage,
        double width,
        bool isStroked,
        bool isFilled,
        string? strokeColor,
        string? fillColor)
    {
        List<BezierGraphic> beziers = contour.Beziers
            .Select((curve, index) => new BezierGraphic(
                $"{id}-bezier-{index + 1}",
                curve.Start,
                curve.Control1,
                curve.Control2,
                curve.End,
                width))
            .ToList();

        if (TryCreateCircle(id, beziers, width, isFilled, out CircleGraphic circle))
        {
            ApplyStyle(circle, isStroked, isFilled, strokeColor, fillColor, "circle");
            circle.Metadata.Set("pdf.normalized-from", "bezier-path");
            targetPage.AddGraphic(circle);
            return new ExtractionCounters(0, 0, 0, 1, 0);
        }

        foreach (BezierGraphic bezier in beziers)
        {
            ApplyStyle(bezier, isStroked, isFilled, strokeColor, fillColor, "bezier");
            targetPage.AddGraphic(bezier);
        }

        if (contour.Points.Count < 2)
        {
            return new ExtractionCounters(0, 0, 0, 0, beziers.Count);
        }

        PdfGeometryNormalizer normalizer = new();
        if (normalizer.TryCreateRectangle(id, contour.Points, width, isFilled, contour.IsClosed, out RectangleGraphic rectangle))
        {
            ApplyStyle(rectangle, isStroked, isFilled, strokeColor, fillColor, "rectangle");
            rectangle.Metadata.Set("pdf.normalized-from", "closed-polyline");
            targetPage.AddGraphic(rectangle);
            return new ExtractionCounters(0, 0, 1, 0, beziers.Count);
        }

        IReadOnlyList<Point2D> normalizedPoints = contour.IsClosed &&
                                                   contour.Points.Count > 1 &&
                                                   AreEqual(contour.Points[0], contour.Points[^1])
            ? contour.Points.Take(contour.Points.Count - 1).ToArray()
            : contour.Points.ToArray();

        if (normalizedPoints.Count == 2)
        {
            LineGraphic line = new(id, normalizedPoints[0], normalizedPoints[1], width);
            ApplyStyle(line, isStroked, isFilled, strokeColor, fillColor, "line");
            targetPage.AddGraphic(line);
            return new ExtractionCounters(1, 0, 0, 0, beziers.Count);
        }

        PolylineGraphic polyline = new(id, normalizedPoints, width, contour.IsClosed);
        ApplyStyle(polyline, isStroked, isFilled, strokeColor, fillColor, contour.IsClosed ? "polygon" : "polyline");
        if (contour.IsClosed)
        {
            polyline.Metadata.Set("pdf.normalized-kind", "polygon");
        }

        targetPage.AddGraphic(polyline);
        return new ExtractionCounters(0, 1, 0, 0, beziers.Count);
    }

    private static bool TryCreateCircle(
        string id,
        IReadOnlyList<BezierGraphic> beziers,
        double strokeWidth,
        bool isFilled,
        out CircleGraphic circle)
    {
        circle = null!;
        if (beziers.Count != 4)
        {
            return false;
        }

        List<Point2D> endpoints = beziers
            .SelectMany(static curve => new[] { curve.Start, curve.End })
            .ToList();
        Bounds2D bounds = Bounds2D.FromPoints(endpoints);
        if (bounds.Width <= PointTolerance || bounds.Height <= PointTolerance)
        {
            return false;
        }

        double dimensionTolerance = Math.Max(bounds.Width, bounds.Height) * 0.02D;
        if (Math.Abs(bounds.Width - bounds.Height) > dimensionTolerance)
        {
            return false;
        }

        for (int index = 0; index < beziers.Count; index++)
        {
            BezierGraphic current = beziers[index];
            BezierGraphic next = beziers[(index + 1) % beziers.Count];
            if (!AreEqual(current.End, next.Start))
            {
                return false;
            }
        }

        Point2D center = new((bounds.Left + bounds.Right) / 2D, (bounds.Top + bounds.Bottom) / 2D);
        double radius = (bounds.Width + bounds.Height) / 4D;
        double radialTolerance = Math.Max(PointTolerance, radius * 0.03D);
        foreach (Point2D endpoint in endpoints)
        {
            double distance = Math.Sqrt(Math.Pow(endpoint.X - center.X, 2D) + Math.Pow(endpoint.Y - center.Y, 2D));
            if (Math.Abs(distance - radius) > radialTolerance)
            {
                return false;
            }
        }

        circle = new CircleGraphic(id, center, radius, strokeWidth, isFilled);
        return true;
    }

    private static bool TryReadLineSegment(object command, Point2D? currentPoint, out Point2D start, out Point2D end)
    {
        start = default;
        end = default;

        string name = command.GetType().Name;
        if (!name.Contains("Line", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool hasStart = TryReadPointProperty(command, out start, "Start", "From", "StartPoint", "Point1");
        if (!hasStart && currentPoint.HasValue)
        {
            start = currentPoint.Value;
            hasStart = true;
        }

        bool hasEnd = TryReadPointProperty(command, out end, "End", "To", "EndPoint", "Point2", "Location", "Point");
        return hasStart && hasEnd;
    }

    private static bool TryReadBezier(object command, Point2D? currentPoint, out Point2D start, out Point2D control1, out Point2D control2, out Point2D end)
    {
        start = currentPoint ?? default;
        control1 = default;
        control2 = default;
        end = default;

        bool hasStart = TryReadPointProperty(command, out start, "Start", "From", "StartPoint");
        if (!hasStart && currentPoint.HasValue)
        {
            start = currentPoint.Value;
            hasStart = true;
        }

        bool hasControl1 = TryReadPointProperty(command, out control1, "Control1", "ControlPoint1", "FirstControlPoint", "Point1");
        bool hasControl2 = TryReadPointProperty(command, out control2, "Control2", "ControlPoint2", "SecondControlPoint", "Point2");
        bool hasEnd = TryReadPointProperty(command, out end, "End", "To", "EndPoint", "Location", "Point");
        return hasStart && hasControl1 && hasControl2 && hasEnd;
    }

    private static bool TryReadCommandPoint(object command, out Point2D point) =>
        TryReadPointProperty(command, out point, "Location", "Point", "To", "End", "EndPoint", "Destination");

    private static bool TryReadPointProperty(object source, out Point2D point, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(source) is object value && TryConvertPoint(value, out point))
            {
                return true;
            }
        }

        point = default;
        return false;
    }

    private static bool TryConvertPoint(object source, out Point2D point)
    {
        PropertyInfo? xProperty = source.GetType().GetProperty("X", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo? yProperty = source.GetType().GetProperty("Y", BindingFlags.Instance | BindingFlags.Public);
        if (xProperty?.GetValue(source) is object xValue && yProperty?.GetValue(source) is object yValue)
        {
            point = new Point2D(
                ToMillimeters(Convert.ToDouble(xValue, CultureInfo.InvariantCulture)),
                ToMillimeters(Convert.ToDouble(yValue, CultureInfo.InvariantCulture)));
            return true;
        }

        point = default;
        return false;
    }

    private static IEnumerable ReadEnumerable(object source, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(source) is IEnumerable enumerable)
            {
                return enumerable;
            }
        }

        return source is IEnumerable enumerableSource && source is not string
            ? enumerableSource
            : Array.Empty<object>();
    }

    private static double ReadDouble(object source, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(source) is object value)
            {
                return Math.Max(0D, Convert.ToDouble(value, CultureInfo.InvariantCulture));
            }
        }

        return 0D;
    }

    private static bool ReadBoolean(object source, bool defaultValue, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(source) is bool value)
            {
                return value;
            }
        }

        return defaultValue;
    }

    private static string? ReadColor(object source, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? property = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            object? color = property?.GetValue(source);
            if (color is null)
            {
                continue;
            }

            MethodInfo? rgbMethod = color.GetType().GetMethod("ToRGBValues", BindingFlags.Instance | BindingFlags.Public, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (rgbMethod?.Invoke(color, null) is IEnumerable values)
            {
                double[] components = values.Cast<object>()
                    .Select(value => Convert.ToDouble(value, CultureInfo.InvariantCulture))
                    .Take(3)
                    .ToArray();
                if (components.Length == 3)
                {
                    int red = NormalizeColorComponent(components[0]);
                    int green = NormalizeColorComponent(components[1]);
                    int blue = NormalizeColorComponent(components[2]);
                    return $"#{red:X2}{green:X2}{blue:X2}";
                }
            }

            return color.ToString();
        }

        return null;
    }

    private static int NormalizeColorComponent(double value)
    {
        double normalized = value <= 1D ? value * 255D : value;
        return (int)Math.Round(Math.Clamp(normalized, 0D, 255D));
    }

    private static void ApplyStyle(
        GraphicObject graphic,
        bool isStroked,
        bool isFilled,
        string? strokeColor,
        string? fillColor,
        string sourceKind)
    {
        graphic.Metadata.Set("source.format", "pdf");
        graphic.Metadata.Set("source.kind", sourceKind);
        graphic.Metadata.Set("pdf.is-stroked", isStroked.ToString(CultureInfo.InvariantCulture));
        graphic.Metadata.Set("pdf.is-filled", isFilled.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(strokeColor))
        {
            graphic.Metadata.Set("pdf.stroke-color", strokeColor);
        }

        if (!string.IsNullOrWhiteSpace(fillColor))
        {
            graphic.Metadata.Set("pdf.fill-color", fillColor);
        }
    }

    private static bool AreEqual(Point2D first, Point2D second) =>
        Math.Abs(first.X - second.X) <= PointTolerance &&
        Math.Abs(first.Y - second.Y) <= PointTolerance;

    private static double ToMillimeters(double pdfPoints) =>
        UnitConverter.Convert(pdfPoints, MeasurementUnit.PdfPoint, MeasurementUnit.Millimeter);

    private sealed record PathStyle(
        double Width,
        bool IsStroked,
        bool IsFilled,
        string? StrokeColor,
        string? FillColor)
    {
        public string CreateKey() => FormattableString.Invariant(
            $"{Width:R}|{IsStroked}|{IsFilled}|{StrokeColor ?? string.Empty}|{FillColor ?? string.Empty}");
    }

    private sealed class ContourData
    {
        public List<Point2D> Points { get; } = [];
        public List<BezierData> Beziers { get; } = [];
        public Point2D? StartPoint { get; set; }
        public bool IsClosed { get; set; }
    }

    private readonly record struct BezierData(Point2D Start, Point2D Control1, Point2D Control2, Point2D End);

    private readonly record struct ExtractionCounters(int Lines, int Polylines, int Rectangles, int Circles, int Beziers)
    {
        public static ExtractionCounters operator +(ExtractionCounters left, ExtractionCounters right) =>
            new(
                left.Lines + right.Lines,
                left.Polylines + right.Polylines,
                left.Rectangles + right.Rectangles,
                left.Circles + right.Circles,
                left.Beziers + right.Beziers);
    }
}

internal readonly record struct PdfVectorExtractionResult(
    int PathCount,
    int LineCount,
    int PolylineCount,
    int RectangleCount,
    int CircleCount,
    int BezierCount)
{
    public int GraphicCount => LineCount + PolylineCount + RectangleCount + CircleCount + BezierCount;
}
