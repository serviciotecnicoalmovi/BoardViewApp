using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Core.Search;

public enum SearchField { Any, Reference, Value, Net, Layer, ElementId, Coordinate, Property }
public sealed record SearchRequest(string Text, SearchField Field = SearchField.Any, Point2D? Coordinate = null, double Tolerance = 0.5D);
public sealed record SearchHit(string Kind, string Id, string Label, Point2D? Position, Bounds2D? Bounds, double Score);

/// <summary>Búsqueda determinista sobre el modelo normalizado, sin depender del formato fuente.</summary>
public sealed class DocumentSearchEngine
{
    public IReadOnlyList<SearchHit> Search(BoardDocument document, SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(request);
        string term=request.Text?.Trim()??string.Empty;
        List<SearchHit> hits=[];
        bool Match(string value)=>term.Length==0 || value.Contains(term,StringComparison.OrdinalIgnoreCase);

        if (request.Field is SearchField.Any or SearchField.Reference or SearchField.Value)
            foreach (BoardComponent c in document.Components)
                if ((request.Field!=SearchField.Value && Match(c.Reference)) || (request.Field!=SearchField.Reference && Match(c.Value)))
                    hits.Add(new("Component",c.Id,$"{c.Reference} {c.Value}".Trim(),c.Position,null,100));

        if (request.Field is SearchField.Any or SearchField.Net)
            foreach (BoardNet n in document.Nets) if (Match(n.Name)) hits.Add(new("Net",n.Id,n.Name,null,null,90));

        if (request.Field is SearchField.Any or SearchField.Layer)
            foreach (BoardLayer l in document.Layers) if (Match(l.Name)) hits.Add(new("Layer",l.Id,l.Name,null,null,80));

        foreach (BoardElement e in document.Elements)
        {
            bool idMatch=(request.Field is SearchField.Any or SearchField.ElementId) && Match(e.Id);
            bool coordMatch=request.Coordinate is Point2D p && Contains(e.Bounds,p,request.Tolerance);
            if (idMatch || coordMatch) hits.Add(new("Element",e.Id,e.Id,Center(e.Bounds),e.Bounds,coordMatch?110:70));
        }
        return hits.OrderByDescending(static h=>h.Score).ThenBy(static h=>h.Label,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static bool Contains(Bounds2D b,Point2D p,double t)=>p.X>=b.Left-t&&p.X<=b.Right+t&&p.Y>=b.Top-t&&p.Y<=b.Bottom+t;
    private static Point2D Center(Bounds2D b)=>new((b.Left+b.Right)/2D,(b.Top+b.Bottom)/2D);
}
