using System.Xml.Linq;
using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Formats.Pcb;

public sealed class EagleBoardParser : IBoardFileParser
{
    public string FormatId=>"eagle"; public IReadOnlyCollection<string> Extensions=>[".brd"];
    public bool CanParse(string p)=>Path.GetExtension(p).Equals(".brd",StringComparison.OrdinalIgnoreCase);
    public async Task<BoardDocument> ParseAsync(string path,CancellationToken ct=default)
    {
        await using FileStream stream=File.OpenRead(path);XDocument x=await XDocument.LoadAsync(stream,LoadOptions.None,ct);BoardDocument d=BoardParserHelpers.Create(path);int i=0;
        foreach(XElement e in x.Descendants("element")){string reference=(string?)e.Attribute("name")??$"U{i+1}";string value=(string?)e.Attribute("value")??string.Empty;d.AddComponent(new BoardComponent($"eagle-component-{++i}",reference,value,new((double?)e.Attribute("x")??0,(double?)e.Attribute("y")??0),ParseRotation((string?)e.Attribute("rot")),((string?)e.Attribute("rot"))?.StartsWith("M",StringComparison.OrdinalIgnoreCase)==true?BoardSide.Bottom:BoardSide.Top));}
        foreach(XElement w in x.Descendants("wire")){Point2D a=new((double?)w.Attribute("x1")??0,(double?)w.Attribute("y1")??0),b=new((double?)w.Attribute("x2")??0,(double?)w.Attribute("y2")??0);string layer=(string?)w.Attribute("layer")=="16"?"bottom-copper":"top-copper";d.AddElement(new TrackElement($"eagle-wire-{++i}",layer,a,b,(double?)w.Attribute("width")??0.15));}
        return d;
    }
    private static double ParseRotation(string? s){if(string.IsNullOrEmpty(s))return 0;string digits=new(s.Where(c=>char.IsDigit(c)||c=='.'||c=='-').ToArray());return double.TryParse(digits,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double v)?v:0;}
}
