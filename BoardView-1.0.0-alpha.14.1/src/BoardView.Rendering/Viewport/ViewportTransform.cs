using System.Windows;
using BoardView.Core.Geometry;

namespace BoardView.Rendering.Viewport;

/// <summary>Immutable world-to-screen transformation used during one render frame.</summary>
public readonly record struct ViewportTransform(
    double Scale,
    double OffsetX,
    double OffsetY,
    Bounds2D SourceBounds)
{
    /// <summary>Gets an empty transform used when no document is available.</summary>
    public static ViewportTransform Empty { get; } = new(1D, 0D, 0D, Bounds2D.Empty);

    /// <summary>Converts a normalized board point to screen coordinates.</summary>
    public Point ToScreen(Point2D point) => new(
        OffsetX + ((point.X - SourceBounds.Left) * Scale),
        OffsetY + ((point.Y - SourceBounds.Top) * Scale));

    /// <summary>Converts normalized board limits to a WPF rectangle.</summary>
    public Rect ToScreen(Bounds2D bounds)
    {
        Point topLeft = ToScreen(new Point2D(bounds.Left, bounds.Top));
        Point bottomRight = ToScreen(new Point2D(bounds.Right, bounds.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>Converts a screen point to normalized board coordinates.</summary>
    public Point2D ToWorld(Point point) => new(
        SourceBounds.Left + ((point.X - OffsetX) / Scale),
        SourceBounds.Top + ((point.Y - OffsetY) / Scale));

    /// <summary>Converts a screen rectangle to normalized board limits.</summary>
    public Bounds2D ToWorld(Rect rectangle)
    {
        Point2D first = ToWorld(rectangle.TopLeft);
        Point2D second = ToWorld(rectangle.BottomRight);
        return new Bounds2D(first.X, first.Y, second.X, second.Y);
    }
}
