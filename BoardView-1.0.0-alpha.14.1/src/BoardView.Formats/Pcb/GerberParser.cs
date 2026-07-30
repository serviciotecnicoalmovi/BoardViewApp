using System.Globalization;
using System.Text.RegularExpressions;
using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;

namespace BoardView.Formats.Pcb;

/// <summary>Parser RS-274X para coordenadas lineales, flashes circulares y selección de aperturas.</summary>
public sealed partial class GerberParser : IBoardFileParser
{
    public string FormatId=>"gerber";
    public IReadOnlyCollection<string> Extensions=>[".gbr",".ger",".gtl",".gbl",".gko",".gm1"];
    public bool CanParse(string filePath)=>Extensions.Contains(Path.GetExtension(filePath),StringComparer.OrdinalIgnoreCase);
    public async Task<BoardDocument> ParseAsync(string filePath,CancellationToken cancellationToken=default)
    {
        string text=await File.ReadAllTextAsync(filePath,cancellationToken); BoardDocument d=BoardParserHelpers.Create(filePath);
        bool metric=text.Contains("%MOMM",StringComparison.OrdinalIgnoreCase); double scale=metric?0.001D:0.0001D*25.4D;
        Dictionary<int,double> apertures=[];
        foreach(Match m in ApertureRegex().Matches(text)) apertures[int.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture)]=BoardParserHelpers.Number(m.Groups[2].Value)*(metric?1D:25.4D);
        Point2D current=Point2D.Zero; int ap=0,index=0;
        foreach(string token in text.Split('*',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Match select=SelectRegex().Match(token); if(select.Success){ap=int.Parse(select.Groups[1].Value,CultureInfo.InvariantCulture);continue;}
            Match c=CoordinateRegex().Match(token); if(!c.Success)continue;
            double x=c.Groups[1].Success?BoardParserHelpers.Number(c.Groups[1].Value)*scale:current.X;
            double y=c.Groups[2].Success?BoardParserHelpers.Number(c.Groups[2].Value)*scale:current.Y;
            int op=c.Groups[3].Success?int.Parse(c.Groups[3].Value,CultureInfo.InvariantCulture):1; Point2D next=new(x,y);
            if(op==1 && next!=current)d.AddElement(new TrackElement($"gerber-track-{++index}","top-copper",current,next,apertures.GetValueOrDefault(ap,0.15D)));
            else if(op==3)d.AddElement(new PadElement($"gerber-flash-{++index}","top-copper",next,apertures.GetValueOrDefault(ap,0.5D),apertures.GetValueOrDefault(ap,0.5D),PadShape.Circle));
            current=next;
        }
        return d;
    }
    [GeneratedRegex(@"%ADD(\d+)C,([0-9.]+)",RegexOptions.IgnoreCase)] private static partial Regex ApertureRegex();
    [GeneratedRegex(@"(?:^|G54)D(\d+)$",RegexOptions.IgnoreCase)] private static partial Regex SelectRegex();
    [GeneratedRegex(@"(?:X(-?\d+))?(?:Y(-?\d+))?(?:D0?([123]))",RegexOptions.IgnoreCase)] private static partial Regex CoordinateRegex();
}
