using System.Collections.ObjectModel;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Índice inmutable y bidireccional de referencias electrónicas.
/// </summary>
/// <remarks>
/// Permite resolver una referencia hacia su componente y un componente hacia
/// su referencia principal sin recorrer colecciones completas.
/// </remarks>
public sealed class BoardReferenceIndex
{
    private readonly IReadOnlyDictionary<string, BoardReferenceEntry> byReference;
    private readonly IReadOnlyDictionary<int, BoardReferenceEntry> byComponentId;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<BoardReferenceEntry>> byPrefix;

    /// <summary>
    /// Inicializa el índice desde un resultado de asociación.
    /// </summary>
    public BoardReferenceIndex(
        BoardReferenceAssociationResult associationResult)
        : this(
            associationResult?.Associations ??
            throw new ArgumentNullException(
                nameof(associationResult)))
    {
    }

    /// <summary>
    /// Inicializa el índice desde asociaciones validadas.
    /// </summary>
    public BoardReferenceIndex(
        IEnumerable<BoardReferenceAssociation> associations)
    {
        ArgumentNullException.ThrowIfNull(
            associations);

        BoardReferenceEntry[] entries =
            associations
                .Select(
                    BoardReferenceEntry.FromAssociation)
                .OrderBy(entry =>
                    entry.Reference,
                    ReferenceComparer.Instance)
                .ThenByDescending(entry =>
                    entry.Confidence)
                .ThenBy(entry =>
                    entry.ComponentId)
                .ToArray();

        Entries =
            Array.AsReadOnly(
                entries);

        byReference =
            new ReadOnlyDictionary<string, BoardReferenceEntry>(
                entries
                    .GroupBy(
                        entry => entry.Reference,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderByDescending(entry =>
                                entry.Confidence)
                            .ThenBy(entry =>
                                entry.DistancePixels)
                            .ThenBy(entry =>
                                entry.ComponentId)
                            .First(),
                        StringComparer.OrdinalIgnoreCase));

        byComponentId =
            new ReadOnlyDictionary<int, BoardReferenceEntry>(
                entries
                    .GroupBy(
                        entry => entry.ComponentId)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderByDescending(entry =>
                                entry.Confidence)
                            .ThenBy(entry =>
                                entry.DistancePixels)
                            .ThenBy(entry =>
                                entry.Reference,
                                ReferenceComparer.Instance)
                            .First()));

        byPrefix =
            new ReadOnlyDictionary<string, IReadOnlyList<BoardReferenceEntry>>(
                entries
                    .Where(entry =>
                        !string.IsNullOrWhiteSpace(
                            entry.Prefix))
                    .GroupBy(
                        entry => entry.Prefix,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group =>
                            (IReadOnlyList<BoardReferenceEntry>)
                            Array.AsReadOnly(
                                group
                                    .OrderBy(entry =>
                                        entry.Reference,
                                        ReferenceComparer.Instance)
                                    .ToArray()),
                        StringComparer.OrdinalIgnoreCase));

