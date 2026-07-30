using BoardView.Core.Documents;
using BoardView.Core.Elements;
using BoardView.Core.Geometry;
using BoardView.Core.Spatial;

namespace BoardView.Rendering.Selection;

/// <summary>
/// Performs viewport selection through the document's shared spatial index. Candidates are
/// ordered from the smallest bounds to the largest so precise objects take selection priority.
/// </summary>
public sealed class BoardHitTester
{
    private readonly BoardDocument document;

    /// <summary>Initializes a hit tester for one normalized board document.</summary>
    public BoardHitTester(BoardDocument document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>Returns visible elements located around the requested point.</summary>
    public IReadOnlyList<BoardElement> HitTest(Point2D point, double tolerance = 0.25D)
    {
        SpatialQueryResult<BoardElement> result = document.Query(
            BoardElementQuery.Near(point, tolerance) with { MaximumResults = 128 });
        return result.Hits
            .Select(static hit => hit.Item)
            .OrderBy(static element => Area(element.Bounds))
            .ThenBy(static element => element.Id, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Returns visible elements intersecting a selection rectangle.</summary>
    public IReadOnlyList<BoardElement> Select(Bounds2D area)
    {
        SpatialQueryResult<BoardElement> result = document.Query(BoardElementQuery.InArea(area));
        return result.Hits.Select(static hit => hit.Item).ToArray();
    }

    private static double Area(Bounds2D bounds) => bounds.Width * bounds.Height;
}
