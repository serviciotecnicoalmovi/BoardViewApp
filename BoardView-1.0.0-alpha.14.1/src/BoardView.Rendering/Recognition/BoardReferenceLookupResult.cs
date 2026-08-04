namespace BoardView.Rendering.Recognition;

/// <summary>
/// Resultado de una consulta dentro del índice de referencias.
/// </summary>
public sealed record BoardReferenceLookupResult
{
    /// <summary>
    /// Inicializa un resultado de búsqueda.
    /// </summary>
    public BoardReferenceLookupResult(
        string query,
        IEnumerable<BoardReferenceEntry> matches,
        bool isExactMatch)
    {
        Query =
            query?.Trim() ??
            string.Empty;

        ArgumentNullException.ThrowIfNull(
            matches);

        Matches =
            Array.AsReadOnly(
                matches.ToArray());

        IsExactMatch =
            isExactMatch;
    }

    /// <summary>
    /// Texto original utilizado para buscar.
    /// </summary>
    public string Query { get; }

    /// <summary>
    /// Coincidencias ordenadas.
    /// </summary>
    public IReadOnlyList<BoardReferenceEntry> Matches { get; }

    /// <summary>
    /// Indica si la primera coincidencia corresponde exactamente a la consulta.
    /// </summary>
    public bool IsExactMatch { get; }

    /// <summary>
    /// Indica si se encontró al menos una coincidencia.
    /// </summary>
    public bool HasMatches =>
        Matches.Count > 0;

    /// <summary>
    /// Primera coincidencia disponible.
    /// </summary>
    public BoardReferenceEntry? BestMatch =>
        Matches.Count > 0
            ? Matches[0]
            : null;

    /// <summary>
    /// Resultado vacío reutilizable.
    /// </summary>
    public static BoardReferenceLookupResult Empty(
        string query)
    {
        return new BoardReferenceLookupResult(
            query,
            Array.Empty<BoardReferenceEntry>(),
            isExactMatch: false);
    }
}
