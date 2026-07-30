using System.Text.RegularExpressions;
using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Formats.Pcb;

/// <summary>Parser del formato textual PCB/PCBNEW heredado para tracks y módulos.</summary>
public sealed partial class LegacyPcbParser : IBoardFileParser
{
    public string FormatId=>"legacy-pcb";public IReadOnlyCollection<string> Extensions=>[".pcb"];
    public bool CanParse(string p)=>Path.GetExtension(p).Equals(".pcb",StringComparison.OrdinalIgnoreCase);
    public async Task<BoardDocument> ParseAsync(string path,CancellationToken ct=default)
    {
        string s=await File.ReadAllTextAsync(path,ct);BoardDocument d=BoardParserHelpers.Create(path);int i=0;
        foreach(Match m in Track().Matches(s)){d.AddElement(new TrackElement($"pcb-track-{++i}","top-copper",new(BoardParserHelpers.Number(m.Groups[1].Value),BoardParserHelpers.Number(m.Groups[2].Value)),new(BoardParserHelpers.Number(m.Groups[3].Value),BoardParserHelpers.Number(m.Groups[4].Value)),BoardParserHelpers.Number(m.Groups[5].Value)));}
        foreach(Match m in Module().Matches(s)){d.AddComponent(new BoardComponent($"pcb-component-{++i}",m.Groups[1].Value,string.Empty,new(BoardParserHelpers.Number(m.Groups[2].Value),BoardParserHelpers.Number(m.Groups[3].Value)),0,BoardSide.Top));}return d;
    }
    [GeneratedRegex(@"Po\s+\d+\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)\s+([-\d.]+)")]private static partial Regex Track();
    [GeneratedRegex(@"\$MODULE\s+([^\s]+).*?Po\s+([-\d.]+)\s+([-\d.]+)",RegexOptions.Singleline)]private static partial Regex Module();
}
