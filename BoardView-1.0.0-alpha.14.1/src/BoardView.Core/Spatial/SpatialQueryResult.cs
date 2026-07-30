namespace BoardView.Core.Spatial;

/// <summary>Contains spatial hits together with query diagnostics.</summary>
/// <typeparam name="T">Type of indexed object.</typeparam>
public sealed class SpatialQueryResult<T> where T : notnull
{
    internal SpatialQueryResult(
        IReadOnlyList<SpatialHit<T>> hits,
        int candidateCount,
        TimeSpan elapsed,
        long indexVersion)
    {
        Hits = hits;
        CandidateCount = candidateCount;
        Elapsed = elapsed;
        IndexVersion = indexVersion;
    }

    /// <summary>Gets ordered query hits.</summary>
    public IReadOnlyList<SpatialHit<T>> Hits { get; }

    /// <summary>Gets the number of objects examined after cell reduction.</summary>
    public int CandidateCount { get; }

    /// <summary>Gets the measured query duration.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Gets the index version used by the query.</summary>
    public long IndexVersion { get; }
}
