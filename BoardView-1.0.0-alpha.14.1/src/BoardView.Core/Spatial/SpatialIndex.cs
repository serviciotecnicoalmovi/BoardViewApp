using System.Diagnostics;
using System.Threading;
using BoardView.Core.Geometry;

namespace BoardView.Core.Spatial;

/// <summary>
/// Thread-safe uniform-grid spatial index designed for PCB and technical-document geometry.
/// Objects spanning multiple cells are stored once per covered cell and deduplicated during
/// queries. All mutations invalidate previous query versions deterministically.
/// </summary>
/// <typeparam name="T">Type of indexed object.</typeparam>
public sealed class SpatialIndex<T> : ISpatialIndex<T>, IDisposable where T : notnull
{
    private readonly double cellSize;
    private readonly Dictionary<(int X, int Y), HashSet<T>> cells = [];
    private readonly Dictionary<T, Bounds2D> boundsByItem = [];
    private readonly ReaderWriterLockSlim synchronization = new(LockRecursionPolicy.NoRecursion);
    private long version;
    private long queryCount;
    private long candidateCount;
    private long hitCount;
    private long totalQueryTicks;
    private bool disposed;

    /// <summary>Initializes an empty index using square cells of the specified size.</summary>
    public SpatialIndex(double cellSize = 10D)
    {
        if (!double.IsFinite(cellSize) || cellSize <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(cellSize));
        }

