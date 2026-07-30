using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Core.Tools;

public sealed record MeasurementResult(Point2D Start, Point2D End, double Distance, double DeltaX, double DeltaY, double AngleDegrees);
public sealed record NetTraceResult(BoardNet Net, IReadOnlyList<BoardElement> Elements, Bounds2D Bounds);
public sealed record Annotation(string Id, Point2D Position, string Text, DateTimeOffset CreatedAt);

public sealed class MeasurementTool
{
    public MeasurementResult Measure(Point2D start, Point2D end)
    {
        double dx=end.X-start.X, dy=end.Y-start.Y;
        return new(start,end,Math.Sqrt(dx*dx+dy*dy),dx,dy,Math.Atan2(dy,dx)*180D/Math.PI);
    }
}

public sealed class LayerTool
{
    public void SetVisible(BoardDocument document,string layerId,bool visible)
    {
        BoardLayer layer=document.Layers.Single(l=>string.Equals(l.Id,layerId,StringComparison.Ordinal));
        layer.IsVisible=visible;
        foreach(BoardElement e in document.Elements.Where(e=>e.LayerId==layerId)) e.IsVisible=visible;
    }
}

public sealed class NetTool
{
    public NetTraceResult Trace(BoardDocument document,string netNameOrId)
    {
        BoardNet net=document.Nets.Single(n=>string.Equals(n.Id,netNameOrId,StringComparison.OrdinalIgnoreCase)||string.Equals(n.Name,netNameOrId,StringComparison.OrdinalIgnoreCase));
        BoardElement[] elements=document.Elements.Where(e=>string.Equals(e.NetId,net.Id,StringComparison.Ordinal)).ToArray();
        Bounds2D bounds=elements.Length==0?default:elements.Select(e=>e.Bounds).Aggregate((a,b)=>a.Union(b));
        return new(net,elements,bounds);
    }
}

public sealed class AnnotationStore
{
    private readonly List<Annotation> annotations=[];
    public IReadOnlyList<Annotation> Items=>annotations;
    public Annotation Add(Point2D position,string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Annotation item=new(Guid.NewGuid().ToString("N"),position,text.Trim(),DateTimeOffset.UtcNow); annotations.Add(item); return item;
    }
    public bool Remove(string id)=>annotations.RemoveAll(a=>a.Id==id)>0;
}
