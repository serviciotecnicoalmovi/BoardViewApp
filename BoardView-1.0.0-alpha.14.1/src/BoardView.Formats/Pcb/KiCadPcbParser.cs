using System.Globalization;
using System.Text.RegularExpressions;
using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Formats.Pcb;

public sealed partial class KiCadPcbParser : IBoardFileParser
{
    public string FormatId=>"kicad-pcb"; public IReadOnlyCollection<string> Extensions=>[".kicad_pcb"];
    public bool CanParse(string p)=>Path.GetExtension(p).Equals(".kicad_pcb",StringComparison.OrdinalIgnoreCase);
    public async Task<BoardDocument> ParseAsync(string path,CancellationToken ct=default)
    {
        string s=await File.ReadAllTextAsync(path,ct);BoardDocument d=BoardParserHelpers.Create(path);int i=0;
        foreach(Match m in Segment().Matches(s)){ct.ThrowIfCancellationRequested();string? net=m.Groups[6].Success?BoardParserHelpers.EnsureNet(d,m.Groups[6].Value):null;d.AddElement(new TrackElement($"kicad-track-{++i}",m.Groups[5].Value=="B.Cu"?"bottom-copper":"top-copper",new(BoardParserHelpers.Number(m.Groups[1].Value),BoardParserHelpers.Number(m.Groups[2].Value)),new(BoardParserHelpers.Number(m.Groups[3].Value),BoardParserHelpers.Number(m.Groups[4].Value)),BoardParserHelpers.Number(m.Groups[7].Value),net));}
        foreach(Match m in Footprint().Matches(s)){d.AddComponent(new BoardComponent($"kicad-component-{++i}",m.Groups[1].Value,m.Groups[2].Value,new(BoardParserHelpers.Number(m.Groups[3].Value),BoardParserHelpers.Number(m.Groups[4].Value)),m.Groups[5].Success?BoardParserHelpers.Number(m.Groups[5].Value):0,BoardSide.Top));}
        List<Point2D> outline=[];foreach(Match m in Edge().Matches(s))outline.Add(new(BoardParserHelpers.Number(m.Groups[1].Value),BoardParserHelpers.Number(m.Groups[2].Value)));BoardParserHelpers.AddOutline(d,outline);
        return d;
    }
    [GeneratedRegex("""\(segment\s+\(start\s+([-\d.]+)\s+([-\d.]+)\)\s+\(end\s+([-\d.]+)\s+([-\d.]+)\).*?\(layer\s+"([^"]+)"\).*?(?:\(net\s+\d+\s+"([^"]+)"\))?.*?\(width\s+([-\d.]+)\)""", RegexOptions.Singleline)] private static partial Regex Segment();
    [GeneratedRegex("""\(footprint.*?\(property\s+"Reference"\s+"([^"]+)".*?\(property\s+"Value"\s+"([^"]*)".*?\(at\s+([-\d.]+)\s+([-\d.]+)(?:\s+([-\d.]+))?\)""", RegexOptions.Singleline)] private static partial Regex Footprint();
    [GeneratedRegex("""\(gr_line\s+\(start\s+([-\d.]+)\s+([-\d.]+)\).*?\(layer\s+"Edge.Cuts"\)""", RegexOptions.Singleline)] private static partial Regex Edge();
}
