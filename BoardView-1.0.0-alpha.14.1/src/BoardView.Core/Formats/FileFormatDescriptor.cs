namespace BoardView.Core.Formats;

/// <summary>Describe un formato que BoardView puede reconocer.</summary>
public sealed record FileFormatDescriptor(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    string Category,
    bool ParserAvailable);
