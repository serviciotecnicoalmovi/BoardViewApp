using BoardView.Core.Documents;

namespace BoardView.Core.Contracts.Formats;

public interface IBoardFileParser
{
    string FormatId { get; }
    IReadOnlyCollection<string> Extensions { get; }
    bool CanParse(string filePath);
    Task<BoardDocument> ParseAsync(string filePath, CancellationToken cancellationToken = default);
}
