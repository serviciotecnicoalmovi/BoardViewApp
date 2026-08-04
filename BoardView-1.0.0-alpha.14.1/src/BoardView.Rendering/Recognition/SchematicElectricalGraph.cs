using System.Collections.ObjectModel;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Grafo eléctrico inmutable de una página esquemática.
/// </summary>
public sealed class SchematicElectricalGraph
{
    private readonly IReadOnlyDictionary<int, SchematicElectricalNode> nodesById;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<SchematicElectricalEdge>> edgesByNodeId;

    public SchematicElectricalGraph(
        int pageWidth,
        int pageHeight,
        IEnumerable<SchematicElectricalNode> nodes,
        IEnumerable<SchematicElectricalEdge> edges)
    {
        if (pageWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageWidth));
        }

        if (pageHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageHeight));
        }

        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        SchematicElectricalNode[] nodeArray =
            nodes
                .OrderBy(node => node.Id)
                .ToArray();

        if (nodeArray
                .Select(node => node.Id)
                .Distinct()
                .Count() !=
            nodeArray.Length)
        {
            throw new ArgumentException(
                "Los identificadores de nodos deben ser únicos.",
                nameof(nodes));
        }

        var mutableNodes =
            nodeArray.ToDictionary(node => node.Id);

        SchematicElectricalEdge[] edgeArray =
            edges
                .OrderBy(edge => edge.FirstNodeId)
                .ThenBy(edge => edge.SecondNodeId)
                .ToArray();

        foreach (SchematicElectricalEdge edge in edgeArray)
        {
            if (!mutableNodes.ContainsKey(edge.FirstNodeId) ||
                !mutableNodes.ContainsKey(edge.SecondNodeId))
            {
                throw new ArgumentException(
                    "Todas las aristas deben referenciar nodos existentes.",
                    nameof(edges));
            }
        }

        PageWidth = pageWidth;
        PageHeight = pageHeight;

        Nodes =
            new ReadOnlyCollection<SchematicElectricalNode>(
                nodeArray);

        Edges =
            new ReadOnlyCollection<SchematicElectricalEdge>(
                edgeArray);

        nodesById =
            new ReadOnlyDictionary<int, SchematicElectricalNode>(
                mutableNodes);

        edgesByNodeId =
            BuildAdjacency(
                nodeArray,
                edgeArray);
    }

    public int PageWidth { get; }

    public int PageHeight { get; }

    public IReadOnlyList<SchematicElectricalNode> Nodes { get; }

    public IReadOnlyList<SchematicElectricalEdge> Edges { get; }

    public int NodeCount =>
        Nodes.Count;

    public int EdgeCount =>
        Edges.Count;

    public bool TryGetNode(
        int nodeId,
        out SchematicElectricalNode? node)
    {
        return nodesById.TryGetValue(
            nodeId,
            out node);
    }

    public IReadOnlyList<SchematicElectricalEdge> GetEdges(
        int nodeId)
    {
        if (!nodesById.ContainsKey(nodeId))
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId));
        }

        return edgesByNodeId[nodeId];
    }

    public IReadOnlyList<SchematicElectricalNode> GetNeighbors(
        int nodeId)
    {
        if (!nodesById.ContainsKey(nodeId))
        {
            throw new ArgumentOutOfRangeException(nameof(nodeId));
        }

        return edgesByNodeId[nodeId]
            .Select(edge =>
                nodesById[edge.GetOtherNodeId(nodeId)])
            .OrderBy(node => node.Id)
            .ToArray();
    }

    /// <summary>
    /// Devuelve el subgrafo conectado alcanzable desde un nodo.
    /// </summary>
    public IReadOnlyList<SchematicElectricalNode> TraverseConnectedNodes(
        int startNodeId,
        int maximumNodes = 512,
        double minimumEdgeConfidence = 0D)
    {
        if (!nodesById.ContainsKey(startNodeId))
        {
            throw new ArgumentOutOfRangeException(nameof(startNodeId));
        }

        if (maximumNodes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumNodes));
        }

        if (!double.IsFinite(minimumEdgeConfidence) ||
            minimumEdgeConfidence < 0D ||
            minimumEdgeConfidence > 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumEdgeConfidence));
        }

        var visited =
            new HashSet<int>
            {
                startNodeId
            };

        var queue =
            new Queue<int>();

        queue.Enqueue(startNodeId);

        while (queue.Count > 0 &&
               visited.Count < maximumNodes)
        {
            int currentNodeId =
                queue.Dequeue();

            foreach (SchematicElectricalEdge edge
                     in edgesByNodeId[currentNodeId])
            {
                if (edge.Confidence <
                    minimumEdgeConfidence)
                {
                    continue;
                }

                int neighborId =
                    edge.GetOtherNodeId(currentNodeId);

                if (!visited.Add(neighborId))
                {
                    continue;
                }

                queue.Enqueue(neighborId);

                if (visited.Count >= maximumNodes)
                {
                    break;
                }
            }
        }

        return visited
            .Select(nodeId => nodesById[nodeId])
            .OrderBy(node => node.Id)
            .ToArray();
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<SchematicElectricalEdge>> BuildAdjacency(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyList<SchematicElectricalEdge> edges)
    {
        var mutable =
            nodes.ToDictionary(
                node => node.Id,
                _ => new List<SchematicElectricalEdge>());

        foreach (SchematicElectricalEdge edge in edges)
        {
            mutable[edge.FirstNodeId].Add(edge);
            mutable[edge.SecondNodeId].Add(edge);
        }

        return new ReadOnlyDictionary<int, IReadOnlyList<SchematicElectricalEdge>>(
            mutable.ToDictionary(
                pair => pair.Key,
                pair =>
                    (IReadOnlyList<SchematicElectricalEdge>)
                    new ReadOnlyCollection<SchematicElectricalEdge>(
                        pair.Value
                            .OrderByDescending(edge => edge.Confidence)
                            .ThenBy(edge => edge.DistancePixels)
                            .ThenBy(edge => edge.FirstNodeId)
                            .ThenBy(edge => edge.SecondNodeId)
                            .ToList())));
    }
}
