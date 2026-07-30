using BoardView.Core.Geometry;
using BoardView.GeometryKernel.Graph;
using BoardView.GeometryKernel.Primitives;
using BoardView.GeometryKernel.Topology;

namespace BoardView.GeometryKernel;

/// <summary>
/// Reconstruye topología de página completa. Fusiona extremos equivalentes, crea un grafo
/// no dirigido y reconoce ciclos rectangulares antes de devolver los segmentos restantes.
/// </summary>
public sealed class PageGeometryKernel : IGeometryKernel
{
    private readonly GeometryKernelOptions options;

    /// <summary>Inicializa el núcleo con tolerancias opcionales.</summary>
    public PageGeometryKernel(GeometryKernelOptions? options = null)
    {
        this.options = options ?? new GeometryKernelOptions();
        if (this.options.SnapTolerance <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "La tolerancia de ajuste debe ser mayor que cero.");
        }
    }

    /// <inheritdoc />
    public GeometryKernelResult Build(IEnumerable<GeometrySegment> segments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(segments);

        GeometrySegment[] source = segments.ToArray();
        List<KernelRectangle> rectangles = [];
        HashSet<string> consumed = new(StringComparer.Ordinal);
        int discarded = 0;
        int nodeCount = 0;
        int edgeCount = 0;
        int cycleCount = 0;
        int rejectedCycles = 0;

        foreach (IGrouping<string, GeometrySegment> group in source.GroupBy(static segment => segment.GroupKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GroupGraph graph = BuildGraph(group, ref discarded);
            nodeCount += graph.Nodes.Count;
            edgeCount += graph.Edges.Count;

            foreach (RectangleCandidate candidate in FindRectangles(graph, cancellationToken))
            {
                cycleCount++;
                if (!ValidateRectangle(candidate.Points, out bool axisAligned))
                {
                    rejectedCycles++;
                    continue;
                }

                if (candidate.EdgeIds.Any(consumed.Contains))
                {
                    rejectedCycles++;
                    continue;
                }

                foreach (string edgeId in candidate.EdgeIds)
                {
                    consumed.Add(edgeId);
                }

                rectangles.Add(new KernelRectangle(
                    $"kernel-rectangle-{rectangles.Count + 1}",
                    candidate.Points,
                    candidate.EdgeIds,
                    group.Key,
                    axisAligned));
            }
        }

        GeometrySegment[] remaining = source
            .Where(segment => segment.Length >= options.MinimumEdgeLength && !consumed.Contains(segment.Id))
            .ToArray();

        GeometryKernelDiagnostics diagnostics = new(
            source.Length,
            discarded,
            nodeCount,
            edgeCount,
            cycleCount,
            rectangles.Count,
            rejectedCycles,
            consumed.Count,
            remaining.Length);

        return new GeometryKernelResult(rectangles, remaining, diagnostics);
    }

    private GroupGraph BuildGraph(IEnumerable<GeometrySegment> segments, ref int discarded)
    {
        Dictionary<SnapKey, int> nodeByKey = [];
        List<Point2D> nodes = [];
        List<GraphEdge> edges = [];
        Dictionary<int, List<int>> adjacency = [];

        foreach (GeometrySegment segment in segments)
        {
            if (segment.Length < options.MinimumEdgeLength)
            {
                discarded++;
                continue;
            }

            int startNode = GetOrCreateNode(segment.Start, nodeByKey, nodes, adjacency);
            int endNode = GetOrCreateNode(segment.End, nodeByKey, nodes, adjacency);
            if (startNode == endNode)
            {
                discarded++;
                continue;
            }

            int edgeIndex = edges.Count;
            edges.Add(new GraphEdge(segment.Id, startNode, endNode));
            adjacency[startNode].Add(edgeIndex);
            adjacency[endNode].Add(edgeIndex);
        }

        return new GroupGraph(nodes, edges, adjacency);
    }

    private int GetOrCreateNode(
        Point2D point,
        IDictionary<SnapKey, int> nodeByKey,
        IList<Point2D> nodes,
        IDictionary<int, List<int>> adjacency)
    {
        SnapKey key = SnapKey.From(point, options.SnapTolerance);
        for (long deltaX = -1; deltaX <= 1; deltaX++)
        {
            for (long deltaY = -1; deltaY <= 1; deltaY++)
            {
                SnapKey candidateKey = new(key.X + deltaX, key.Y + deltaY);
                if (nodeByKey.TryGetValue(candidateKey, out int candidateIndex) &&
                    nodes[candidateIndex].DistanceTo(point) <= options.SnapTolerance)
                {
                    return candidateIndex;
                }
            }
        }

        int index = nodes.Count;
        nodeByKey.Add(key, index);
        nodes.Add(point);
        adjacency.Add(index, []);
        return index;
    }

    private IEnumerable<RectangleCandidate> FindRectangles(GroupGraph graph, CancellationToken cancellationToken)
    {
        HashSet<string> emitted = new(StringComparer.Ordinal);

        for (int a = 0; a < graph.Nodes.Count; a++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[] neighbors = GetNeighborNodes(graph, a).Distinct().ToArray();
            for (int firstIndex = 0; firstIndex < neighbors.Length; firstIndex++)
            {
                int b = neighbors[firstIndex];
                for (int secondIndex = firstIndex + 1; secondIndex < neighbors.Length; secondIndex++)
                {
                    int d = neighbors[secondIndex];
                    foreach (int c in GetNeighborNodes(graph, b).Intersect(GetNeighborNodes(graph, d)))
                    {
                        if (c == a || c == b || c == d)
                        {
                            continue;
                        }

                        int[] nodeIds = [a, b, c, d];
                        if (nodeIds.Distinct().Count() != 4)
                        {
                            continue;
                        }

                        GraphEdge? edgeAB = FindEdge(graph, a, b);
                        GraphEdge? edgeBC = FindEdge(graph, b, c);
                        GraphEdge? edgeCD = FindEdge(graph, c, d);
                        GraphEdge? edgeDA = FindEdge(graph, d, a);
                        if (edgeAB is null || edgeBC is null || edgeCD is null || edgeDA is null)
                        {
                            continue;
                        }

                        string[] edgeIds = [edgeAB.Id, edgeBC.Id, edgeCD.Id, edgeDA.Id];
                        string canonical = string.Join('|', edgeIds.Order(StringComparer.Ordinal));
                        if (!emitted.Add(canonical))
                        {
                            continue;
                        }

                        yield return new RectangleCandidate(
                            [graph.Nodes[a], graph.Nodes[b], graph.Nodes[c], graph.Nodes[d]],
                            edgeIds);
                    }
                }
            }
        }
    }

    private static IEnumerable<int> GetNeighborNodes(GroupGraph graph, int node)
    {
        foreach (int edgeIndex in graph.Adjacency[node])
        {
            GraphEdge edge = graph.Edges[edgeIndex];
            yield return edge.StartNode == node ? edge.EndNode : edge.StartNode;
        }
    }

    private static GraphEdge? FindEdge(GroupGraph graph, int first, int second)
    {
        foreach (int edgeIndex in graph.Adjacency[first])
        {
            GraphEdge edge = graph.Edges[edgeIndex];
            if ((edge.StartNode == first && edge.EndNode == second) ||
                (edge.StartNode == second && edge.EndNode == first))
            {
                return edge;
            }
        }

        return null;
    }

    private bool ValidateRectangle(IReadOnlyList<Point2D> points, out bool axisAligned)
    {
        axisAligned = false;
        if (points.Count != 4)
        {
            return false;
        }

        Vector2D ab = points[1] - points[0];
        Vector2D bc = points[2] - points[1];
        Vector2D cd = points[3] - points[2];
        Vector2D da = points[0] - points[3];
        double[] lengths = [ab.Length, bc.Length, cd.Length, da.Length];
        if (lengths.Any(length => length < options.MinimumEdgeLength))
        {
            return false;
        }

        if (!ArePerpendicular(ab, bc) || !ArePerpendicular(bc, cd) ||
            !AreParallel(ab, cd) || !AreParallel(bc, da))
        {
            return false;
        }

        if (!NearlyEqual(lengths[0], lengths[2]) || !NearlyEqual(lengths[1], lengths[3]))
        {
            return false;
        }

        double area = Math.Abs(Cross(ab, bc));
        if (area < options.MinimumRectangleArea)
        {
            return false;
        }

        axisAligned = (IsHorizontal(ab) && IsVertical(bc)) || (IsVertical(ab) && IsHorizontal(bc));
        return true;
    }

    private bool ArePerpendicular(Vector2D first, Vector2D second)
    {
        double denominator = first.Length * second.Length;
        return denominator > double.Epsilon && Math.Abs(Dot(first, second)) / denominator <= options.AngularTolerance;
    }

    private bool AreParallel(Vector2D first, Vector2D second)
    {
        double denominator = first.Length * second.Length;
        return denominator > double.Epsilon && Math.Abs(Cross(first, second)) / denominator <= options.AngularTolerance;
    }

    private bool NearlyEqual(double first, double second)
    {
        double scale = Math.Max(first, second);
        return Math.Abs(first - second) <= Math.Max(options.SnapTolerance, scale * options.AngularTolerance);
    }

    private bool IsHorizontal(Vector2D vector) => Math.Abs(vector.Y) <= options.SnapTolerance;

    private bool IsVertical(Vector2D vector) => Math.Abs(vector.X) <= options.SnapTolerance;

    private static double Dot(Vector2D first, Vector2D second) => (first.X * second.X) + (first.Y * second.Y);

    private static double Cross(Vector2D first, Vector2D second) => (first.X * second.Y) - (first.Y * second.X);

    private readonly record struct SnapKey(long X, long Y)
    {
        public static SnapKey From(Point2D point, double tolerance) =>
            new((long)Math.Round(point.X / tolerance), (long)Math.Round(point.Y / tolerance));
    }

    private sealed record GraphEdge(string Id, int StartNode, int EndNode);

    private sealed record GroupGraph(
        IReadOnlyList<Point2D> Nodes,
        IReadOnlyList<GraphEdge> Edges,
        IReadOnlyDictionary<int, List<int>> Adjacency);

    private sealed record RectangleCandidate(IReadOnlyList<Point2D> Points, IReadOnlyList<string> EdgeIds);
}
