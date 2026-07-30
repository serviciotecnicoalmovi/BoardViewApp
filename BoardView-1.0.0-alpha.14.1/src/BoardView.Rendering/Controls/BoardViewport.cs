using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Core.Spatial;
using BoardView.Core.Recognition;
using BoardView.Rendering.Engine;
using BoardView.Rendering.Viewport;

namespace BoardView.Rendering.Controls;

/// <summary>
/// Native WPF board surface. It consumes only <see cref="BoardDocument"/> data and remains
/// completely independent from PDF, WebView2 and source-format rendering engines.
/// </summary>
public sealed class BoardViewport : FrameworkElement
{
    private const double ViewPadding = 42D;
    private readonly ViewportCamera camera = new();
    private readonly NativeBoardRenderer renderer = new();
    private Point dragOrigin;
    private Vector panOrigin;
    private bool isPanning;
    private BoardElement? selectedElement;

    /// <summary>Identifies the normalized board document dependency property.</summary>
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(BoardDocument),
        typeof(BoardViewport),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDocumentChanged));

    /// <summary>Identifies the document name dependency property.</summary>
    public static readonly DependencyProperty DocumentNameProperty = DependencyProperty.Register(
        nameof(DocumentName),
        typeof(string),
        typeof(BoardViewport),
        new FrameworkPropertyMetadata("Ningún archivo abierto", FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Identifies the grid visibility dependency property.</summary>
    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid),
        typeof(bool),
        typeof(BoardViewport),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RecognitionResultProperty = DependencyProperty.Register(
        nameof(RecognitionResult), typeof(RecognitionResult), typeof(BoardViewport),
        new FrameworkPropertyMetadata(RecognitionResult.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowDetectedPadsProperty = RegisterDiagnosticFlag(nameof(ShowDetectedPads));
    public static readonly DependencyProperty ShowDetectedViasProperty = RegisterDiagnosticFlag(nameof(ShowDetectedVias));
    public static readonly DependencyProperty ShowDetectedHolesProperty = RegisterDiagnosticFlag(nameof(ShowDetectedHoles));
    public static readonly DependencyProperty ShowRecognizedFootprintsProperty = RegisterDiagnosticFlag(nameof(ShowRecognizedFootprints));

    /// <summary>Identifies the composition mode dependency property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode),
        typeof(BoardViewportMode),
        typeof(BoardViewport),
        new FrameworkPropertyMetadata(BoardViewportMode.Model, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Initializes the native board viewport.</summary>
    public BoardViewport()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    /// <summary>Gets or sets the normalized model rendered by this surface.</summary>
    public BoardDocument? Document
    {
        get => (BoardDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Gets or sets the user-facing document name.</summary>
    public string DocumentName
    {
        get => (string)GetValue(DocumentNameProperty);
        set => SetValue(DocumentNameProperty, value);
    }

    /// <summary>Gets or sets whether the adaptive native grid is visible.</summary>
    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>Gets or sets whether the native surface is opaque or transparent.</summary>
    public BoardViewportMode Mode
    {
        get => (BoardViewportMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }


    /// <summary>Gets or sets the recognized high-level electronic model.</summary>
    public RecognitionResult RecognitionResult
    {
        get => (RecognitionResult)GetValue(RecognitionResultProperty);
        set => SetValue(RecognitionResultProperty, value);
    }

    public bool ShowDetectedPads { get => (bool)GetValue(ShowDetectedPadsProperty); set => SetValue(ShowDetectedPadsProperty, value); }
    public bool ShowDetectedVias { get => (bool)GetValue(ShowDetectedViasProperty); set => SetValue(ShowDetectedViasProperty, value); }
    public bool ShowDetectedHoles { get => (bool)GetValue(ShowDetectedHolesProperty); set => SetValue(ShowDetectedHolesProperty, value); }
    public bool ShowRecognizedFootprints { get => (bool)GetValue(ShowRecognizedFootprintsProperty); set => SetValue(ShowRecognizedFootprintsProperty, value); }

    /// <summary>Gets the currently selected normalized model element.</summary>
    public BoardElement? SelectedElement => selectedElement;

    /// <summary>Fits the entire document, clears pan and selection, and redraws the viewport.</summary>
    public void FitToDocument()
    {
        camera.Reset();
        selectedElement = null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect viewport = new(0D, 0D, ActualWidth, ActualHeight);
        bool drawBackground = Mode == BoardViewportMode.Model;

        if (Document is null || Document.Elements.Count == 0 || Document.Bounds.IsEmpty)
        {
            if (drawBackground)
            {
                drawingContext.DrawRectangle(
                    new SolidColorBrush(Color.FromRgb(11, 17, 24)),
                    null,
                    viewport);
                renderer.DrawPlaceholder(drawingContext, RenderSize, DocumentName);
            }
            return;
        }

        ViewportTransform transform = camera.CreateTransform(Document.Bounds, RenderSize, ViewPadding);
        NativeRenderFrame frame = renderer.BuildFrame(Document, transform, viewport);
        renderer.Draw(
            drawingContext,
            viewport,
            frame,
            selectedElement,
            drawBackground,
            ShowGrid && drawBackground,
            RecognitionResult,
            ShowDetectedPads,
            ShowDetectedVias,
            ShowDetectedHoles,
            ShowRecognizedFootprints);
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Document is null)
        {
            return;
        }

        camera.ZoomAt(
            e.GetPosition(this),
            e.Delta > 0 ? 1.18D : 1D / 1.18D,
            Document.Bounds,
            RenderSize,
            ViewPadding);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        if (e.ClickCount == 2)
        {
            FitToDocument();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.IsKeyDown(Key.Space))
        {
            BeginPan(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        SelectAt(e.GetPosition(this));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        BeginPan(e.GetPosition(this));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!isPanning)
        {
            return;
        }

        Point current = e.GetPosition(this);
        Vector nextPan = panOrigin + (current - dragOrigin);
        camera.SetPan(nextPan);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) => EndPan(e);

    /// <inheritdoc />
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => EndPan(e);

    private static DependencyProperty RegisterDiagnosticFlag(string name) => DependencyProperty.Register(
        name, typeof(bool), typeof(BoardViewport),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnDocumentChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        _ = e;
        ((BoardViewport)dependencyObject).FitToDocument();
    }

    private void BeginPan(Point point)
    {
        dragOrigin = point;
        panOrigin = camera.Pan;
        isPanning = true;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
    }

    private void EndPan(MouseButtonEventArgs e)
    {
        if (!isPanning)
        {
            return;
        }

        isPanning = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void SelectAt(Point screenPoint)
    {
        if (Document is null || Document.Bounds.IsEmpty)
        {
            return;
        }

        ViewportTransform transform = camera.CreateTransform(Document.Bounds, RenderSize, ViewPadding);
        Point2D world = transform.ToWorld(screenPoint);
        double tolerance = 6D / transform.Scale;
        selectedElement = Document.Query(
                BoardElementQuery.Near(world, tolerance) with
                {
                    VisibleOnly = true,
                    MaximumResults = 32,
                })
            .Hits
            .OrderBy(hit => DistanceToBounds(world, hit.Item.Bounds))
            .ThenByDescending(hit => ResolveLayerOrder(Document, hit.Item.LayerId))
            .Select(static hit => hit.Item)
            .FirstOrDefault();
        InvalidateVisual();
    }

    private static int ResolveLayerOrder(BoardDocument document, string layerId) =>
        document.TryGetLayer(layerId, out BoardLayer? layer) && layer is not null ? layer.Order : int.MinValue;

    private static double DistanceToBounds(Point2D point, Bounds2D bounds)
    {
        double x = Math.Max(bounds.Left, Math.Min(point.X, bounds.Right));
        double y = Math.Max(bounds.Top, Math.Min(point.Y, bounds.Bottom));
        double dx = point.X - x;
        double dy = point.Y - y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
