using System.Globalization;
using System.Text.RegularExpressions;
using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Formats.Pcb;

public sealed partial class ExcellonParser : IBoardFileParser
{
    public string FormatId=>"excellon"; public IReadOnlyCollection<string> Extensions=>[".drl",".xln",".exc"];
    public bool CanParse(string p)=>Extensions.Contains(Path.GetExtension(p),StringComparer.OrdinalIgnoreCase);
    public async Task<BoardDocument> ParseAsync(string path,CancellationToken ct=default)
    {
        string[] lines=await File.ReadAllLinesAsync(path,ct); BoardDocument d=BoardParserHelpers.Create(path); Dictionary<int,double> tools=[]; int tool=0,i=0; bool inch=false;
        foreach(string raw in lines){ct.ThrowIfCancellationRequested();string line=raw.Trim(); if(line.StartsWith("INCH",StringComparison.OrdinalIgnoreCase))inch=true;
            Match td=ToolDef().Match(line);if(td.Success){tools[int.Parse(td.Groups[1].Value,CultureInfo.InvariantCulture)]=BoardParserHelpers.Number(td.Groups[2].Value)*(inch?25.4D:1D);continue;}
            Match ts=ToolSelect().Match(line);if(ts.Success){tool=int.Parse(ts.Groups[1].Value,CultureInfo.InvariantCulture);continue;}
            Match p=Point().Match(line);if(!p.Success)continue; double scale=inch?0.0001D*25.4D:0.001D; Point2D pos=new(BoardParserHelpers.Number(p.Groups[1].Value)*scale,BoardParserHelpers.Number(p.Groups[2].Value)*scale); double dia=tools.GetValueOrDefault(tool,0.3D); d.AddElement(new ViaElement($"drill-{++i}","drill",pos,dia,dia));}
        return d;
    }
    [GeneratedRegex(@"^T(\d+)C([0-9.]+)",RegexOptions.IgnoreCase)]private static partial Regex ToolDef();
    [GeneratedRegex(@"^T(\d+)$",RegexOptions.IgnoreCase)]private static partial Regex ToolSelect();
    [GeneratedRegex(@"^X(-?\d+)Y(-?\d+)",RegexOptions.IgnoreCase)]private static partial Regex Point();
}
