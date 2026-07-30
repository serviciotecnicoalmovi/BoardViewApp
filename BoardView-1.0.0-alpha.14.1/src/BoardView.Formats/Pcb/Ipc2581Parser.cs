using System.Xml.Linq;
using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;
using BoardView.Core.Geometry;

namespace BoardView.Formats.Pcb;

public sealed class Ipc2581Parser : IBoardFileParser
{
    public string FormatId=>"ipc-2581"; public IReadOnlyCollection<string> Extensions=>[".xml",".cvg"];
    public bool CanParse(string p)=>Extensions.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase);
    public async Task<BoardDocument> ParseAsync(string path,CancellationToken ct=default)
    {
        await using FileStream fs=File.OpenRead(path);XDocument x=await XDocument.LoadAsync(fs,LoadOptions.None,ct);if(x.Root?.Name.LocalName.Contains("IPC",StringComparison.OrdinalIgnoreCase)!=true)throw new InvalidDataException("El archivo no contiene una raíz IPC-2581.");BoardDocument d=BoardParserHelpers.Create(path);int i=0;
        foreach(XElement c in x.Descendants().Where(e=>e.Name.LocalName is "Component" or "ComponentInstance")){string reference=(string?)c.Attribute("refDes")??(string?)c.Attribute("name")??$"U{++i}";double px=Read(c,"x"),py=Read(c,"y");d.AddComponent(new BoardComponent($"ipc-component-{++i}",reference,(string?)c.Attribute("part")??string.Empty,new Point2D(px,py),Read(c,"rotation"),BoardSide.Top));}
        foreach(XElement n in x.Descendants().Where(e=>e.Name.LocalName=="LogicalNet")){string name=(string?)n.Attribute("name")??$"Net-{++i}";BoardParserHelpers.EnsureNet(d,name);}return d;
    }
    private static double Read(XElement e,string name)=>double.TryParse((string?)e.Attribute(name),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double v)?v:0;
}
