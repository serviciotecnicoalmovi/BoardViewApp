using System.Collections.ObjectModel;
using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Resultado inmutable de la asociación entre referencias y geometría.
/// </summary>
public sealed class BoardReferenceAssociationResult
{
    private readonly IReadOnlyDictionary<string, BoardReferenceAssociation> byReference;
    private readonly IReadOnlyDictionary<int, BoardReferenceAssociation> byComponentId;

    /// <summary>
    /// Inicializa el resultado.
    /// </summary>
    public BoardReferenceAssociationResult(
        IEnumerable<BoardReferenceCandidate> candidates,
        IEnumerable<BoardReferenceAssociation> associations)
    {
        ArgumentNullException.ThrowIfNull(
            candidates);

        ArgumentNullException.ThrowIfNull(
            associations);

        BoardReferenceCandidate[] candidateArray =
            candidates
                .OrderBy(candidate => candidate.Id)
                .ToArray();

        BoardReferenceAssociation[] associationArray =
            associations
                .OrderByDescending(association => association.Score)
                .ThenBy(association => association.Candidate.Id)
                .ToArray();

        Candidates =
            Array.AsReadOnly(
                candidateArray);

        Associations =
            Array.AsReadOnly(
                associationArray);

        byReference =
            new ReadOnlyDictionary<string, BoardReferenceAssociation>(
                associationArray
                    .GroupBy(
                        association => association.Reference,
                        StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderByDescending(item => item.Score)
                            .First(),
                        StringComparer.OrdinalIgnoreCase));

        byComponentId =
            new ReadOnlyDictionary<int, BoardReferenceAssociation>(
                associationArray
                    .GroupBy(
                        association => association.ComponentId)
                    .ToDictionary(
                        group => group.Key,
                        group => group
                            .OrderByDescending(item => item.Score)
                            .First()));

        AssociatedCandidateIds =
            new ReadOnlySet<int>(
                associationArray
                    .Select(association => association.Candidate.Id));

        UnassociatedCandidates =
            Array.AsReadOnly(
                candidateArray
                    .Where(candidate =>
                        !AssociatedCandidateIds.Contains(candidate.Id))
                    .ToArray());

        Statistics =
            CreateStatistics(
                candidateArray,
                associationArray);
    }

    /// <summary>
    /// Todos los candidatos evaluados.
    /// </summary>
    public IReadOnlyList<BoardReferenceCandidate> Candidates { get; }

    /// <summary>
    /// Asociaciones aceptadas.
    /// </summary>
    public IReadOnlyList<BoardReferenceAssociation> Associations { get; }

    /// <summary>
    /// Candidatos que no pudieron asociarse.
    /// </summary>
    public IReadOnlyList<BoardReferenceCandidate> UnassociatedCandidates { get; }

    /// <summary>
    /// Identificadores de candidatos asociados.
    /// </summary>
    public IReadOnlySet<int> AssociatedCandidateIds { get; }

    /// <summary>
    /// Estadísticas agregadas.
    /// </summary>
    public BoardReferenceAssociationStatistics Statistics { get; }

    /// <summary>
    /// Indica si existe al menos una asociación.
    /// </summary>
    public bool HasAssociations =>
        Associations.Count > 0;

    /// <summary>
    /// Busca una asociación por referencia normalizada.
    /// </summary>
    public bool TryGetByReference(
        string reference,
        out BoardReferenceAssociation? association)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            association = null;
            return false;
        }

        string normalized =
            BoardReferenceCandidate.NormalizeReference(
                reference);

        return byReference.TryGetValue(
            normalized,
            out association);
    }

    /// <summary>
    /// Busca una asociación por identificador de componente.
    /// </summary>
    public bool TryGetByComponentId(
        int componentId,
        out BoardReferenceAssociation? association)
    {
        return byComponentId.TryGetValue(
            componentId,
            out association);
    }

    /// <summary>
    /// Obtiene asociaciones cuyo texto comienza con el prefijo indicado.
    /// </summary>
    public IReadOnlyList<BoardReferenceAssociation> FindByPrefix(
        string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Array.Empty<BoardReferenceAssociation>();
        }

        string normalizedPrefix =
            BoardReferenceCandidate.NormalizeReference(
                prefix);

        return Associations
            .Where(association =>
                association.Reference.StartsWith(
                    normalizedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>
    /// Crea un resultado vacío.
    /// </summary>
    public static BoardReferenceAssociationResult Empty { get; } =
        new(
            Array.Empty<BoardReferenceCandidate>(),
            Array.Empty<BoardReferenceAssociation>());

    private static BoardReferenceAssociationStatistics CreateStatistics(
        IReadOnlyCollection<BoardReferenceCandidate> candidates,
        IReadOnlyCollection<BoardReferenceAssociation> associations)
    {
        double averageScore =
            associations.Count == 0
                ? 0D
                : associations.Average(
                    association => association.Score);

        double coverage =
            candidates.Count == 0
                ? 0D
                : (double)associations
                    .Select(association => association.Candidate.Id)
                    .Distinct()
                    .Count() /
                  candidates.Count;

        return new BoardReferenceAssociationStatistics(
            candidates.Count,
            associations.Count,
            candidates.Count -
            associations
                .Select(association => association.Candidate.Id)
                .Distinct()
                .Count(),
            averageScore,
            coverage);
    }
}

/// <summary>
/// Estadísticas del resultado de asociación.
/// </summary>
public sealed record BoardReferenceAssociationStatistics(
    int CandidateCount,
    int AssociationCount,
    int UnassociatedCandidateCount,
    double AverageScore,
    double CandidateCoverage);

/// <summary>
/// Implementación inmutable mínima de <see cref="IReadOnlySet{T}"/>.
/// </summary>
internal sealed class ReadOnlySet<T> : IReadOnlySet<T>
{
    private readonly HashSet<T> values;

    public ReadOnlySet(
        IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(
            values);

        this.values =
            new HashSet<T>(
                values);
    }

    public int Count =>
        values.Count;

    public bool Contains(
        T item)
    {
        return values.Contains(
            item);
    }

    public bool IsProperSubsetOf(
        IEnumerable<T> other)
    {
        return values.IsProperSubsetOf(
            other);
    }

    public bool IsProperSupersetOf(
        IEnumerable<T> other)
    {
        return values.IsProperSupersetOf(
            other);
    }

    public bool IsSubsetOf(
        IEnumerable<T> other)
    {
        return values.IsSubsetOf(
            other);
    }

    public bool IsSupersetOf(
        IEnumerable<T> other)
    {
        return values.IsSupersetOf(
            other);
    }

    public bool Overlaps(
        IEnumerable<T> other)
    {
        return values.Overlaps(
            other);
    }

    public bool SetEquals(
        IEnumerable<T> other)
    {
        return values.SetEquals(
            other);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return values.GetEnumerator();
    }

    System.Collections.IEnumerator
        System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
