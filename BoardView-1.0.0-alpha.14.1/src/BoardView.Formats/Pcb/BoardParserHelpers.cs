using System.Globalization;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Formats.Pcb;

internal static class BoardParserHelpers
{
    public static BoardDocument Create(string path)
    {
        BoardDocument d=new(Path.GetFileNameWithoutExtension(path),path);
        d.AddLayer(new BoardLayer("top-copper","Top Copper",LayerType.Copper,BoardSide.Top,10));
        d.AddLayer(new BoardLayer("bottom-copper","Bottom Copper",LayerType.Copper,BoardSide.Bottom,20));
        d.AddLayer(new BoardLayer("outline","Board Outline",LayerType.Outline,BoardSide.Both,30));
        d.AddLayer(new BoardLayer("drill","Drill",LayerType.Drill,BoardSide.Both,40));
        return d;
    }
    public static double Number(string text)=>double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
    public static string EnsureNet(BoardDocument d,string name)
    {
        string id="net-"+string.Concat(name.Select(c=>char.IsLetterOrDigit(c)?char.ToLowerInvariant(c):'-')).Trim('-');
        if(!d.Nets.Any(n=>n.Id==id)) d.AddNet(new BoardNet(id,name)); return id;
    }
    public static void AddOutline(BoardDocument d,IEnumerable<Point2D> pts,string id="outline-1")
    {
        Point2D[] p=pts.ToArray(); if(p.Length>=3)d.AddElement(new PolygonElement(id,"outline",p,false));
    }
}
