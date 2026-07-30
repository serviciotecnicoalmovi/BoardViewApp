using System.Globalization;
using System.Windows;
using System.Windows.Media;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Core.Spatial;
using BoardView.Core.Recognition;
using BoardView.Rendering.Viewport;

namespace BoardView.Rendering.Engine;

/// <summary>
/// Stateless WPF renderer for the normalized <see cref="BoardDocument"/> model. It never reads
/// source-format data and never depends on WebView2 or a PDF renderer.
/// </summary>
public sealed class NativeBoardRenderer
{
    private static readonly Brush BackgroundBrush = Freeze(new SolidColorBrush(Color.FromRgb(11, 17, 24)));
    private static readonly Brush GridBrush = Freeze(new SolidColorBrush(Color.FromArgb(95, 46, 62, 77)));
    private static readonly Brush DefaultBrush = Freeze(new SolidColorBrush(Color.FromRgb(0, 207, 255)));
    private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(235, 242, 248)));
    private static readonly Brush TopBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 82, 98)));
    private static readonly Brush BottomBrush = Freeze(new SolidColorBrush(Color.FromRgb(70, 160, 255)));
    private static readonly Brush ImageBrush = Freeze(new SolidColorBrush(Color.FromArgb(70, 105, 125, 145)));
    private static readonly Brush DrillBrush = Freeze(new SolidColorBrush(Color.FromRgb(7, 15, 22)));
    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 193, 7)));
    private static readonly Pen SelectionPen = Freeze(new Pen(SelectionBrush, 2.2D));
    private static readonly Brush PadDiagnosticBrush = Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 193, 7)));
    private static readonly Brush ViaDiagnosticBrush = Freeze(new SolidColorBrush(Color.FromArgb(190, 177, 92, 255)));
    private static readonly Brush HoleDiagnosticBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 110, 110)));
    private static readonly Brush FootprintDiagnosticBrush = Freeze(new SolidColorBrush(Color.FromArgb(220, 255, 193, 7)));

    /// <summary>Builds the frame visibility list using the document spatial index.</summary>
    public NativeRenderFrame BuildFrame(BoardDocument document, ViewportTransform transform, Rect viewport)
    {
        ArgumentNullException.ThrowIfNull(document);
        Bounds2D visibleWorld = transform.ToWorld(viewport);
        IReadOnlyDictionary<string, BoardLayer> layers = document.Layers.ToDictionary(
            static layer => layer.Id,
            StringComparer.Ordinal);

        IReadOnlyList<BoardElement> elements = document.Query(
                BoardElementQuery.InArea(visibleWorld) with { VisibleOnly = true })
            .Hits
            .Select(static hit => hit.Item)
            .Where(element => layers.TryGetValue(element.LayerId, out BoardLayer? layer) && layer.IsVisible)
            .OrderBy(element => layers[element.LayerId].Order)
            .ThenBy(static element => element.Id, StringComparer.Ordinal)
            .ToArray();

        return new NativeRenderFrame(transform, elements, layers);
    }

    /// <summary>Draws the complete native model frame.</summary>
    public void Draw(
        DrawingContext context,
        Rect viewport,
        NativeRenderFrame frame,
        BoardElement? selectedElement,
        bool drawBackground,
        bool drawGrid,
        RecognitionResult recognition,
        bool showPads,
        bool showVias,
        bool showHoles,
        bool showFootprints)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frame);

        if (drawBackground)
        {
            context.DrawRectangle(BackgroundBrush, null, viewport);
        }

        if (drawGrid)
        {
            DrawGrid(context, viewport, frame.Transform);
        }

        foreach (BoardElement element in frame.VisibleElements)
        {
            Brush brush = ReferenceEquals(element, selectedElement)
                ? SelectionBrush
                : GetLayerBrush(frame.Layers[element.LayerId]);
            DrawElement(context, element, frame.Transform, brush);
        }

        DrawRecognitionDiagnostics(
            context, frame.Transform, recognition, showPads, showVias, showHoles, showFootprints);

        if (selectedElement is not null)
        {
            context.DrawRectangle(null, SelectionPen, frame.Transform.ToScreen(selectedElement.Bounds));
        }
    }

    /// <summary>Draws the empty native viewport message.</summary>
    public void DrawPlaceholder(DrawingContext context, Size viewportSize, string documentName)
    {
        ArgumentNullException.ThrowIfNull(context);
        FormattedText title = CreateText("BoardView", "Segoe UI Semibold", 38D, DefaultBrush);
        FormattedText subtitle = CreateText(documentName, "Segoe UI", 17D, TextBrush);
        context.DrawText(title, new Point((viewportSize.Width - title.Width) / 2D, (viewportSize.Height / 2D) - 58D));
        context.DrawText(subtitle, new Point((viewportSize.Width - subtitle.Width) / 2D, (viewportSize.Height / 2D) + 2D));
    }

    private static void DrawRecognitionDiagnostics(
        DrawingContext context,
        ViewportTransform transform,
        RecognitionResult recognition,
        bool showPads,
        bool showVias,
        bool showHoles,
        bool showFootprints)
    {
        if (showPads)
        {
            Pen pen = Freeze(new Pen(PadDiagnosticBrush, 1.1D));
            foreach (RecognizedPad pad in recognition.Pads)
            {
                Rect rectangle = transform.ToScreen(pad.Bounds);
                if (pad.Shape == PadShape.Circle)
                {
                    context.DrawEllipse(null, pen, transform.ToScreen(pad.Center), rectangle.Width / 2D, rectangle.Height / 2D);
                }
                else
                {
                    context.DrawRectangle(null, pen, rectangle);
                }
            }
        }

        if (showVias)
        {
            Pen pen = Freeze(new Pen(ViaDiagnosticBrush, 1.5D));
            foreach (RecognizedVia via in recognition.Vias)
            {
                Rect rectangle = transform.ToScreen(via.Bounds);
                context.DrawEllipse(null, pen, transform.ToScreen(via.Center), rectangle.Width / 2D, rectangle.Height / 2D);
            }
        }

        if (showHoles)
        {
            Pen pen = Freeze(new Pen(HoleDiagnosticBrush, 1.4D));
            foreach (RecognizedHole hole in recognition.Holes)
            {
                Rect rectangle = transform.ToScreen(hole.Bounds);
                Point center = transform.ToScreen(hole.Center);
                context.DrawEllipse(null, pen, center, rectangle.Width / 2D, rectangle.Height / 2D);
                context.DrawLine(pen, new Point(rectangle.Left, center.Y), new Point(rectangle.Right, center.Y));
                context.DrawLine(pen, new Point(center.X, rectangle.Top), new Point(center.X, rectangle.Bottom));
            }
        }

        if (showFootprints)
        {
            Pen pen = Freeze(new Pen(FootprintDiagnosticBrush, 1.4D) { DashStyle = DashStyles.Dash });
            foreach (RecognizedFootprint footprint in recognition.Footprints)
            {
                Rect bounds = transform.ToScreen(footprint.Bounds);
                context.DrawRectangle(null, pen, bounds);
                FormattedText label = CreateText(
                    $"{footprint.Classification} · {footprint.PadIds.Count} pads",
                    "Segoe UI Semibold",
                    10D,
                    FootprintDiagnosticBrush);
                context.DrawText(label, new Point(bounds.Left + 2D, bounds.Top - label.Height - 2D));
            }
        }
    }

    private static void DrawElement(DrawingContext context, BoardElement element, ViewportTransform transform, Brush brush)
    {
        switch (element)
        {
            case VectorLineElement line:
                context.DrawLine(CreatePen(brush, line.Width, transform), transform.ToScreen(line.Start), transform.ToScreen(line.End));
                break;
            case VectorPolylineElement polyline:
                DrawPolyline(context, polyline, transform, brush);
                break;
            case VectorBezierElement bezier:
                DrawBezier(context, bezier, transform, brush);
                break;
            case VectorEllipseElement ellipse:
                DrawEllipse(context, ellipse, transform, brush);
                break;
            case VectorRectangleElement rectangle:
                DrawRectangle(context, rectangle, transform, brush);
                break;
            case TextElement text:
                DrawText(context, text, transform, brush);
                break;
            case RasterImageElement image:
                context.DrawRectangle(ImageBrush, CreatePen(brush, 0.2D, transform), transform.ToScreen(image.Bounds));
                break;
            case TrackElement track:
                context.DrawLine(CreatePen(brush, track.Width, transform), transform.ToScreen(track.Start), transform.ToScreen(track.End));
                break;
            case ViaElement via:
                DrawVia(context, via, transform, brush);
                break;
            case PadElement pad:
                DrawPad(context, pad, transform, brush);
                break;
            case PolygonElement polygon:
                DrawPolygon(context, polygon, transform, brush);
                break;
            case ArcElement arc:
                DrawArc(context, arc, transform, brush);
                break;
            case DrillHoleElement drill:
                DrawDrill(context, drill, transform, brush);
                break;
        }
    }

    private static void DrawPolyline(DrawingContext context, VectorPolylineElement element, ViewportTransform transform, Brush brush)
    {
        if (element.Points.Count < 2)
        {
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(transform.ToScreen(element.Points[0]), false, element.IsClosed);
            geometryContext.PolyLineTo(element.Points.Skip(1).Select(transform.ToScreen).ToArray(), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(null, CreatePen(brush, element.Width, transform), geometry);
    }

    private static void DrawBezier(DrawingContext context, VectorBezierElement element, ViewportTransform transform, Brush brush)
    {
        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(transform.ToScreen(element.Start), false, false);
            geometryContext.BezierTo(
                transform.ToScreen(element.Control1),
                transform.ToScreen(element.Control2),
                transform.ToScreen(element.End),
                true,
                false);
        }

        geometry.Freeze();
        context.DrawGeometry(null, CreatePen(brush, element.Width, transform), geometry);
    }

    private static void DrawEllipse(DrawingContext context, VectorEllipseElement element, ViewportTransform transform, Brush brush)
    {
        Point center = transform.ToScreen(element.Center);
        Pen? pen = element.IsFilled ? null : CreatePen(brush, element.StrokeWidth, transform);
        context.DrawEllipse(
            element.IsFilled ? brush : null,
            pen,
            center,
            Math.Max(0.5D, element.RadiusX * transform.Scale),
            Math.Max(0.5D, element.RadiusY * transform.Scale));
    }

    private static void DrawRectangle(DrawingContext context, VectorRectangleElement element, ViewportTransform transform, Brush brush)
    {
        context.DrawRectangle(
            element.IsFilled ? brush : null,
            element.IsFilled ? null : CreatePen(brush, element.StrokeWidth, transform),
            transform.ToScreen(element.Rectangle));
    }

    private static void DrawText(DrawingContext context, TextElement element, ViewportTransform transform, Brush brush)
    {
        double size = Math.Clamp(element.Height * transform.Scale, 4D, 128D);
        FormattedText text = CreateText(element.Text, "Segoe UI", size, brush);
        Point origin = transform.ToScreen(element.Position);
        if (Math.Abs(element.RotationDegrees) > 0.01D)
        {
            context.PushTransform(new RotateTransform(-element.RotationDegrees, origin.X, origin.Y));
            context.DrawText(text, origin);
            context.Pop();
            return;
        }

        context.DrawText(text, origin);
    }

    private static void DrawVia(DrawingContext context, ViaElement via, ViewportTransform transform, Brush brush)
    {
        Point center = transform.ToScreen(via.Position);
        double radius = Math.Max(1D, via.Diameter * transform.Scale / 2D);
        double hole = Math.Max(0.7D, via.DrillDiameter * transform.Scale / 2D);
        context.DrawEllipse(brush, null, center, radius, radius);
        context.DrawEllipse(DrillBrush, null, center, hole, hole);
    }

    private static void DrawPad(DrawingContext context, PadElement pad, ViewportTransform transform, Brush brush)
    {
        Point center = transform.ToScreen(pad.Position);
        double width = Math.Max(1D, pad.Width * transform.Scale);
        double height = Math.Max(1D, pad.Height * transform.Scale);
        Rect rectangle = new(center.X - (width / 2D), center.Y - (height / 2D), width, height);

        switch (pad.Shape)
        {
            case PadShape.Circle:
                context.DrawEllipse(brush, null, center, width / 2D, height / 2D);
                break;
            case PadShape.Oval:
            case PadShape.RoundedRectangle:
                double radius = Math.Min(width, height) * 0.35D;
                context.DrawRoundedRectangle(brush, null, rectangle, radius, radius);
                break;
            default:
                context.DrawRectangle(brush, null, rectangle);
                break;
        }
    }

    private static void DrawPolygon(DrawingContext context, PolygonElement polygon, ViewportTransform transform, Brush brush)
    {
        if (polygon.Vertices.Count < 3)
        {
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(transform.ToScreen(polygon.Vertices[0]), polygon.IsFilled, true);
            geometryContext.PolyLineTo(polygon.Vertices.Skip(1).Select(transform.ToScreen).ToArray(), true, false);
        }

        geometry.Freeze();
        context.DrawGeometry(
            polygon.IsFilled ? brush : null,
            polygon.IsFilled ? null : CreatePen(brush, 0.2D, transform),
            geometry);
    }

    private static void DrawArc(DrawingContext context, ArcElement arc, ViewportTransform transform, Brush brush)
    {
        double startRadians = arc.StartAngleDegrees * Math.PI / 180D;
        double endRadians = (arc.StartAngleDegrees + arc.SweepAngleDegrees) * Math.PI / 180D;
        Point2D start = new(
            arc.Center.X + (Math.Cos(startRadians) * arc.Radius),
            arc.Center.Y + (Math.Sin(startRadians) * arc.Radius));
        Point2D end = new(
            arc.Center.X + (Math.Cos(endRadians) * arc.Radius),
            arc.Center.Y + (Math.Sin(endRadians) * arc.Radius));

        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(transform.ToScreen(start), false, false);
            geometryContext.ArcTo(
                transform.ToScreen(end),
                new Size(arc.Radius * transform.Scale, arc.Radius * transform.Scale),
                0D,
                Math.Abs(arc.SweepAngleDegrees) > 180D,
                arc.SweepAngleDegrees >= 0D ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                true,
                false);
        }

        geometry.Freeze();
        context.DrawGeometry(null, CreatePen(brush, arc.Width, transform), geometry);
    }

    private static void DrawDrill(DrawingContext context, DrillHoleElement drill, ViewportTransform transform, Brush brush)
    {
        Point center = transform.ToScreen(drill.Center);
        double radius = Math.Max(1D, drill.Diameter * transform.Scale / 2D);
        context.DrawEllipse(DrillBrush, CreatePen(brush, 0.15D, transform), center, radius, radius);
    }

    private static void DrawGrid(DrawingContext context, Rect viewport, ViewportTransform transform)
    {
        double spacing = Math.Max(0.001D, transform.Scale);
        double worldStep = ChooseGridStep(spacing);
        double firstWorldX = Math.Floor(transform.ToWorld(viewport.TopLeft).X / worldStep) * worldStep;
        double firstWorldY = Math.Floor(transform.ToWorld(viewport.TopLeft).Y / worldStep) * worldStep;
        Pen pen = Freeze(new Pen(GridBrush, 1D));

        for (double worldX = firstWorldX; ; worldX += worldStep)
        {
            double screenX = transform.ToScreen(new Point2D(worldX, transform.SourceBounds.Top)).X;
            if (screenX > viewport.Right)
            {
                break;
            }
            if (screenX >= viewport.Left)
            {
                context.DrawLine(pen, new Point(screenX, viewport.Top), new Point(screenX, viewport.Bottom));
            }
        }

        for (double worldY = firstWorldY; ; worldY += worldStep)
        {
            double screenY = transform.ToScreen(new Point2D(transform.SourceBounds.Left, worldY)).Y;
            if (screenY > viewport.Bottom)
            {
                break;
            }
            if (screenY >= viewport.Top)
            {
                context.DrawLine(pen, new Point(viewport.Left, screenY), new Point(viewport.Right, screenY));
            }
        }

    }

    private static double ChooseGridStep(double scale)
    {
        double[] steps = [0.1D, 0.2D, 0.5D, 1D, 2D, 5D, 10D, 20D, 50D, 100D, 200D, 500D, 1000D];
        return steps.FirstOrDefault(step => step * scale >= 22D, steps[^1]);
    }

    private static Pen CreatePen(Brush brush, double worldWidth, ViewportTransform transform)
    {
        Pen pen = new(brush, Math.Max(0.65D, worldWidth * transform.Scale))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        return Freeze(pen);
    }

    private static Brush GetLayerBrush(BoardLayer layer) => layer.Type switch
    {
        LayerType.Outline => TextBrush,
        _ when layer.Side == BoardSide.Top => TopBrush,
        _ when layer.Side == BoardSide.Bottom => BottomBrush,
        _ => DefaultBrush,
    };

    private static FormattedText CreateText(string text, string typeface, double size, Brush brush) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(typeface),
        size,
        brush,
        1D);

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
