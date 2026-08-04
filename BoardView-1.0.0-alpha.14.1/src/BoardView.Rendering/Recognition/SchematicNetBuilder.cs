namespace BoardView.Rendering.Recognition;

/// <summary>
/// Consolida las aristas topológicas en redes conductoras transitables.
/// </summary>
/// <remarks>
/// El BFS del ensamblador exige una confianza superior para continuar por
/// cadenas Wire → Wire. Los detectores geométricos pueden producir conexiones
/// correctas con una confianza conservadora. Esta etapa confirma dichas
/// conexiones mediante su tipo, distancia y roles eléctricos, elimina
/// duplicados y eleva únicamente las aristas topológicamente verificadas.
///
/// No crea conexiones nuevas por proximidad y no une cuerpos de símbolos.
/// </remarks>
public sealed class SchematicNetBuilder
{
    /// <summary>
    /// Confianza mínima asignada a una continuidad conductora verificada.
    /// Debe permanecer por encima del umbral de cadenas Wire → Wire utilizado
    /// por SchematicSymbolAssembler.
    /// </summary>
    private const double VerifiedConductorConfidence = 0.78D;

    /// <summary>
    /// Normaliza las aristas del grafo y consolida componentes conductoras.
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

        if (edges.Count == 0)
        {
            return Array.Empty<SchematicElectricalEdge>();
        }

        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById =
            nodes.ToDictionary(node => node.Id);

        var bestByPair =
            new Dictionary<(int First, int Second), SchematicElectricalEdge>();

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

            var pair =
                (
                    normalized.FirstNodeId,
                    normalized.SecondNodeId);