        this.cellSize = cellSize;
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            synchronization.EnterReadLock();
            try
            {
                ThrowIfDisposed();
                return boundsByItem.Count;
            }
            finally
            {
                synchronization.ExitReadLock();
            }
        }
    }

    /// <inheritdoc />
    public long Version => Interlocked.Read(ref version);

    /// <inheritdoc />
    public void Add(T item, Bounds2D bounds)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateBounds(bounds);
        synchronization.EnterWriteLock();
        try
        {
            ThrowIfDisposed();
            if (!boundsByItem.TryAdd(item, bounds))
            {
                throw new InvalidOperationException("El elemento ya está indexado.");
            }

            AddToCells(item, bounds);
            IncrementVersion();
        }
        finally
        {
            synchronization.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void AddRange(IEnumerable<(T Item, Bounds2D Bounds)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        (T Item, Bounds2D Bounds)[] materialized = entries.ToArray();
        foreach ((T item, Bounds2D bounds) in materialized)
        {
            ArgumentNullException.ThrowIfNull(item);
            ValidateBounds(bounds);
        }

        if (materialized.Select(static entry => entry.Item).Distinct().Count() != materialized.Length)
        {
            throw new InvalidOperationException("El lote contiene elementos duplicados.");
        }

        synchronization.EnterWriteLock();
        try
        {
            ThrowIfDisposed();
            if (materialized.Any(entry => boundsByItem.ContainsKey(entry.Item)))
            {
                throw new InvalidOperationException("Uno o más elementos del lote ya están indexados.");
            }

            foreach ((T item, Bounds2D bounds) in materialized)
            {
                boundsByItem.Add(item, bounds);
                AddToCells(item, bounds);
            }

            if (materialized.Length > 0)
            {
                IncrementVersion();
            }
        }
        finally
        {
            synchronization.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public bool Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        synchronization.EnterWriteLock();
        try
        {
            ThrowIfDisposed();
            if (!boundsByItem.Remove(item, out Bounds2D bounds))
            {
                return false;
            }

            RemoveFromCells(item, bounds);
            IncrementVersion();
            return true;
        }
        finally
        {
            synchronization.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void Update(T item, Bounds2D bounds)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateBounds(bounds);
        synchronization.EnterWriteLock();
        try
        {
            ThrowIfDisposed();
            if (!boundsByItem.TryGetValue(item, out Bounds2D previousBounds))
            {
                throw new KeyNotFoundException("El elemento no está indexado.");
            }

            if (previousBounds == bounds)
            {
                return;
            }

            RemoveFromCells(item, previousBounds);
            boundsByItem[item] = bounds;
            AddToCells(item, bounds);
            IncrementVersion();
        }
        finally
        {
            synchronization.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        synchronization.EnterWriteLock();
        try
        {
            ThrowIfDisposed();
            if (boundsByItem.Count == 0)
            {
                return;
            }

            cells.Clear();
            boundsByItem.Clear();
            IncrementVersion();
        }
        finally
        {
            synchronization.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<T> Query(Bounds2D area) =>
        Query(SpatialQuery<T>.InArea(area)).Hits.Select(static hit => hit.Item).ToArray();

    /// <inheritdoc />
    public IReadOnlyList<T> Query(Point2D point, double tolerance = 0D) =>
        Query(SpatialQuery<T>.Near(point, tolerance)).Hits.Select(static hit => hit.Item).ToArray();

    /// <inheritdoc />
    public SpatialQueryResult<T> Query(SpatialQuery<T> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<SpatialHit<T>> hits = [];
        int candidates;
        long queryVersion;

        synchronization.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            queryVersion = version;
            HashSet<T> candidateItems = CollectCandidates(query.Area);
            candidates = candidateItems.Count;

            foreach (T item in candidateItems)
            {
                Bounds2D bounds = boundsByItem[item];
                if (!bounds.Intersects(query.Area))
                {
                    continue;
                }

                if (query.Radius is double radius && query.Origin is Point2D origin &&
                    DistanceToBounds(origin, bounds) > radius)
                {
                    continue;
                }

                if (query.Predicate is not null && !query.Predicate(item))
                {
                    continue;
                }

                double distance = query.Origin is Point2D queryOrigin
                    ? DistanceToBounds(queryOrigin, bounds)
                    : 0D;
                hits.Add(new SpatialHit<T>(item, bounds, distance));
            }
        }
        finally
        {
            synchronization.ExitReadLock();
        }

        if (query.Origin is not null)
        {
            hits.Sort(static (left, right) => left.Distance.CompareTo(right.Distance));
        }

        if (query.MaximumResults is int maximum && hits.Count > maximum)
        {
            hits.RemoveRange(maximum, hits.Count - maximum);
        }

        stopwatch.Stop();
        Interlocked.Increment(ref queryCount);
        Interlocked.Add(ref candidateCount, candidates);
        Interlocked.Add(ref hitCount, hits.Count);
        Interlocked.Add(ref totalQueryTicks, stopwatch.Elapsed.Ticks);
        return new SpatialQueryResult<T>(hits.ToArray(), candidates, stopwatch.Elapsed, queryVersion);
    }

    /// <inheritdoc />
    public SpatialStatistics GetStatistics()
    {
        synchronization.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            return new SpatialStatistics(
                boundsByItem.Count,
                cells.Count,
                cellSize,
                version,
                Interlocked.Read(ref queryCount),
                Interlocked.Read(ref candidateCount),
                Interlocked.Read(ref hitCount),
                TimeSpan.FromTicks(Interlocked.Read(ref totalQueryTicks)));
        }
        finally
        {
            synchronization.ExitReadLock();
        }
    }

    /// <summary>Releases synchronization resources owned by the index.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        synchronization.Dispose();
        disposed = true;
    }

    private HashSet<T> CollectCandidates(Bounds2D area)
    {
        HashSet<T> found = [];
        foreach ((int X, int Y) key in EnumerateCells(area))
        {
            if (cells.TryGetValue(key, out HashSet<T>? bucket))
            {
                found.UnionWith(bucket);
            }
        }

        return found;
    }

    private void AddToCells(T item, Bounds2D bounds)
    {
        foreach ((int X, int Y) key in EnumerateCells(bounds))
        {
            if (!cells.TryGetValue(key, out HashSet<T>? bucket))
            {
                bucket = [];
                cells.Add(key, bucket);
            }

            bucket.Add(item);
        }
    }

    private void RemoveFromCells(T item, Bounds2D bounds)
    {
        foreach ((int X, int Y) key in EnumerateCells(bounds))
        {
            if (!cells.TryGetValue(key, out HashSet<T>? bucket))
            {
                continue;
            }

            bucket.Remove(item);
            if (bucket.Count == 0)
            {
                cells.Remove(key);
            }
        }
    }

    private IEnumerable<(int X, int Y)> EnumerateCells(Bounds2D bounds)
    {
        int left = ToCell(bounds.Left);
        int right = ToCell(bounds.Right);
        int top = ToCell(bounds.Top);
        int bottom = ToCell(bounds.Bottom);

        for (int x = left; x <= right; x++)
        {
            for (int y = top; y <= bottom; y++)
            {
                yield return (x, y);
            }
        }
    }

    private int ToCell(double coordinate)
    {
        double value = Math.Floor(coordinate / cellSize);
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinate), "La coordenada excede el rango del índice espacial.");
        }

        return (int)value;
    }

    private static double DistanceToBounds(Point2D point, Bounds2D bounds)
    {
        double deltaX = point.X < bounds.Left
            ? bounds.Left - point.X
            : point.X > bounds.Right ? point.X - bounds.Right : 0D;
        double deltaY = point.Y < bounds.Top
            ? bounds.Top - point.Y
            : point.Y > bounds.Bottom ? point.Y - bounds.Bottom : 0D;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static void ValidateBounds(Bounds2D bounds)
    {
        if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
            !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }
    }

    private void IncrementVersion() => Interlocked.Increment(ref version);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}
