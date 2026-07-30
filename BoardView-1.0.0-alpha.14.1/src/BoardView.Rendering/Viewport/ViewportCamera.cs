using System.Windows;
using BoardView.Core.Geometry;

namespace BoardView.Rendering.Viewport;

/// <summary>
/// Maintains the native renderer camera and performs reversible transformations between
/// normalized board coordinates and WPF device-independent pixels.
/// </summary>
public sealed class ViewportCamera
{
    private const double MinimumZoom = 0.02D;
    private const double MaximumZoom = 500D;

    /// <summary>Gets the user zoom factor relative to the fitted document scale.</summary>
    public double Zoom { get; private set; } = 1D;

    /// <summary>Gets the current screen-space pan displacement.</summary>
    public Vector Pan { get; private set; }

    /// <summary>Resets the camera to the fitted document view.</summary>
    public void Reset()
    {
        Zoom = 1D;
        Pan = default;
    }

    /// <summary>Pans the camera by a screen-space displacement.</summary>
    public void PanBy(Vector delta) => Pan += delta;

    /// <summary>Sets the absolute screen-space pan displacement without changing zoom.</summary>
    public void SetPan(Vector value) => Pan = value;

    /// <summary>
    /// Applies zoom around a fixed screen point so that the board coordinate below the pointer
    /// remains stationary while the zoom changes.
    /// </summary>
    public void ZoomAt(
        Point screenPoint,
        double factor,
        Bounds2D documentBounds,
        Size viewportSize,
        double padding)
    {
        if (!double.IsFinite(factor) || factor <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }

        ViewportTransform before = CreateTransform(documentBounds, viewportSize, padding);
        Point2D anchoredWorldPoint = before.ToWorld(screenPoint);
        double nextZoom = Math.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
        if (Math.Abs(nextZoom - Zoom) <= double.Epsilon)
        {
            return;
        }

        Zoom = nextZoom;
        ViewportTransform after = CreateTransform(documentBounds, viewportSize, padding);
        Point movedScreenPoint = after.ToScreen(anchoredWorldPoint);
        Pan += screenPoint - movedScreenPoint;
    }

    /// <summary>Creates an immutable transform for one render frame.</summary>
    public ViewportTransform CreateTransform(Bounds2D documentBounds, Size viewportSize, double padding)
    {
        if (documentBounds.IsEmpty)
        {
            return ViewportTransform.Empty;
        }

        double availableWidth = Math.Max(1D, viewportSize.Width - (padding * 2D));
        double availableHeight = Math.Max(1D, viewportSize.Height - (padding * 2D));
        double fittedScale = Math.Min(
            availableWidth / Math.Max(documentBounds.Width, 0.000001D),
            availableHeight / Math.Max(documentBounds.Height, 0.000001D));
        double scale = Math.Max(0.000001D, fittedScale * Zoom);
        double renderedWidth = documentBounds.Width * scale;
        double renderedHeight = documentBounds.Height * scale;
        double offsetX = ((viewportSize.Width - renderedWidth) / 2D) + Pan.X;
        double offsetY = ((viewportSize.Height - renderedHeight) / 2D) + Pan.Y;

        return new ViewportTransform(scale, offsetX, offsetY, documentBounds);
    }
}
