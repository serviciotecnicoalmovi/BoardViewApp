using System.IO.Compression;
using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;

namespace BoardView.Formats.Pcb;

/// <summary>Lee paquetes ODB++ comprimidos, identifica capas y crea el modelo de capas normalizado.</summary>
public sealed class OdbPlusPlusParser : IBoardFileParser
{
    public string FormatId=>"odb++";public IReadOnlyCollection<string> Extensions=>[".zip",".tgz",".tar.gz"];
    public bool CanParse(string p)=>Path.GetExtension(p).Equals(".zip",StringComparison.OrdinalIgnoreCase);
    public Task<BoardDocument> ParseAsync(string path,CancellationToken ct=default)
    {
        BoardDocument d=BoardParserHelpers.Create(path);using ZipArchive z=ZipFile.OpenRead(path);int order=100;
        foreach(string layer in z.Entries.Select(e=>e.FullName).Where(n=>n.Contains("/layers/",StringComparison.OrdinalIgnoreCase)).Select(n=>n.Split('/').SkipWhile(p=>!p.Equals("layers",StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()).OfType<string>().Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {ct.ThrowIfCancellationRequested();string id="odb-"+layer.ToLowerInvariant();if(!d.Layers.Any(l=>l.Id==id))d.AddLayer(new BoardLayer(id,layer,LayerType.Unknown,BoardSide.None,order++));}
        if(d.Layers.Count==4)throw new InvalidDataException("El ZIP no contiene una estructura ODB++ reconocible.");return Task.FromResult(d);
    }
}
