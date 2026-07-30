namespace BoardView.Core.Documents.Common;

/// <summary>Información estable que identifica el documento y su origen.</summary>
public sealed record DocumentInfo
{
    public DocumentInfo(string name, string sourcePath, TechnicalDocumentKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        Name = name.Trim();
        SourcePath = sourcePath;
        Kind = kind;
    }

    public string Name { get; }
    public string SourcePath { get; }
    public TechnicalDocumentKind Kind { get; }
    public DateTimeOffset ImportedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
