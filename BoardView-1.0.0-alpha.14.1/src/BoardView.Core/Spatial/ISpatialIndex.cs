using BoardView.Core.Geometry;

namespace BoardView.Core.Spatial;

/// <summary>
/// Defines the common contract used by rendering, selection, search and analysis engines
/// to query indexed two-dimensional objects without traversing complete document collections.
/// </summary>
/// <typeparam name="T">Type of object stored by the index.</typeparam>
public interface ISpatialIndex<T> where T : notnull
{
    /// <summary>Gets the number of unique indexed objects.</summary>
    int Count { get; }

    /// <summary>Gets the monotonically increasing index version.</summary>
    long Version { get; }

    /// <summary>Adds one object and its current bounds.</summary>
    void Add(T item, Bounds2D bounds);

    /// <summary>Adds multiple objects as one validated operation.</summary>
    void AddRange(IEnumerable<(T Item, Bounds2D Bounds)> entries);

    /// <summary>Removes an object when it exists.</summary>
    bool Remove(T item);

    /// <summary>Updates the location of an existing object.</summary>
    void Update(T item, Bounds2D bounds);

    /// <summary>Removes every object from the index.</summary>
    void Clear();

    /// <summary>Returns objects whose bounds intersect the requested area.</summary>
    IReadOnlyList<T> Query(Bounds2D area);

    /// <summary>Returns objects around a point using the specified tolerance.</summary>
    IReadOnlyList<T> Query(Point2D point, double tolerance = 0D);

    /// <summary>Executes an advanced spatial query and returns diagnostics.</summary>
    SpatialQueryResult<T> Query(SpatialQuery<T> query);

    /// <summary>Returns a consistent statistics snapshot.</summary>
    SpatialStatistics GetStatistics();
}
