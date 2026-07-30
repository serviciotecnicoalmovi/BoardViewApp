using BoardView.Core.Contracts.Formats;
using BoardView.Core.Documents;

namespace BoardView.Formats.Pcb;

public sealed class BoardParserRegistry
{
    private readonly IReadOnlyList<IBoardFileParser> parsers=[new GerberParser(),new ExcellonParser(),new KiCadPcbParser(),new EagleBoardParser(),new Ipc2581Parser(),new OdbPlusPlusParser(),new LegacyPcbParser()];
    public IReadOnlyList<IBoardFileParser> Parsers=>parsers;
    public IBoardFileParser Resolve(string path)=>parsers.FirstOrDefault(p=>p.CanParse(path))??throw new NotSupportedException($"No existe parser para '{Path.GetExtension(path)}'.");
    public Task<BoardDocument> ParseAsync(string path,CancellationToken ct=default)=>Resolve(path).ParseAsync(path,ct);
}
