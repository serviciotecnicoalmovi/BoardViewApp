namespace BoardView.Rendering.Recognition;

/// <summary>
/// Consolida las relaciones topológicas verificadas en componentes eléctricas
/// transitables por el BFS.
/// </summary>
/// <remarks>
/// El contrato actual de <see cref="SchematicElectricalGraph"/> conserva
/// exclusivamente nodos geométricos reales o terminales recuperados. Por esa
/// razón esta clase no introduce un nodo sintético <c>Net</c>.
///
/// Cada red queda representada como una componente conexa formada por:
/// <list type="bullet">
/// <item>Wire;</item>
/// <item>Pin;</item>
/// <item>Terminal;</item>
/// <item>Junction;</item>
/// <item>Ground;</item>
/// <item>PowerPort.</item>
/// </list>
///
/// La clase elimina aristas duplicadas, conserva la relación más confiable y
/// eleva la confianza únicamente de conexiones ya demostradas por contacto,
/// intersección o continuidad colineal. No conecta elementos por proximidad.
/// </remarks>
public sealed class SchematicNetBuilder
{
    private const double VerifiedTopologyConfidence = 0.82D;
    private const double VerifiedPinConfidence = 0.88D;
    private const double VerifiedJunctionConfidence = 0.90D;
    private const double VerifiedPowerConfidence = 0.86D;

    /// <summary>
    /// Consolida las redes eléctricas y devuelve la colección definitiva de
    /// aristas del grafo.
    /// </summary>
    public IReadOnlyList<SchematicElectricalEdge> Build(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyList<SchematicElectricalEdge> edges,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(options);

        if (nodes.Count == 0 ||
            edges.Count == 0)
        {
            return edges
                .OrderBy(edge => edge.FirstNodeId)
                .ThenBy(edge => edge.SecondNodeId)
                .ToArray();
        }

        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById =
            nodes.ToDictionary(node => node.Id);

        var bestByPair =
            new Dictionary<
                (int FirstNodeId, int SecondNodeId),
                SchematicElectricalEdge>();

        foreach (SchematicElectricalEdge edge in edges)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!nodesById.TryGetValue(
                    edge.FirstNodeId,
                    out SchematicElectricalNode? first) ||
                !nodesById.TryGetValue(
                    edge.SecondNodeId,
                    out SchematicElectricalNode? second))
            {
                continue;
            }

            SchematicElectricalEdge normalized =
                NormalizeEdge(
                    first,
                    second,
                    edge,
                    options);

            var key =
                (
                    normalized.FirstNodeId,
                    normalized.SecondNodeId);

