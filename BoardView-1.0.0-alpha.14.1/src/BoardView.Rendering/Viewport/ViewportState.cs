using BoardView.Core.Geometry;

namespace BoardView.Rendering.Viewport;

/// <summary>Estado independiente de WPF para zoom, paneo, ajuste y transformación de coordenadas.</summary>
public sealed class ViewportState
{
    public double Zoom { get; private set; }=1D;
    public Vector2D Pan { get; private set; }=Vector2D.Zero;
    public double MinimumZoom { get; init; }=0.05D;
    public double MaximumZoom { get; init; }=100D;
    public void SetZoom(double zoom)=>Zoom=Math.Clamp(zoom,MinimumZoom,MaximumZoom);
    public void ZoomAt(double factor,Point2D anchor)
    {
        if(factor<=0D)throw new ArgumentOutOfRangeException(nameof(factor));double old=Zoom;SetZoom(Zoom*factor);double ratio=Zoom/old;Pan=new Vector2D(anchor.X-(anchor.X-Pan.X)*ratio,anchor.Y-(anchor.Y-Pan.Y)*ratio);
    }
    public void PanBy(Vector2D delta)=>Pan+=delta;
    public void Reset(){Zoom=1D;Pan=Vector2D.Zero;}
}
