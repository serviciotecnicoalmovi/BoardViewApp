namespace BoardView.Core.Spatial;

/// <summary>Immutable operational statistics for a spatial index.</summary>
public sealed record SpatialStatistics(
    int ItemCount,
    int CellCount,
    double CellSize,
    long Version,
    long QueryCount,
    long CandidateCount,
    long HitCount,
    TimeSpan TotalQueryTime)
{
    /// <summary>Gets the mean number of candidates inspected per query.</summary>
    public double AverageCandidates => QueryCount == 0 ? 0D : (double)CandidateCount / QueryCount;

    /// <summary>Gets the mean number of hits returned per query.</summary>
    public double AverageHits => QueryCount == 0 ? 0D : (double)HitCount / QueryCount;
}
