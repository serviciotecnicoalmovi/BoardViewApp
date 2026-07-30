namespace BoardView.Core.Contracts.Documents;

/// <summary>Solicitud inmutable enviada a un parser documental.</summary>
public sealed record DocumentParseRequest(string FilePath, CancellationToken CancellationToken = default)
{
    public string FilePath { get; } =
        string.IsNullOrWhiteSpace(FilePath)
            ? throw new ArgumentException("La ruta del archivo es obligatoria.", nameof(FilePath))
            : FilePath;
}
