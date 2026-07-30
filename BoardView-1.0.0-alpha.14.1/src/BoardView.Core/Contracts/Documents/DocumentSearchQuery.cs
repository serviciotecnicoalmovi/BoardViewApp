namespace BoardView.Core.Contracts.Documents;

/// <summary>Consulta textual para proveedores de búsqueda.</summary>
public sealed record DocumentSearchQuery
{
    public DocumentSearchQuery(string text, bool matchCase = false, bool wholeWord = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text.Trim();
        MatchCase = matchCase;
        WholeWord = wholeWord;
    }

    public string Text { get; }
    public bool MatchCase { get; }
    public bool WholeWord { get; }
}