            if (!bestByPair.TryGetValue(
                    pair,
                    out SchematicElectricalEdge? existing) ||
                IsBetter(normalized, existing))
            {
                bestByPair[pair] = normalized;
            }
        }

        /*
         * Las componentes se calculan para validar que la normalización no
         * produzca nodos aislados artificiales. No se crea un nodo Net
         * sintético porque el contrato actual del grafo conserva únicamente
         * geometrías reales del PDF.
         */
        IReadOnlyList<SchematicElectricalEdge> normalizedEdges =
            bestByPair
                .Values
                .OrderBy(edge => edge.FirstNodeId)
                .ThenBy(edge => edge.SecondNodeId)
                .ToArray();

        ValidateConductorComponents(
            nodesById,
            normalizedEdges,
            cancellationToken);

        return normalizedEdges;
    }

    /// <summary>
    /// Eleva la confianza sólo cuando una conexión conductora ya fue probada
    /// por una regla topológica fuerte.
    /// </summary>
    private static SchematicElectricalEdge NormalizeEdge(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalEdge edge,
        SchematicElectricalGraphBuilderOptions options)
    {
        if (!IsConductor(first) ||
            !IsConductor(second))
        {
            return edge;
        }

        bool verifiedKind =
            edge.Kind is
                SchematicElectricalEdgeKind.EndpointContact or
                SchematicElectricalEdgeKind.CollinearGap or
                SchematicElectricalEdgeKind.BoundsIntersection or
                SchematicElectricalEdgeKind.BoundsTouch;

        if (!verifiedKind)
        {
            return edge;
        }

        double allowedDistance =
            edge.Kind switch
            {
                SchematicElectricalEdgeKind.CollinearGap =>
                    options.MaximumCollinearGapPixels,

                SchematicElectricalEdgeKind.EndpointContact =>
                    Math.Max(
                        options.EndpointTolerancePixels,
                        options.EndpointToSegmentTolerancePixels),

                SchematicElectricalEdgeKind.BoundsTouch =>
                    options.TouchTolerancePixels,

                SchematicElectricalEdgeKind.BoundsIntersection =>
                    0D,

                _ =>
                    0D
            };

        if (edge.DistancePixels >
            allowedDistance +
            Math.Max(
                1D,
                Math.Min(
                    MinimumDimension(first),
                    MinimumDimension(second)) *
                0.50D))
        {
            return edge;
        }

        double roleBonus =
            CalculateRoleBonus(
                first,
                second);

        double promotedConfidence =
            Clamp01(
                Math.Max(
                    edge.Confidence,
                    VerifiedConductorConfidence +
                    roleBonus));

        if (Math.Abs(
                promotedConfidence -
                edge.Confidence) <
            0.000001D)
        {
            return edge;
        }

        return new SchematicElectricalEdge(
            edge.FirstNodeId,
            edge.SecondNodeId,
            edge.Kind,
            promotedConfidence,
            edge.DistancePixels,
            edge.ContactX,
            edge.ContactY);
    }

    /// <summary>
    /// Calcula las redes como componentes conexas de conductores. La validación
    /// es deliberadamente no destructiva.
    /// </summary>
    private static void ValidateConductorComponents(
        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById,
        IReadOnlyList<SchematicElectricalEdge> edges,
        CancellationToken cancellationToken)
    {
        var adjacency =
            new Dictionary<int, List<int>>();

        foreach (SchematicElectricalEdge edge in edges)
        {
            if (!nodesById.TryGetValue(
                    edge.FirstNodeId,
                    out SchematicElectricalNode? first) ||
                !nodesById.TryGetValue(
                    edge.SecondNodeId,
                    out SchematicElectricalNode? second) ||
                !IsConductor(first) ||
                !IsConductor(second))
            {
                continue;
            }

            AddNeighbor(
                adjacency,
                first.Id,
                second.Id);

            AddNeighbor(
                adjacency,
                second.Id,
                first.Id);
        }

        var visited =
            new HashSet<int>();

        foreach (int nodeId in adjacency.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(nodeId))
            {
                continue;
            }

            var queue =
                new Queue<int>();

            queue.Enqueue(nodeId);

            while (queue.Count > 0)
            {
                int current =
                    queue.Dequeue();

                if (!adjacency.TryGetValue(
                        current,
                        out List<int>? neighbors))
                {
                    continue;
                }

                foreach (int neighbor in neighbors)
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
    }

    private static void AddNeighbor(
        IDictionary<int, List<int>> adjacency,
        int nodeId,
        int neighborId)
    {
        if (!adjacency.TryGetValue(
                nodeId,
                out List<int>? neighbors))
        {
            neighbors = [];
            adjacency[nodeId] = neighbors;
        }

        neighbors.Add(neighborId);
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
            current.DistancePixels)
        {
            return true;
        }

        return false;
    }

    private static bool IsConductor(
        SchematicElectricalNode node)
    {
        return node.Kind is
            SchematicElectricalNodeKind.Wire or
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal;
    }

    private static double CalculateRoleBonus(
        SchematicElectricalNode first,
        SchematicElectricalNode second)
    {
        bool pinWire =
            (first.Kind ==
                 SchematicElectricalNodeKind.Pin &&
             second.Kind ==
                 SchematicElectricalNodeKind.Wire) ||
            (second.Kind ==
                 SchematicElectricalNodeKind.Pin &&
             first.Kind ==
                 SchematicElectricalNodeKind.Wire);

        if (pinWire)
        {
            return 0.06D;
        }

        bool terminalWire =
            (first.Kind ==
                 SchematicElectricalNodeKind.Terminal &&
             second.Kind ==
                 SchematicElectricalNodeKind.Wire) ||
            (second.Kind ==
                 SchematicElectricalNodeKind.Terminal &&
             first.Kind ==
                 SchematicElectricalNodeKind.Wire);

        if (terminalWire)
        {
            return 0.05D;
        }

        if (first.Kind ==
                SchematicElectricalNodeKind.Wire &&
            second.Kind ==
                SchematicElectricalNodeKind.Wire)
        {
            return 0.03D;
        }

        return 0.02D;
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