            if (!bestByPair.TryGetValue(
                    key,
                    out SchematicElectricalEdge? current) ||
                IsBetter(
                    normalized,
                    current))
            {
                bestByPair[key] =
                    normalized;
            }
        }

        SchematicElectricalEdge[] normalizedEdges =
            bestByPair
                .Values
                .OrderBy(edge => edge.FirstNodeId)
                .ThenBy(edge => edge.SecondNodeId)
                .ToArray();

        /*
         * La segunda pasada trabaja por componente conexa. Esto permite
         * confirmar una cadena completa sin crear conexiones nuevas.
         */
        PromoteConnectedNetworks(
            nodesById,
            normalizedEdges,
            options,
            cancellationToken);

        return normalizedEdges
            .OrderBy(edge => edge.FirstNodeId)
            .ThenBy(edge => edge.SecondNodeId)
            .ToArray();
    }

    private static SchematicElectricalEdge NormalizeEdge(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalEdge edge,
        SchematicElectricalGraphBuilderOptions options)
    {
        if (!IsTopologyNode(first) ||
            !IsTopologyNode(second) ||
            !IsVerifiedTopologyKind(edge.Kind))
        {
            return edge;
        }

        if (!IsDistanceCompatible(
                first,
                second,
                edge,
                options))
        {
            return edge;
        }

        double minimumConfidence =
            ResolveVerifiedConfidence(
                first,
                second);

        double confidence =
            Clamp01(
                Math.Max(
                    edge.Confidence,
                    minimumConfidence));

        if (Math.Abs(
                confidence -
                edge.Confidence) <
            0.000001D)
        {
            return edge;
        }

        return CopyWithConfidence(
            edge,
            confidence);
    }

    /// <summary>
    /// Confirma transitividad dentro de cada red ya conectada. Sólo modifica
    /// la confianza de las aristas existentes.
    /// </summary>
    private static void PromoteConnectedNetworks(
        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById,
        SchematicElectricalEdge[] edges,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        var edgeIndexesByNode =
            new Dictionary<int, List<int>>();

        for (int edgeIndex = 0;
             edgeIndex < edges.Length;
             edgeIndex++)
        {
            SchematicElectricalEdge edge =
                edges[edgeIndex];

            if (!nodesById.TryGetValue(
                    edge.FirstNodeId,
                    out SchematicElectricalNode? first) ||
                !nodesById.TryGetValue(
                    edge.SecondNodeId,
                    out SchematicElectricalNode? second) ||
                !IsTopologyNode(first) ||
                !IsTopologyNode(second) ||
                !IsVerifiedTopologyKind(edge.Kind) ||
                !IsDistanceCompatible(
                    first,
                    second,
                    edge,
                    options))
            {
                continue;
            }

            AddEdgeIndex(
                edgeIndexesByNode,
                first.Id,
                edgeIndex);

            AddEdgeIndex(
                edgeIndexesByNode,
                second.Id,
                edgeIndex);
        }

        var visited =
            new HashSet<int>();

        foreach (int startNodeId in
                 edgeIndexesByNode.Keys.OrderBy(id => id))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(startNodeId))
            {
                continue;
            }

            var queue =
                new Queue<int>();

            var componentEdgeIndexes =
                new HashSet<int>();

            var componentNodeIds =
                new HashSet<int>
                {
                    startNodeId
                };

            queue.Enqueue(startNodeId);

            while (queue.Count > 0)
            {
                int currentNodeId =
                    queue.Dequeue();

                if (!edgeIndexesByNode.TryGetValue(
                        currentNodeId,
                        out List<int>? currentEdges))
                {
                    continue;
                }

                foreach (int edgeIndex in currentEdges)
                {
                    componentEdgeIndexes.Add(edgeIndex);

                    SchematicElectricalEdge edge =
                        edges[edgeIndex];

                    int neighborId =
                        edge.GetOtherNodeId(
                            currentNodeId);

                    componentNodeIds.Add(neighborId);

                    if (visited.Add(neighborId))
                    {
                        queue.Enqueue(neighborId);
                    }
                }
            }

            PromoteComponent(
                componentNodeIds,
                componentEdgeIndexes,
                nodesById,
                edges);
        }
    }

    private static void PromoteComponent(
        IReadOnlySet<int> componentNodeIds,
        IReadOnlySet<int> componentEdgeIndexes,
        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById,
        SchematicElectricalEdge[] edges)
    {
        bool containsJunction =
            componentNodeIds
                .Select(nodeId => nodesById[nodeId])
                .Any(node =>
                    node.Kind ==
                    SchematicElectricalNodeKind.Junction);

        bool containsPinOrTerminal =
            componentNodeIds
                .Select(nodeId => nodesById[nodeId])
                .Any(node =>
                    node.Kind is
                        SchematicElectricalNodeKind.Pin or
                        SchematicElectricalNodeKind.Terminal);

        bool containsPowerEndpoint =
            componentNodeIds
                .Select(nodeId => nodesById[nodeId])
                .Any(node =>
                    node.Kind is
                        SchematicElectricalNodeKind.Ground or
                        SchematicElectricalNodeKind.PowerPort);

        double componentFloor =
            VerifiedTopologyConfidence;

        if (containsPinOrTerminal)
        {
            componentFloor =
                Math.Max(
                    componentFloor,
                    VerifiedPinConfidence);
        }

        if (containsJunction)
        {
            componentFloor =
                Math.Max(
                    componentFloor,
                    VerifiedJunctionConfidence);
        }

        if (containsPowerEndpoint)
        {
            componentFloor =
                Math.Max(
                    componentFloor,
                    VerifiedPowerConfidence);
        }

        foreach (int edgeIndex in componentEdgeIndexes)
        {
            SchematicElectricalEdge edge =
                edges[edgeIndex];

            SchematicElectricalNode first =
                nodesById[edge.FirstNodeId];

            SchematicElectricalNode second =
                nodesById[edge.SecondNodeId];

            double pairFloor =
                ResolveVerifiedConfidence(
                    first,
                    second);

            double confidence =
                Clamp01(
                    Math.Max(
                        edge.Confidence,
                        Math.Min(
                            componentFloor,
                            pairFloor + 0.04D)));

            if (confidence >
                edge.Confidence +
                0.000001D)
            {
                edges[edgeIndex] =
                    CopyWithConfidence(
                        edge,
                        confidence);
            }
        }
    }

    private static bool IsDistanceCompatible(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalEdge edge,
        SchematicElectricalGraphBuilderOptions options)
    {
        double thicknessAllowance =
            Math.Max(
                1D,
                Math.Min(
                    MinimumDimension(first),
                    MinimumDimension(second)) *
                0.60D);

        double maximumDistance =
            edge.Kind switch
            {
                SchematicElectricalEdgeKind.BoundsIntersection =>
                    thicknessAllowance,

                SchematicElectricalEdgeKind.BoundsTouch =>
                    options.TouchTolerancePixels +
                    thicknessAllowance,

                SchematicElectricalEdgeKind.EndpointContact =>
                    Math.Max(
                        options.EndpointTolerancePixels,
                        options.EndpointToSegmentTolerancePixels) +
                    thicknessAllowance,

                SchematicElectricalEdgeKind.CollinearGap =>
                    options.MaximumCollinearGapPixels +
                    thicknessAllowance,

                _ =>
                    -1D
            };

        return maximumDistance >= 0D &&
               edge.DistancePixels <=
               maximumDistance;
    }

    private static double ResolveVerifiedConfidence(
        SchematicElectricalNode first,
        SchematicElectricalNode second)
    {
        if (first.Kind ==
                SchematicElectricalNodeKind.Junction ||
            second.Kind ==
                SchematicElectricalNodeKind.Junction)
        {
            return VerifiedJunctionConfidence;
        }

        if (first.Kind is
                SchematicElectricalNodeKind.Ground or
                SchematicElectricalNodeKind.PowerPort ||
            second.Kind is
                SchematicElectricalNodeKind.Ground or
                SchematicElectricalNodeKind.PowerPort)
        {
            return VerifiedPowerConfidence;
        }

        if (first.Kind is
                SchematicElectricalNodeKind.Pin or
                SchematicElectricalNodeKind.Terminal ||
            second.Kind is
                SchematicElectricalNodeKind.Pin or
                SchematicElectricalNodeKind.Terminal)
        {
            return VerifiedPinConfidence;
        }

        return VerifiedTopologyConfidence;
    }

    private static bool IsTopologyNode(
        SchematicElectricalNode node)
    {
        return node.Kind is
            SchematicElectricalNodeKind.Wire or
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal or
            SchematicElectricalNodeKind.Junction or
            SchematicElectricalNodeKind.Ground or
            SchematicElectricalNodeKind.PowerPort;
    }

    private static bool IsVerifiedTopologyKind(
        SchematicElectricalEdgeKind kind)
    {
        return kind is
            SchematicElectricalEdgeKind.BoundsIntersection or
            SchematicElectricalEdgeKind.BoundsTouch or
            SchematicElectricalEdgeKind.EndpointContact or
            SchematicElectricalEdgeKind.CollinearGap;
    }

    private static void AddEdgeIndex(
        IDictionary<int, List<int>> edgeIndexesByNode,
        int nodeId,
        int edgeIndex)
    {
        if (!edgeIndexesByNode.TryGetValue(
                nodeId,
                out List<int>? edgeIndexes))
        {
            edgeIndexes = [];
            edgeIndexesByNode[nodeId] =
                edgeIndexes;
        }

        edgeIndexes.Add(edgeIndex);
    }

    private static bool IsBetter(
        SchematicElectricalEdge candidate,
        SchematicElectricalEdge current)
    {
        if (candidate.Confidence >
            current.Confidence +
            0.000001D)
        {
            return true;
        }

        if (Math.Abs(
                candidate.Confidence -
                current.Confidence) <
            0.000001D &&
            candidate.DistancePixels <
            current.DistancePixels -
            0.000001D)
        {
            return true;
        }

        return false;
    }

    private static SchematicElectricalEdge CopyWithConfidence(
        SchematicElectricalEdge edge,
        double confidence)
    {
        return new SchematicElectricalEdge(
            edge.FirstNodeId,
            edge.SecondNodeId,
            edge.Kind,
            Clamp01(confidence),
            edge.DistancePixels,
            edge.ContactX,
            edge.ContactY);
    }

    private static double MinimumDimension(
        SchematicElectricalNode node)
    {
        return Math.Max(
            1D,
            Math.Min(
                node.Bounds.Width,
                node.Bounds.Height));
    }

    private static double Clamp01(
        double value)
    {
        return Math.Max(
            0D,
            Math.Min(
                1D,
                value));
    }
}