        Statistics =
            new BoardReferenceIndexStatistics(
                entries.Length,
                byReference.Count,
                byComponentId.Count,
                byPrefix.Count);
    }

    /// <summary>
    /// Todas las entradas del índice.
    /// </summary>
    public IReadOnlyList<BoardReferenceEntry> Entries { get; }

    /// <summary>
    /// Estadísticas agregadas.
    /// </summary>
    public BoardReferenceIndexStatistics Statistics { get; }

    /// <summary>
    /// Cantidad de referencias únicas.
    /// </summary>
    public int Count =>
        byReference.Count;

    /// <summary>
    /// Indica si el índice contiene entradas.
    /// </summary>
    public bool IsEmpty =>
        Count == 0;

    /// <summary>
    /// Obtiene una entrada mediante una referencia exacta.
    /// </summary>
    public bool TryGetByReference(
        string reference,
        out BoardReferenceEntry? entry)
    {
        if (string.IsNullOrWhiteSpace(
                reference))
        {
            entry = null;
            return false;
        }

        string normalized =
            BoardReferenceCandidate.NormalizeReference(
                reference);

        return byReference.TryGetValue(
            normalized,
            out entry);
    }

    /// <summary>
    /// Obtiene la referencia principal asociada a un componente.
    /// </summary>
    public bool TryGetByComponentId(
        int componentId,
        out BoardReferenceEntry? entry)
    {
        return byComponentId.TryGetValue(
            componentId,
            out entry);
    }

    /// <summary>
    /// Busca referencias exactas, por prefijo textual o por contenido.
    /// </summary>
    public BoardReferenceLookupResult Search(
        string query,
        int maximumResults = 50)
    {
        if (maximumResults <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumResults),
                maximumResults,
                "La cantidad máxima debe ser mayor que cero.");
        }

        if (string.IsNullOrWhiteSpace(
                query))
        {
            return BoardReferenceLookupResult.Empty(
                query);
        }

        string normalized =
            BoardReferenceCandidate.NormalizeReference(
                query);

        if (byReference.TryGetValue(
                normalized,
                out BoardReferenceEntry? exact))
        {
            return new BoardReferenceLookupResult(
                query,
                new[]
                {
                    exact
                },
                isExactMatch: true);
        }

        BoardReferenceEntry[] matches =
            Entries
                .Where(entry =>
                    entry.Reference.StartsWith(
                        normalized,
                        StringComparison.OrdinalIgnoreCase))
                .Concat(
                    Entries.Where(entry =>
                        !entry.Reference.StartsWith(
                            normalized,
                            StringComparison.OrdinalIgnoreCase) &&
                        entry.Reference.Contains(
                            normalized,
                            StringComparison.OrdinalIgnoreCase)))
                .DistinctBy(entry =>
                    entry.Reference,
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(entry =>
                    GetSearchPriority(
                        entry.Reference,
                        normalized))
                .ThenBy(entry =>
                    entry.Reference.Length)
                .ThenBy(entry =>
                    entry.Reference,
                    ReferenceComparer.Instance)
                .ThenByDescending(entry =>
                    entry.Confidence)
                .Take(
                    maximumResults)
                .ToArray();

        return new BoardReferenceLookupResult(
            query,
            matches,
            isExactMatch: false);
    }

    /// <summary>
    /// Obtiene todas las referencias correspondientes a un prefijo.
    /// </summary>
    public IReadOnlyList<BoardReferenceEntry> FindByPrefix(
        string prefix)
    {
        if (string.IsNullOrWhiteSpace(
                prefix))
        {
            return Array.Empty<BoardReferenceEntry>();
        }

        string normalized =
            BoardReferenceCandidate.NormalizeReference(
                prefix);

        return byPrefix.TryGetValue(
            normalized,
            out IReadOnlyList<BoardReferenceEntry>? entries)
                ? entries
                : Array.Empty<BoardReferenceEntry>();
    }

    /// <summary>
    /// Crea un índice vacío reutilizable.
    /// </summary>
    public static BoardReferenceIndex Empty { get; } =
        new(
            Array.Empty<BoardReferenceAssociation>());

    private static int GetSearchPriority(
        string reference,
        string query)
    {
        if (string.Equals(
                reference,
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (reference.StartsWith(
                query,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    /// <summary>
    /// Comparador natural para referencias alfanuméricas.
    /// </summary>
    private sealed class ReferenceComparer : IComparer<string>
    {
        public static ReferenceComparer Instance { get; } =
            new();

        public int Compare(
            string? left,
            string? right)
        {
            if (ReferenceEquals(
                    left,
                    right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            ParseReference(
                left,
                out string leftPrefix,
                out long leftNumber,
                out string leftSuffix);

            ParseReference(
                right,
                out string rightPrefix,
                out long rightNumber,
                out string rightSuffix);

            int prefixComparison =
                StringComparer.OrdinalIgnoreCase.Compare(
                    leftPrefix,
                    rightPrefix);

            if (prefixComparison != 0)
            {
                return prefixComparison;
            }

            int numberComparison =
                leftNumber.CompareTo(
                    rightNumber);

            if (numberComparison != 0)
            {
                return numberComparison;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(
                leftSuffix,
                rightSuffix);
        }

        private static void ParseReference(
            string value,
            out string prefix,
            out long number,
            out string suffix)
        {
            int digitStart = 0;

            while (digitStart < value.Length &&
                   !char.IsDigit(value[digitStart]))
            {
                digitStart++;
            }

            int digitEnd =
                digitStart;

            while (digitEnd < value.Length &&
                   char.IsDigit(value[digitEnd]))
            {
                digitEnd++;
            }

            prefix =
                value[..digitStart];

            string numericPart =
                value[digitStart..digitEnd];

            number =
                long.TryParse(
                    numericPart,
                    out long parsed)
                    ? parsed
                    : long.MaxValue;

            suffix =
                value[digitEnd..];
        }
    }
}

/// <summary>
/// Estadísticas del índice de referencias.
/// </summary>
public sealed record BoardReferenceIndexStatistics(
    int EntryCount,
    int UniqueReferenceCount,
    int IndexedComponentCount,
    int PrefixCount);
