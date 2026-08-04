using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Construye el grafo de conectividad eléctrica aproximada de una página
/// esquemática.
/// </summary>
/// <remarks>
/// Esta versión enriquecida distingue cuerpos, pines, segmentos de red,
/// uniones y terminales. Las aristas ya no se crean únicamente por cercanía:
/// se evalúan contacto de extremos, extremo contra segmento, continuidad
/// colineal, cruces ortogonales y relación pin-cuerpo.
/// </remarks>
public sealed class SchematicElectricalGraphBuilder
{
    /// <summary>
    /// Construye el grafo con la configuración predeterminada.
    /// </summary>
    public SchematicElectricalGraph Build(
        BoardGeometryIndex geometryIndex)
    {
        return Build(
            geometryIndex,
            SchematicElectricalGraphBuilderOptions.Default,
            CancellationToken.None);
    }

    /// <summary>
    /// Construye un grafo eléctrico inmutable para la página indexada.
    /// </summary>
    public SchematicElectricalGraph Build(
        BoardGeometryIndex geometryIndex,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geometryIndex);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        BoardGeometryIndexQueryOptions queryOptions =
            CreateQueryOptions(options);

        SchematicElectricalNode[] nodes =
            geometryIndex.Components
                .Where(component =>
                    component.Confidence >=
                    options.MinimumComponentConfidence)
                .Where(component =>
                    !queryOptions.ExcludedTypes.Contains(
                        component.Type))
                .Select(component =>
                    new SchematicElectricalNode(
                        component.Id,
                        ClassifyNode(
                            component,
                            options),
                        component))
                .OrderBy(node => node.Id)
                .ToArray();

        var nodesById =
            nodes.ToDictionary(
                node => node.Id);

        var evaluatedPairs =
            new HashSet<(int First, int Second)>();

        var edges =
            new List<SchematicElectricalEdge>();

        foreach (SchematicElectricalNode node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double searchRadius =
                ResolveSearchRadius(
                    node,
                    options);

            IReadOnlyList<BoardGeometryIndexedComponent> nearbyComponents =
                geometryIndex.QueryNearest(
                    node.CenterX,
                    node.CenterY,
                    searchRadius,
                    options.MaximumNeighborsPerNode,
                    queryOptions);

            foreach (BoardGeometryIndexedComponent nearbyComponent
                     in nearbyComponents)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (nearbyComponent.Id == node.Id ||
                    !nodesById.TryGetValue(
                        nearbyComponent.Id,
                        out SchematicElectricalNode? neighbor))
                {
                    continue;
                }

                int firstId =
                    Math.Min(
                        node.Id,
                        neighbor.Id);

                int secondId =
                    Math.Max(
                        node.Id,
                        neighbor.Id);

                if (!evaluatedPairs.Add(
                        (firstId, secondId)))
                {
                    continue;
                }

                ConnectionEvaluation evaluation =
                    EvaluateConnection(
                        node,
                        neighbor,
                        options);

                if (!evaluation.IsConnected ||
                    evaluation.Confidence <
                    options.MinimumEdgeConfidence)
                {
                    continue;
                }

                edges.Add(
                    new SchematicElectricalEdge(
                        firstId,
                        secondId,
                        evaluation.Kind,
                        evaluation.Confidence,
                        evaluation.DistancePixels,
                        evaluation.ContactX,
                        evaluation.ContactY));
            }
        }

        IReadOnlyList<SchematicElectricalEdge> normalizedEdges =
            NormalizeTopology(
                nodes,
                edges,
                options);

        return new SchematicElectricalGraph(
            geometryIndex.PageWidth,
            geometryIndex.PageHeight,
            nodes,
            normalizedEdges);
    }

    /// <summary>
    /// Normaliza la topología después de evaluar las relaciones locales.
    /// </summary>
    /// <remarks>
    /// Esta pasada resuelve tres ambigüedades que no pueden decidirse
    /// correctamente observando una pareja aislada:
    ///
    /// <list type="bullet">
    /// <item>un pin o terminal pertenece a un único cuerpo principal;</item>
    /// <item>un cuerpo no debe conectarse directamente a una red cuando existe
    /// un pin intermedio que representa esa unión;</item>
    /// <item>una unión compacta débil no debe fusionar redes por sí sola.</item>
    /// </list>
    /// </remarks>
    private static IReadOnlyList<SchematicElectricalEdge> NormalizeTopology(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyList<SchematicElectricalEdge> edges,
        SchematicElectricalGraphBuilderOptions options)
    {
        if (edges.Count == 0)
        {
            return Array.Empty<SchematicElectricalEdge>();
        }

        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById =
            nodes.ToDictionary(
                node =>
                    node.Id);

        var normalizedEdges =
            edges.ToList();

        /*
         * Segunda pasada global para recuperar continuidad que puede quedar
         * fuera de las consultas QueryNearest locales por fragmentación,
         * límites de vecinos o pequeñas discontinuidades del rasterizado.
         */
        AddGlobalConductorConnections(
            nodes,
            normalizedEdges,
            options);

        AddExplicitJunctionConnections(
            nodes,
            normalizedEdges,
            options);

        RecoverBodyTerminalConnections(
            nodes,
            normalizedEdges,
            options);

        var retained =
            new HashSet<SchematicElectricalEdge>(
                normalizedEdges);

        ResolveExclusiveBodyOwnership(
            nodesById,
            normalizedEdges,
            retained);

        RemoveRedundantBodyWireEdges(
            nodesById,
            normalizedEdges,
            retained,
            options);

        RemoveWeakFalseJunctions(
            nodesById,
            normalizedEdges,
            retained,
            options);

        return retained
            .OrderBy(
                edge =>
                    edge.FirstNodeId)
            .ThenBy(
                edge =>
                    edge.SecondNodeId)
            .ToArray();
    }

    /// <summary>
    /// Recupera conexiones entre conductores fragmentados mediante una pasada
    /// global determinista. No fusiona nodos: añade aristas topológicas para
    /// que el BFS pueda atravesar una red continua aun cuando el PDF la haya
    /// dividido en varios componentes geométricos.
    /// </summary>
    private static void AddGlobalConductorConnections(
        IReadOnlyList<SchematicElectricalNode> nodes,
        ICollection<SchematicElectricalEdge> edges,
        SchematicElectricalGraphBuilderOptions options)
    {
        SchematicElectricalNode[] conductors =
            nodes
                .Where(node => node.IsWireLike)
                .OrderBy(node => node.Bounds.Left)
                .ThenBy(node => node.Bounds.Top)
                .ThenBy(node => node.Id)
                .ToArray();

        var existingPairs =
            edges
                .Select(edge =>
                    (edge.FirstNodeId, edge.SecondNodeId))
                .ToHashSet();

        double maximumForwardDistance =
            Math.Max(
                options.MaximumCollinearGapPixels,
                options.EndpointToSegmentTolerancePixels) +
            options.CollinearAxisTolerancePixels;

        for (int firstIndex = 0;
             firstIndex < conductors.Length;
             firstIndex++)
        {
            SchematicElectricalNode first =
                conductors[firstIndex];

            for (int secondIndex = firstIndex + 1;
                 secondIndex < conductors.Length;
                 secondIndex++)
            {
                SchematicElectricalNode second =
                    conductors[secondIndex];

                if (second.Bounds.Left >
                    first.Bounds.Right +
                    maximumForwardDistance)
                {
                    break;
                }

                int firstId =
                    Math.Min(first.Id, second.Id);

                int secondId =
                    Math.Max(first.Id, second.Id);

                if (existingPairs.Contains((firstId, secondId)))
                {
                    continue;
                }

                ConnectionEvaluation evaluation =
                    EvaluateCollinearConnection(
                        first,
                        second,
                        options);

                if (!evaluation.IsConnected)
                {
                    evaluation =
                        EvaluateEndpointToSegmentConnection(
                            first,
                            second,
                            options);
                }

                if (!evaluation.IsConnected)
                {
                    evaluation =
                        EvaluateEndpointContact(
                            first,
                            second,
                            options);
                }

                if (!evaluation.IsConnected ||
                    evaluation.Confidence <
                    options.MinimumEdgeConfidence)
                {
                    continue;
                }

                edges.Add(
                    new SchematicElectricalEdge(
                        firstId,
                        secondId,
                        evaluation.Kind,
                        evaluation.Confidence,
                        evaluation.DistancePixels,
                        evaluation.ContactX,
                        evaluation.ContactY));

                existingPairs.Add((firstId, secondId));
            }
        }
    }

    /// <summary>
    /// Conecta explícitamente cada punto de unión con todos los segmentos que
    /// realmente alcanzan su centro. Esta pasada permite representar uniones
    /// en T y cruces con punto aun cuando la segmentación produzca múltiples
    /// componentes pequeños alrededor de la unión.
    /// </summary>
    private static void AddExplicitJunctionConnections(
        IReadOnlyList<SchematicElectricalNode> nodes,
        ICollection<SchematicElectricalEdge> edges,
        SchematicElectricalGraphBuilderOptions options)
    {
        SchematicElectricalNode[] junctions =
            nodes
                .Where(node =>
                    node.Kind ==
                    SchematicElectricalNodeKind.Junction)
                .OrderBy(node => node.Id)
                .ToArray();

        SchematicElectricalNode[] conductors =
            nodes
                .Where(node => node.IsWireLike)
                .OrderBy(node => node.Id)
                .ToArray();

        var existingPairs =
            edges
                .Select(edge =>
                    (edge.FirstNodeId, edge.SecondNodeId))
                .ToHashSet();

        foreach (SchematicElectricalNode junction in junctions)
        {
            foreach (SchematicElectricalNode conductor in conductors)
            {
                int firstId =
                    Math.Min(junction.Id, conductor.Id);

                int secondId =
                    Math.Max(junction.Id, conductor.Id);

                if (existingPairs.Contains((firstId, secondId)))
                {
                    continue;
                }

                double distance =
                    DistancePointToBounds(
                        junction.CenterX,
                        junction.CenterY,
                        conductor.Bounds);

                if (distance >
                    options.JunctionConnectionTolerancePixels)
                {
                    continue;
                }

                double distanceScore =
                    Clamp01(
                        1D -
                        (distance /
                         Math.Max(
                             1D,
                             options.JunctionConnectionTolerancePixels)));

                double confidence =
                    Clamp01(
                        options.JunctionBaseConfidence +
                        (distanceScore *
                         options.JunctionDistanceWeight));

                if (confidence <
                    options.MinimumEdgeConfidence)
                {
                    continue;
                }

                edges.Add(
                    new SchematicElectricalEdge(
                        firstId,
                        secondId,
                        SchematicElectricalEdgeKind.EndpointContact,
                        confidence,
                        distance,
                        junction.CenterX,
                        junction.CenterY));

                existingPairs.Add((firstId, secondId));
            }
        }
    }

    /// <summary>
    /// Recupera relaciones cuerpo-terminal que pueden perderse cuando el PDF
    /// fragmenta un pin en varios componentes o cuando QueryNearest agota su
    /// límite de vecinos alrededor de una zona densa.
    /// </summary>
    /// <remarks>
    /// La recuperación es deliberadamente estricta: sólo considera pines,
    /// terminales y conductores cortos; exige alineación con uno de los bordes
    /// del cuerpo y una separación reducida. Nunca conecta un cuerpo con una
    /// línea de red larga, evitando que el BFS salte directamente a toda la net.
    /// </remarks>
    private static void RecoverBodyTerminalConnections(
        IReadOnlyList<SchematicElectricalNode> nodes,
        ICollection<SchematicElectricalEdge> edges,
        SchematicElectricalGraphBuilderOptions options)
    {
        SchematicElectricalNode[] bodies =
            nodes
                .Where(node =>
                    node.Kind ==
                    SchematicElectricalNodeKind.SymbolBody)
                .OrderBy(node => node.Id)
                .ToArray();

        SchematicElectricalNode[] terminalCandidates =
            nodes
                .Where(node =>
                    node.Kind is
                        SchematicElectricalNodeKind.Pin or
                        SchematicElectricalNodeKind.Terminal or
                        SchematicElectricalNodeKind.Wire)
                .Where(node =>
                    Math.Max(
                        node.Bounds.Width,
                        node.Bounds.Height) <=
                    options.MaximumRecoverableTerminalLengthPixels)
                .OrderBy(node => node.Id)
                .ToArray();

        var existingPairs =
            edges
                .Select(edge =>
                    (edge.FirstNodeId, edge.SecondNodeId))
                .ToHashSet();

        foreach (SchematicElectricalNode body in bodies)
        {
            foreach (SchematicElectricalNode terminal in terminalCandidates)
            {
                int firstId = Math.Min(body.Id, terminal.Id);
                int secondId = Math.Max(body.Id, terminal.Id);

                if (existingPairs.Contains((firstId, secondId)))
                {
                    continue;
                }

                double distance =
                    DistanceBetweenBounds(
                        body.Bounds,
                        terminal.Bounds);

                if (distance >
                    options.MaximumRecoveredBodyTerminalGapPixels)
                {
                    continue;
                }

                double alignment =
                    CalculateBestAxisAlignment(
                        body.Bounds,
                        terminal.Bounds);

                if (alignment <
                    options.MinimumRecoveredBodyTerminalAlignment)
                {
                    continue;
                }

                EndpointProjection terminalToBody =
                    FindBestEndpointProjection(
                        terminal.Bounds,
                        body.Bounds);

                if (terminalToBody.DistancePixels >
                    options.MaximumRecoveredBodyTerminalGapPixels)
                {
                    continue;
                }

                double distanceScore =
                    Clamp01(
                        1D -
                        (terminalToBody.DistancePixels /
                         Math.Max(
                             1D,
                             options.MaximumRecoveredBodyTerminalGapPixels)));

                double confidence =
                    Clamp01(
                        options.RecoveredBodyTerminalBaseConfidence +
                        (distanceScore *
                         options.RecoveredBodyTerminalDistanceWeight) +
                        (alignment *
                         options.AxisOverlapWeight));

                if (confidence <
                    options.MinimumEdgeConfidence)
                {
                    continue;
                }

                edges.Add(
                    new SchematicElectricalEdge(
                        firstId,
                        secondId,
                        SchematicElectricalEdgeKind.EndpointContact,
                        confidence,
                        terminalToBody.DistancePixels,
                        terminalToBody.ContactX,
                        terminalToBody.ContactY));

                existingPairs.Add((firstId, secondId));
            }
        }
    }

    /// <summary>
    /// Conserva solamente la mejor relación cuerpo-pin para cada pin o
    /// terminal. Esto impide que un terminal situado entre dos componentes
    /// termine perteneciendo simultáneamente a ambos.
    /// </summary>
    private static void ResolveExclusiveBodyOwnership(
        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById,
        IReadOnlyList<SchematicElectricalEdge> edges,
        ISet<SchematicElectricalEdge> retained)
    {
        IEnumerable<IGrouping<int, BodyOwnershipCandidate>> candidatesByPin =
            edges
                .Select(
                    edge =>
                        TryCreateBodyOwnershipCandidate(
                            edge,
                            nodesById))
                .Where(
                    candidate =>
                        candidate is not null)
                .Select(
                    candidate =>
                        candidate!.Value)
                .GroupBy(
                    candidate =>
                        candidate.PinNodeId);

        foreach (IGrouping<int, BodyOwnershipCandidate> group
                 in candidatesByPin)
        {
            BodyOwnershipCandidate winner =
                group
                    .OrderByDescending(
                        candidate =>
                            candidate.Edge.Confidence)
                    .ThenBy(
                        candidate =>
                            candidate.Edge.DistancePixels)
                    .ThenBy(
                        candidate =>
                            candidate.BodyNodeId)
                    .First();

            foreach (BodyOwnershipCandidate candidate
                     in group)
            {
                if (!ReferenceEquals(
                        candidate.Edge,
                        winner.Edge) &&
                    candidate.Edge !=
                    winner.Edge)
                {
                    retained.Remove(
                        candidate.Edge);
                }
            }
        }
    }

    private static BodyOwnershipCandidate? TryCreateBodyOwnershipCandidate(
        SchematicElectricalEdge edge,
        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById)
    {
        SchematicElectricalNode first =
            nodesById[edge.FirstNodeId];

        SchematicElectricalNode second =
            nodesById[edge.SecondNodeId];

        bool firstIsBody =
            first.Kind ==
            SchematicElectricalNodeKind.SymbolBody;

        bool secondIsBody =
            second.Kind ==
            SchematicElectricalNodeKind.SymbolBody;

        bool firstIsPin =
            first.Kind is
                SchematicElectricalNodeKind.Pin or
                SchematicElectricalNodeKind.Terminal;

        bool secondIsPin =
            second.Kind is
                SchematicElectricalNodeKind.Pin or
                SchematicElectricalNodeKind.Terminal;

        if (firstIsBody &&
            secondIsPin)
        {
            return new BodyOwnershipCandidate(
                second.Id,
                first.Id,
                edge);
        }

        if (secondIsBody &&
            firstIsPin)
        {
            return new BodyOwnershipCandidate(
                first.Id,
                second.Id,
                edge);
        }

        return null;
    }

    /// <summary>
    /// Elimina una relación directa cuerpo-red cuando el mismo contacto ya
    /// está modelado mediante cuerpo-pin y pin-red.
    /// </summary>
    private static void RemoveRedundantBodyWireEdges(
        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById,
        IReadOnlyList<SchematicElectricalEdge> edges,
        ISet<SchematicElectricalEdge> retained,
        SchematicElectricalGraphBuilderOptions options)
    {
        IReadOnlyDictionary<int, IReadOnlyList<SchematicElectricalEdge>> adjacency =
            BuildTemporaryAdjacency(
                nodesById.Keys,
                edges);

        foreach (SchematicElectricalEdge edge
                 in edges)
        {
            if (!retained.Contains(
                    edge))
            {
                continue;
            }

            SchematicElectricalNode first =
                nodesById[edge.FirstNodeId];

            SchematicElectricalNode second =
                nodesById[edge.SecondNodeId];

            SchematicElectricalNode? body =
                first.Kind ==
                SchematicElectricalNodeKind.SymbolBody
                    ? first
                    : second.Kind ==
                      SchematicElectricalNodeKind.SymbolBody
                        ? second
                        : null;

            if (body is null)
            {
                continue;
            }

            SchematicElectricalNode other =
                body.Id ==
                first.Id
                    ? second
                    : first;

            if (other.Kind is not
                (SchematicElectricalNodeKind.Wire or
                 SchematicElectricalNodeKind.Junction))
            {
                continue;
            }

            bool hasPinBridge =
                adjacency[body.Id]
                    .Where(
                        bodyEdge =>
                            retained.Contains(
                                bodyEdge))
                    .Select(
                        bodyEdge =>
                            nodesById[
                                bodyEdge.GetOtherNodeId(
                                    body.Id)])
                    .Where(
                        node =>
                            node.Kind is
                                SchematicElectricalNodeKind.Pin or
                                SchematicElectricalNodeKind.Terminal)
                    .Any(
                        pin =>
                            adjacency[pin.Id]
                                .Where(
                                    pinEdge =>
                                        retained.Contains(
                                            pinEdge))
                                .Any(
                                    pinEdge =>
                                        pinEdge.GetOtherNodeId(
                                            pin.Id) ==
                                        other.Id));

            if (hasPinBridge &&
                edge.Confidence <
                options.RedundantBodyWireRetentionConfidence)
            {
                retained.Remove(
                    edge);
            }
        }
    }

    /// <summary>
    /// Una unión real debe participar en al menos dos relaciones lineales
    /// confiables. Las uniones compactas con una sola relación débil suelen ser
    /// texto rasterizado o ruido de segmentación.
    /// </summary>
    private static void RemoveWeakFalseJunctions(
        IReadOnlyDictionary<int, SchematicElectricalNode> nodesById,
        IReadOnlyList<SchematicElectricalEdge> edges,
        ISet<SchematicElectricalEdge> retained,
        SchematicElectricalGraphBuilderOptions options)
    {
        IReadOnlyDictionary<int, IReadOnlyList<SchematicElectricalEdge>> adjacency =
            BuildTemporaryAdjacency(
                nodesById.Keys,
                edges);

        foreach (SchematicElectricalNode junction
                 in nodesById.Values.Where(
                     node =>
                         node.Kind ==
                         SchematicElectricalNodeKind.Junction))
        {
            SchematicElectricalEdge[] activeLinearEdges =
                adjacency[junction.Id]
                    .Where(
                        edge =>
                            retained.Contains(
                                edge))
                    .Where(
                        edge =>
                        {
                            SchematicElectricalNode other =
                                nodesById[
                                    edge.GetOtherNodeId(
                                        junction.Id)];

                            return other.IsWireLike ||
                                   other.Kind ==
                                   SchematicElectricalNodeKind.Wire;
                        })
                    .OrderByDescending(
                        edge =>
                            edge.Confidence)
                    .ToArray();

            if (activeLinearEdges.Length >= 2)
            {
                continue;
            }

            foreach (SchematicElectricalEdge weakEdge
                     in activeLinearEdges.Where(
                         edge =>
                             edge.Confidence <
                             options.MinimumSingleJunctionEdgeConfidence))
            {
                retained.Remove(
                    weakEdge);
            }
        }
    }

    private static IReadOnlyDictionary<int, IReadOnlyList<SchematicElectricalEdge>> BuildTemporaryAdjacency(
        IEnumerable<int> nodeIds,
        IEnumerable<SchematicElectricalEdge> edges)
    {
        var adjacency =
            nodeIds.ToDictionary(
                id =>
                    id,
                _ =>
                    new List<SchematicElectricalEdge>());

        foreach (SchematicElectricalEdge edge
                 in edges)
        {
            adjacency[edge.FirstNodeId].Add(
                edge);

            adjacency[edge.SecondNodeId].Add(
                edge);
        }

        return adjacency.ToDictionary(
            pair =>
                pair.Key,
            pair =>
                (IReadOnlyList<SchematicElectricalEdge>)pair.Value);
    }

    private readonly record struct BodyOwnershipCandidate(
        int PinNodeId,
        int BodyNodeId,
        SchematicElectricalEdge Edge);

    /// <summary>
    /// Construye los filtros utilizados por las consultas espaciales.
    /// </summary>
    private static BoardGeometryIndexQueryOptions CreateQueryOptions(
        SchematicElectricalGraphBuilderOptions options)
    {
        return new BoardGeometryIndexQueryOptions
        {
            MinimumConfidence =
                options.MinimumComponentConfidence,

            ExcludedTypes =
                new HashSet<BoardGeometryComponentType>
                {
                    BoardGeometryComponentType.Noise,
                    BoardGeometryComponentType.Text,
                    BoardGeometryComponentType.Silkscreen,
                    BoardGeometryComponentType.BoardOutline
                }
        };
    }

    /// <summary>
    /// Clasifica el rol eléctrico aproximado de una geometría.
    /// </summary>
    private static SchematicElectricalNodeKind ClassifyNode(
        BoardGeometryIndexedComponent component,
        SchematicElectricalGraphBuilderOptions options)
    {
        BoardGeometryBounds bounds =
            component.Bounds;

        double width =
            Math.Max(
                1D,
                bounds.Width);

        double height =
            Math.Max(
                1D,
                bounds.Height);

        double minimumDimension =
            Math.Min(
                width,
                height);

        double maximumDimension =
            Math.Max(
                width,
                height);

        double aspectRatio =
            maximumDimension /
            minimumDimension;

        double area =
            width *
            height;

        if (component.Type ==
            BoardGeometryComponentType.ComponentBody)
        {
            return SchematicElectricalNodeKind.SymbolBody;
        }

        if (component.Type ==
            BoardGeometryComponentType.Hole)
        {
            return SchematicElectricalNodeKind.Hole;
        }

        if (component.Type ==
            BoardGeometryComponentType.Pad)
        {
            return area <=
                   options.MaximumJunctionAreaPixels
                ? SchematicElectricalNodeKind.Junction
                : SchematicElectricalNodeKind.Pad;
        }

        bool thinLinearGeometry =
            minimumDimension <=
                options.MaximumWireThicknessPixels &&
            aspectRatio >=
                options.MinimumWireAspectRatio;

        if (thinLinearGeometry)
        {
            /*
             * Los segmentos cortos suelen ser pines o terminales. Los largos
             * representan con mayor probabilidad una red.
             */
            if (maximumDimension <=
                options.MaximumPinLengthPixels)
            {
                return SchematicElectricalNodeKind.Pin;
            }

            return SchematicElectricalNodeKind.Wire;
        }

        if (area <=
                options.MaximumJunctionAreaPixels &&
            aspectRatio <=
                options.MaximumJunctionAspectRatio)
        {
            return SchematicElectricalNodeKind.Junction;
        }

        if (aspectRatio >=
                options.MinimumTerminalAspectRatio &&
            maximumDimension <=
                options.MaximumTerminalLengthPixels &&
            minimumDimension <=
                options.MaximumPinThicknessPixels)
        {
            return SchematicElectricalNodeKind.Terminal;
        }

        if (component.Type ==
            BoardGeometryComponentType.Copper)
        {
            return SchematicElectricalNodeKind.Wire;
        }

        return SchematicElectricalNodeKind.Unknown;
    }

    /// <summary>
    /// Calcula el radio necesario para descubrir todas las conexiones
    /// plausibles de un nodo.
    /// </summary>
    private static double ResolveSearchRadius(
        SchematicElectricalNode node,
        SchematicElectricalGraphBuilderOptions options)
    {
        double scale =
            Math.Max(
                node.Bounds.Width,
                node.Bounds.Height);

        double roleMultiplier =
            node.Kind switch
            {
                SchematicElectricalNodeKind.Wire => 1.20D,
                SchematicElectricalNodeKind.Pin => 1.10D,
                SchematicElectricalNodeKind.Terminal => 1.10D,
                SchematicElectricalNodeKind.SymbolBody => 1.35D,
                SchematicElectricalNodeKind.Junction => 0.90D,
                _ => 1D
            };

        return Math.Max(
            options.MinimumSearchRadiusPixels,
            Math.Min(
                options.MaximumSearchRadiusPixels,
                (scale *
                 options.SearchRadiusScaleFactor *
                 roleMultiplier) +
                options.MaximumConnectionGapPixels));
    }

    /// <summary>
    /// Evalúa todas las reglas de conectividad entre dos nodos.
    /// </summary>
    private static ConnectionEvaluation EvaluateConnection(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        BoardGeometryBounds firstBounds =
            first.Bounds;

        BoardGeometryBounds secondBounds =
            second.Bounds;

        double boundsDistance =
            DistanceBetweenBounds(
                firstBounds,
                secondBounds);

        (double contactX, double contactY) =
            CalculateClosestContactPoint(
                firstBounds,
                secondBounds);

        if (Intersects(
                firstBounds,
                secondBounds))
        {
            /*
             * Dos trazos que se cruzan no están necesariamente conectados.
             * En esquemáticos es frecuente que una red atraviese otra sin
             * punto de unión. Para geometrías lineales exigimos continuidad
             * colineal o contacto real de un extremo con el otro segmento.
             * Si existe un punto de unión, éste será un nodo Junction separado
             * y conectará ambos trazos mediante sus propias aristas.
             */
            if (first.IsWireLike &&
                second.IsWireLike)
            {
                ConnectionEvaluation wireIntersection =
                    EvaluateWireIntersection(
                        first,
                        second,
                        options);

                return wireIntersection;
            }

            double confidence =
                Clamp01(
                    options.IntersectionBaseConfidence +
                    CalculateRoleCompatibilityBonus(
                        first,
                        second));

            return new ConnectionEvaluation(
                true,
                SchematicElectricalEdgeKind.BoundsIntersection,
                confidence,
                0D,
                contactX,
                contactY);
        }

        /*
         * Un punto de unión pequeño conectado a un segmento debe prevalecer
         * sobre cualquier regla genérica de proximidad.
         */
        ConnectionEvaluation junctionConnection =
            EvaluateJunctionConnection(
                first,
                second,
                options);

        if (junctionConnection.IsConnected)
        {
            return junctionConnection;
        }

        /*
         * Relación pin-cuerpo: un pin suele terminar junto al borde del cuerpo
         * sin que sus rectángulos necesariamente se intersecten.
         */
        ConnectionEvaluation bodyPinConnection =
            EvaluateBodyPinConnection(
                first,
                second,
                options);

        if (bodyPinConnection.IsConnected)
        {
            return bodyPinConnection;
        }

        /*
         * Detecta T-junctions: el extremo de un segmento cae sobre el interior
         * de otro segmento.
         */
        ConnectionEvaluation endpointToSegment =
            EvaluateEndpointToSegmentConnection(
                first,
                second,
                options);

        if (endpointToSegment.IsConnected)
        {
            return endpointToSegment;
        }

        /*
         * Detecta contacto entre extremos, frecuente entre pin, terminal y
         * segmento de red.
         */
        ConnectionEvaluation endpointContact =
            EvaluateEndpointContact(
                first,
                second,
                options);

        if (endpointContact.IsConnected)
        {
            return endpointContact;
        }

        /*
         * Detecta continuidad colineal con un espacio pequeño entre segmentos.
         */
        ConnectionEvaluation collinearConnection =
            EvaluateCollinearConnection(
                first,
                second,
                options);

        if (collinearConnection.IsConnected)
        {
            return collinearConnection;
        }

        /*
         * Un contacto muy próximo y alineado puede representar separación
         * producida por antialiasing o segmentación.
         */
        if (boundsDistance <=
            options.TouchTolerancePixels)
        {
            double alignment =
                CalculateBestAxisAlignment(
                    firstBounds,
                    secondBounds);

            double confidence =
                Clamp01(
                    options.TouchBaseConfidence +
                    (alignment *
                     options.AxisOverlapWeight) +
                    CalculateRoleCompatibilityBonus(
                        first,
                        second));

            return new ConnectionEvaluation(
                confidence >=
                    options.MinimumEdgeConfidence,
                SchematicElectricalEdgeKind.BoundsTouch,
                confidence,
                boundsDistance,
                contactX,
                contactY);
        }

        /*
         * La proximidad pura se reserva para parejas eléctricamente
         * compatibles. Esto evita unir símbolos vecinos sólo porque estén
         * próximos.
         */
        if (boundsDistance <=
                options.MaximumConnectionGapPixels &&
            IsProximityCompatible(
                first,
                second))
        {
            double distanceScore =
                Clamp01(
                    1D -
                    (boundsDistance /
                     Math.Max(
                         1D,
                         options.MaximumConnectionGapPixels)));

            double alignmentScore =
                CalculateBestAxisAlignment(
                    firstBounds,
                    secondBounds);

            double confidence =
                Clamp01(
                    options.ProximityBaseConfidence +
                    (distanceScore *
                     options.ProximityDistanceWeight) +
                    (alignmentScore *
                     options.AxisOverlapWeight) +
                    CalculateRoleCompatibilityBonus(
                        first,
                        second));

            return new ConnectionEvaluation(
                confidence >=
                    options.MinimumEdgeConfidence,
                SchematicElectricalEdgeKind.Proximity,
                confidence,
                boundsDistance,
                contactX,
                contactY);
        }

        return ConnectionEvaluation.NotConnected;
    }

    /// <summary>
    /// Evalúa la intersección entre dos geometrías lineales sin asumir que
    /// todo cruce ortogonal representa una unión eléctrica.
    /// </summary>
    private static ConnectionEvaluation EvaluateWireIntersection(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        SegmentOrientation firstOrientation =
            ResolveOrientation(first.Bounds);

        SegmentOrientation secondOrientation =
            ResolveOrientation(second.Bounds);

        /*
         * Segmentos colineales solapados sí representan continuidad.
         */
        if (firstOrientation == secondOrientation &&
            firstOrientation is not SegmentOrientation.Compact)
        {
            double axisOffset =
                firstOrientation == SegmentOrientation.Horizontal
                    ? Math.Abs(GetCenterY(first.Bounds) - GetCenterY(second.Bounds))
                    : Math.Abs(GetCenterX(first.Bounds) - GetCenterX(second.Bounds));

            if (axisOffset <= options.CollinearAxisTolerancePixels)
            {
                (double contactX, double contactY) =
                    CalculateClosestContactPoint(
                        first.Bounds,
                        second.Bounds);

                double axisScore =
                    Clamp01(
                        1D -
                        (axisOffset /
                         Math.Max(1D, options.CollinearAxisTolerancePixels)));

                return new ConnectionEvaluation(
                    true,
                    SchematicElectricalEdgeKind.BoundsIntersection,
                    Clamp01(
                        options.IntersectionBaseConfidence +
                        (axisScore * options.CollinearAxisWeight) +
                        CalculateRoleCompatibilityBonus(first, second)),
                    0D,
                    contactX,
                    contactY);
            }
        }

        /*
         * En un cruce ortogonal sólo existe conexión cuando el extremo de al
         * menos uno de los trazos termina en el punto de cruce. Un cruce por
         * el interior de ambos segmentos queda desconectado hasta que aparezca
         * un nodo Junction explícito.
         */
        if (firstOrientation != secondOrientation &&
            firstOrientation is not SegmentOrientation.Compact &&
            secondOrientation is not SegmentOrientation.Compact)
        {
            double intersectionX =
                firstOrientation == SegmentOrientation.Vertical
                    ? GetCenterX(first.Bounds)
                    : GetCenterX(second.Bounds);

            double intersectionY =
                firstOrientation == SegmentOrientation.Horizontal
                    ? GetCenterY(first.Bounds)
                    : GetCenterY(second.Bounds);

            bool firstEndsAtIntersection =
                HasEndpointNearPoint(
                    first.Bounds,
                    intersectionX,
                    intersectionY,
                    options.EndpointToSegmentTolerancePixels);

            bool secondEndsAtIntersection =
                HasEndpointNearPoint(
                    second.Bounds,
                    intersectionX,
                    intersectionY,
                    options.EndpointToSegmentTolerancePixels);

            if (firstEndsAtIntersection ||
                secondEndsAtIntersection)
            {
                double confidence =
                    Clamp01(
                        options.EndpointToSegmentBaseConfidence +
                        options.EndpointDistanceWeight +
                        CalculateRoleCompatibilityBonus(first, second));

                return new ConnectionEvaluation(
                    confidence >= options.MinimumEdgeConfidence,
                    SchematicElectricalEdgeKind.EndpointContact,
                    confidence,
                    0D,
                    intersectionX,
                    intersectionY);
            }
        }

        return ConnectionEvaluation.NotConnected;
    }

    private static bool HasEndpointNearPoint(
        BoardGeometryBounds bounds,
        double x,
        double y,
        double tolerancePixels)
    {
        return GetEndpointCandidates(bounds)
            .Any(endpoint =>
                Distance(
                    endpoint.X,
                    endpoint.Y,
                    x,
                    y) <= tolerancePixels);
    }

    /// <summary>
    /// Evalúa la conexión de una unión compacta con otra geometría.
    /// </summary>
    private static ConnectionEvaluation EvaluateJunctionConnection(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        SchematicElectricalNode? junction =
            first.Kind ==
            SchematicElectricalNodeKind.Junction
                ? first
                : second.Kind ==
                  SchematicElectricalNodeKind.Junction
                    ? second
                    : null;

        if (junction is null)
        {
            return ConnectionEvaluation.NotConnected;
        }

        SchematicElectricalNode other =
            junction.Id == first.Id
                ? second
                : first;

        if (!other.IsWireLike &&
            other.Kind is not
                SchematicElectricalNodeKind.Wire and not
                SchematicElectricalNodeKind.Pin and not
                SchematicElectricalNodeKind.Terminal)
        {
            return ConnectionEvaluation.NotConnected;
        }

        double distance =
            DistancePointToBounds(
                junction.CenterX,
                junction.CenterY,
                other.Bounds);

        if (distance >
            options.JunctionConnectionTolerancePixels)
        {
            return ConnectionEvaluation.NotConnected;
        }

        double distanceScore =
            Clamp01(
                1D -
                (distance /
                 Math.Max(
                     1D,
                     options.JunctionConnectionTolerancePixels)));

        return new ConnectionEvaluation(
            true,
            SchematicElectricalEdgeKind.EndpointContact,
            Clamp01(
                options.JunctionBaseConfidence +
                (distanceScore *
                 options.JunctionDistanceWeight)),
            distance,
            junction.CenterX,
            junction.CenterY);
    }

    /// <summary>
    /// Evalúa la relación entre un cuerpo de símbolo y uno de sus pines.
    /// </summary>
    private static ConnectionEvaluation EvaluateBodyPinConnection(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        SchematicElectricalNode? body =
            first.Kind ==
            SchematicElectricalNodeKind.SymbolBody
                ? first
                : second.Kind ==
                  SchematicElectricalNodeKind.SymbolBody
                    ? second
                    : null;

        if (body is null)
        {
            return ConnectionEvaluation.NotConnected;
        }

        SchematicElectricalNode pin =
            body.Id == first.Id
                ? second
                : first;

        if (pin.Kind is not
            (SchematicElectricalNodeKind.Pin or
             SchematicElectricalNodeKind.Terminal or
             SchematicElectricalNodeKind.Wire))
        {
            return ConnectionEvaluation.NotConnected;
        }

        double distance =
            DistanceBetweenBounds(
                body.Bounds,
                pin.Bounds);

        if (distance >
            options.MaximumBodyPinGapPixels)
        {
            return ConnectionEvaluation.NotConnected;
        }

        double alignment =
            CalculateBestAxisAlignment(
                body.Bounds,
                pin.Bounds);

        if (alignment <
            options.MinimumBodyPinAlignment)
        {
            return ConnectionEvaluation.NotConnected;
        }

        (double contactX, double contactY) =
            CalculateClosestContactPoint(
                body.Bounds,
                pin.Bounds);

        double distanceScore =
            Clamp01(
                1D -
                (distance /
                 Math.Max(
                     1D,
                     options.MaximumBodyPinGapPixels)));

        double confidence =
            Clamp01(
                options.BodyPinBaseConfidence +
                (distanceScore *
                 options.BodyPinDistanceWeight) +
                (alignment *
                 options.AxisOverlapWeight));

        return new ConnectionEvaluation(
            confidence >=
                options.MinimumEdgeConfidence,
            SchematicElectricalEdgeKind.EndpointContact,
            confidence,
            distance,
            contactX,
            contactY);
    }

    /// <summary>
    /// Detecta cuando el extremo de un segmento toca el interior de otro.
    /// </summary>
    private static ConnectionEvaluation EvaluateEndpointToSegmentConnection(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        if (!first.IsWireLike ||
            !second.IsWireLike)
        {
            return ConnectionEvaluation.NotConnected;
        }

        EndpointProjection firstToSecond =
            FindBestEndpointProjection(
                first.Bounds,
                second.Bounds);

        EndpointProjection secondToFirst =
            FindBestEndpointProjection(
                second.Bounds,
                first.Bounds);

        EndpointProjection best =
            firstToSecond.DistancePixels <=
            secondToFirst.DistancePixels
                ? firstToSecond
                : secondToFirst;

        if (best.DistancePixels >
            options.EndpointToSegmentTolerancePixels)
        {
            return ConnectionEvaluation.NotConnected;
        }

        double distanceScore =
            Clamp01(
                1D -
                (best.DistancePixels /
                 Math.Max(
                     1D,
                     options.EndpointToSegmentTolerancePixels)));

        double confidence =
            Clamp01(
                options.EndpointToSegmentBaseConfidence +
                (distanceScore *
                 options.EndpointDistanceWeight) +
                CalculateRoleCompatibilityBonus(
                    first,
                    second));

        return new ConnectionEvaluation(
            confidence >=
                options.MinimumEdgeConfidence,
            SchematicElectricalEdgeKind.EndpointContact,
            confidence,
            best.DistancePixels,
            best.ContactX,
            best.ContactY);
    }

    /// <summary>
    /// Detecta contacto directo entre los extremos de dos segmentos.
    /// </summary>
    private static ConnectionEvaluation EvaluateEndpointContact(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        if (!first.IsWireLike &&
            !second.IsWireLike)
        {
            return ConnectionEvaluation.NotConnected;
        }

        EndpointPair pair =
            FindClosestEndpointPair(
                first.Bounds,
                second.Bounds);

        if (pair.DistancePixels >
            options.EndpointTolerancePixels)
        {
            return ConnectionEvaluation.NotConnected;
        }

        double distanceScore =
            Clamp01(
                1D -
                (pair.DistancePixels /
                 Math.Max(
                     1D,
                     options.EndpointTolerancePixels)));

        double confidence =
            Clamp01(
                options.EndpointBaseConfidence +
                (distanceScore *
                 options.EndpointDistanceWeight) +
                CalculateRoleCompatibilityBonus(
                    first,
                    second));

        return new ConnectionEvaluation(
            confidence >=
                options.MinimumEdgeConfidence,
            SchematicElectricalEdgeKind.EndpointContact,
            confidence,
            pair.DistancePixels,
            (pair.FirstX + pair.SecondX) / 2D,
            (pair.FirstY + pair.SecondY) / 2D);
    }

    /// <summary>
    /// Detecta dos segmentos de la misma orientación y eje aproximado.
    /// </summary>
    private static ConnectionEvaluation EvaluateCollinearConnection(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        if (!first.IsWireLike ||
            !second.IsWireLike)
        {
            return ConnectionEvaluation.NotConnected;
        }

        SegmentOrientation firstOrientation =
            ResolveOrientation(
                first.Bounds);

        SegmentOrientation secondOrientation =
            ResolveOrientation(
                second.Bounds);

        if (firstOrientation ==
                SegmentOrientation.Compact ||
            secondOrientation ==
                SegmentOrientation.Compact ||
            firstOrientation !=
                secondOrientation)
        {
            return ConnectionEvaluation.NotConnected;
        }

        double axisOffset;
        double longitudinalGap;

        if (firstOrientation ==
            SegmentOrientation.Horizontal)
        {
            axisOffset =
                Math.Abs(
                    GetCenterY(first.Bounds) -
                    GetCenterY(second.Bounds));

            longitudinalGap =
                IntervalGap(
                    first.Bounds.Left,
                    first.Bounds.Right,
                    second.Bounds.Left,
                    second.Bounds.Right);
        }
        else
        {
            axisOffset =
                Math.Abs(
                    GetCenterX(first.Bounds) -
                    GetCenterX(second.Bounds));

            longitudinalGap =
                IntervalGap(
                    first.Bounds.Top,
                    first.Bounds.Bottom,
                    second.Bounds.Top,
                    second.Bounds.Bottom);
        }

        if (axisOffset >
                options.CollinearAxisTolerancePixels ||
            longitudinalGap >
                options.MaximumCollinearGapPixels)
        {
            return ConnectionEvaluation.NotConnected;
        }

        double axisScore =
            Clamp01(
                1D -
                (axisOffset /
                 Math.Max(
                     1D,
                     options.CollinearAxisTolerancePixels)));

        double gapScore =
            Clamp01(
                1D -
                (longitudinalGap /
                 Math.Max(
                     1D,
                     options.MaximumCollinearGapPixels)));

        (double contactX, double contactY) =
            CalculateClosestContactPoint(
                first.Bounds,
                second.Bounds);

        double confidence =
            Clamp01(
                options.CollinearBaseConfidence +
                (axisScore *
                 options.CollinearAxisWeight) +
                (gapScore *
                 options.GapDistanceWeight) +
                CalculateRoleCompatibilityBonus(
                    first,
                    second));

        return new ConnectionEvaluation(
            confidence >=
                options.MinimumEdgeConfidence,
            SchematicElectricalEdgeKind.CollinearGap,
            confidence,
            longitudinalGap,
            contactX,
            contactY);
    }

    /// <summary>
    /// Evita crear aristas de proximidad entre cuerpos independientes.
    /// </summary>
    private static bool IsProximityCompatible(
        SchematicElectricalNode first,
        SchematicElectricalNode second)
    {
        if (first.Kind == SchematicElectricalNodeKind.Hole ||
            second.Kind == SchematicElectricalNodeKind.Hole)
        {
            return false;
        }

        if (first.Kind == SchematicElectricalNodeKind.SymbolBody &&
            second.Kind == SchematicElectricalNodeKind.SymbolBody)
        {
            return false;
        }

        /*
         * Wire-Wire no se conecta nunca por proximidad genérica. Esas parejas
         * deben demostrar contacto de extremos, T-junction o continuidad
         * colineal. Esto evita que líneas paralelas y redes vecinas terminen
         * formando un único subgrafo.
         */
        if (first.IsWireLike && second.IsWireLike)
        {
            return false;
        }

        if (first.Kind == SchematicElectricalNodeKind.Junction ||
            second.Kind == SchematicElectricalNodeKind.Junction)
        {
            return first.IsWireLike || second.IsWireLike;
        }

        /*
         * La proximidad residual se limita a cuerpo-pin/terminal. Las reglas
         * especializadas se ejecutan antes, por lo que este bloque sólo cubre
         * pequeñas discontinuidades de rasterización.
         */
        bool firstBodySecondTerminal =
            first.Kind == SchematicElectricalNodeKind.SymbolBody &&
            second.Kind is
                SchematicElectricalNodeKind.Pin or
                SchematicElectricalNodeKind.Terminal;

        bool secondBodyFirstTerminal =
            second.Kind == SchematicElectricalNodeKind.SymbolBody &&
            first.Kind is
                SchematicElectricalNodeKind.Pin or
                SchematicElectricalNodeKind.Terminal;

        return firstBodySecondTerminal || secondBodyFirstTerminal;
    }

    /// <summary>
    /// Bonificación semántica de una pareja de nodos.
    /// </summary>
    private static double CalculateRoleCompatibilityBonus(
        SchematicElectricalNode first,
        SchematicElectricalNode second)
    {
        if (first.IsWireLike &&
            second.IsWireLike)
        {
            return 0.08D;
        }

        if ((first.IsWireLike &&
             second.IsSymbolLike) ||
            (second.IsWireLike &&
             first.IsSymbolLike))
        {
            return 0.07D;
        }

        if (first.Kind ==
                SchematicElectricalNodeKind.Junction ||
            second.Kind ==
                SchematicElectricalNodeKind.Junction)
        {
            return 0.09D;
        }

        if ((first.Kind ==
                 SchematicElectricalNodeKind.Pin &&
             second.Kind ==
                 SchematicElectricalNodeKind.SymbolBody) ||
            (second.Kind ==
                 SchematicElectricalNodeKind.Pin &&
             first.Kind ==
                 SchematicElectricalNodeKind.SymbolBody))
        {
            return 0.10D;
        }

        return 0D;
    }

    private static EndpointProjection FindBestEndpointProjection(
        BoardGeometryBounds source,
        BoardGeometryBounds target)
    {
        (double X, double Y)[] endpoints =
            GetEndpointCandidates(source);

        EndpointProjection best =
            new(
                double.MaxValue,
                0D,
                0D);

        foreach ((double x, double y) in endpoints)
        {
            double projectedX =
                Clamp(
                    x,
                    target.Left,
                    target.Right);

            double projectedY =
                Clamp(
                    y,
                    target.Top,
                    target.Bottom);

            double distance =
                Distance(
                    x,
                    y,
                    projectedX,
                    projectedY);

            if (distance <
                best.DistancePixels)
            {
                best =
                    new EndpointProjection(
                        distance,
                        projectedX,
                        projectedY);
            }
        }

        return best;
    }

    private static EndpointPair FindClosestEndpointPair(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        (double X, double Y)[] firstEndpoints =
            GetEndpointCandidates(first);

        (double X, double Y)[] secondEndpoints =
            GetEndpointCandidates(second);

        EndpointPair best =
            new(
                double.MaxValue,
                0D,
                0D,
                0D,
                0D);

        foreach ((double firstX, double firstY) in firstEndpoints)
        {
            foreach ((double secondX, double secondY) in secondEndpoints)
            {
                double distance =
                    Distance(
                        firstX,
                        firstY,
                        secondX,
                        secondY);

                if (distance <
                    best.DistancePixels)
                {
                    best =
                        new EndpointPair(
                            distance,
                            firstX,
                            firstY,
                            secondX,
                            secondY);
                }
            }
        }

        return best;
    }

    private static (double X, double Y)[] GetEndpointCandidates(
        BoardGeometryBounds bounds)
    {
        double centerX =
            GetCenterX(bounds);

        double centerY =
            GetCenterY(bounds);

        SegmentOrientation orientation =
            ResolveOrientation(bounds);

        return orientation switch
        {
            SegmentOrientation.Horizontal =>
            [
                (bounds.Left, centerY),
                (bounds.Right, centerY)
            ],

            SegmentOrientation.Vertical =>
            [
                (centerX, bounds.Top),
                (centerX, bounds.Bottom)
            ],

            _ =>
            [
                (bounds.Left, centerY),
                (bounds.Right, centerY),
                (centerX, bounds.Top),
                (centerX, bounds.Bottom)
            ]
        };
    }

    private static SegmentOrientation ResolveOrientation(
        BoardGeometryBounds bounds)
    {
        if (bounds.Width >=
            bounds.Height * 1.50D)
        {
            return SegmentOrientation.Horizontal;
        }

        if (bounds.Height >=
            bounds.Width * 1.50D)
        {
            return SegmentOrientation.Vertical;
        }

        return SegmentOrientation.Compact;
    }

    private static bool Intersects(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        return first.Left < second.Right &&
               first.Right > second.Left &&
               first.Top < second.Bottom &&
               first.Bottom > second.Top;
    }

    private static double CalculateBestAxisAlignment(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        return Math.Max(
            AxisOverlapRatio(
                first.Left,
                first.Right,
                second.Left,
                second.Right),
            AxisOverlapRatio(
                first.Top,
                first.Bottom,
                second.Top,
                second.Bottom));
    }

    private static double AxisOverlapRatio(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd)
    {
        double overlap =
            Math.Min(
                firstEnd,
                secondEnd) -
            Math.Max(
                firstStart,
                secondStart);

        if (overlap <= 0D)
        {
            return 0D;
        }

        double firstLength =
            Math.Max(
                1D,
                firstEnd -
                firstStart);

        double secondLength =
            Math.Max(
                1D,
                secondEnd -
                secondStart);

        return Clamp01(
            overlap /
            Math.Min(
                firstLength,
                secondLength));
    }

    private static double DistanceBetweenBounds(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        double horizontalDistance =
            first.Right < second.Left
                ? second.Left - first.Right
                : second.Right < first.Left
                    ? first.Left - second.Right
                    : 0D;

        double verticalDistance =
            first.Bottom < second.Top
                ? second.Top - first.Bottom
                : second.Bottom < first.Top
                    ? first.Top - second.Bottom
                    : 0D;

        return Math.Sqrt(
            (horizontalDistance *
             horizontalDistance) +
            (verticalDistance *
             verticalDistance));
    }

    private static double DistancePointToBounds(
        double x,
        double y,
        BoardGeometryBounds bounds)
    {
        double nearestX =
            Clamp(
                x,
                bounds.Left,
                bounds.Right);

        double nearestY =
            Clamp(
                y,
                bounds.Top,
                bounds.Bottom);

        return Distance(
            x,
            y,
            nearestX,
            nearestY);
    }

    private static (double X, double Y) CalculateClosestContactPoint(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        double firstX =
            Clamp(
                GetCenterX(second),
                first.Left,
                first.Right);

        double firstY =
            Clamp(
                GetCenterY(second),
                first.Top,
                first.Bottom);

        double secondX =
            Clamp(
                GetCenterX(first),
                second.Left,
                second.Right);

        double secondY =
            Clamp(
                GetCenterY(first),
                second.Top,
                second.Bottom);

        return (
            (firstX + secondX) / 2D,
            (firstY + secondY) / 2D);
    }

    private static double IntervalGap(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd)
    {
        if (firstEnd < secondStart)
        {
            return secondStart - firstEnd;
        }

        if (secondEnd < firstStart)
        {
            return firstStart - secondEnd;
        }

        return 0D;
    }

    private static double GetCenterX(
        BoardGeometryBounds bounds)
    {
        return bounds.Left +
               (bounds.Width / 2D);
    }

    private static double GetCenterY(
        BoardGeometryBounds bounds)
    {
        return bounds.Top +
               (bounds.Height / 2D);
    }

    private static double Distance(
        double firstX,
        double firstY,
        double secondX,
        double secondY)
    {
        double deltaX =
            firstX -
            secondX;

        double deltaY =
            firstY -
            secondY;

        return Math.Sqrt(
            (deltaX * deltaX) +
            (deltaY * deltaY));
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum)
    {
        return Math.Max(
            minimum,
            Math.Min(
                maximum,
                value));
    }

    private static double Clamp01(
        double value)
    {
        return Clamp(
            value,
            0D,
            1D);
    }

    private enum SegmentOrientation
    {
        Compact = 0,
        Horizontal = 1,
        Vertical = 2
    }

    private readonly record struct EndpointProjection(
        double DistancePixels,
        double ContactX,
        double ContactY);

    private readonly record struct EndpointPair(
        double DistancePixels,
        double FirstX,
        double FirstY,
        double SecondX,
        double SecondY);

    private readonly record struct ConnectionEvaluation(
        bool IsConnected,
        SchematicElectricalEdgeKind Kind,
        double Confidence,
        double DistancePixels,
        double ContactX,
        double ContactY)
    {
        public static ConnectionEvaluation NotConnected { get; } =
            new(
                false,
                SchematicElectricalEdgeKind.Unknown,
                0D,
                0D,
                0D,
                0D);
    }
}

/// <summary>
/// Configuración del constructor enriquecido del grafo eléctrico.
/// </summary>
public sealed record SchematicElectricalGraphBuilderOptions
{
    public static SchematicElectricalGraphBuilderOptions Default { get; } =
        new();

    public double MinimumComponentConfidence { get; init; } = 0.12D;
    public int MaximumNeighborsPerNode { get; init; } = 96;

    public double MinimumSearchRadiusPixels { get; init; } = 12D;
    public double MaximumSearchRadiusPixels { get; init; } = 96D;
    public double SearchRadiusScaleFactor { get; init; } = 1.65D;

    public double TouchTolerancePixels { get; init; } = 2D;
    public double EndpointTolerancePixels { get; init; } = 7D;
    public double EndpointToSegmentTolerancePixels { get; init; } = 6D;
    public double JunctionConnectionTolerancePixels { get; init; } = 8D;
    public double CollinearAxisTolerancePixels { get; init; } = 5D;
    public double MaximumCollinearGapPixels { get; init; } = 15D;
    public double MaximumConnectionGapPixels { get; init; } = 20D;
    public double MaximumBodyPinGapPixels { get; init; } = 10D;

    public double MinimumBodyPinAlignment { get; init; } = 0.25D;
    public double MinimumEdgeConfidence { get; init; } = 0.48D;

    public double MinimumWireAspectRatio { get; init; } = 3D;
    public double MaximumWireThicknessPixels { get; init; } = 9D;
    public double MaximumPinLengthPixels { get; init; } = 42D;
    public double MaximumRecoverableTerminalLengthPixels { get; init; } = 58D;
    public double MaximumRecoveredBodyTerminalGapPixels { get; init; } = 12D;
    public double MinimumRecoveredBodyTerminalAlignment { get; init; } = 0.28D;
    public double MinimumTerminalAspectRatio { get; init; } = 2D;
    public double MaximumTerminalLengthPixels { get; init; } = 56D;
    public double MaximumPinThicknessPixels { get; init; } = 11D;
    public double MaximumJunctionAreaPixels { get; init; } = 196D;
    public double MaximumJunctionAspectRatio { get; init; } = 1.90D;

    public double IntersectionBaseConfidence { get; init; } = 0.90D;
    public double TouchBaseConfidence { get; init; } = 0.76D;
    public double JunctionBaseConfidence { get; init; } = 0.80D;
    public double JunctionDistanceWeight { get; init; } = 0.18D;
    public double BodyPinBaseConfidence { get; init; } = 0.58D;
    public double BodyPinDistanceWeight { get; init; } = 0.22D;
    public double RecoveredBodyTerminalBaseConfidence { get; init; } = 0.56D;
    public double RecoveredBodyTerminalDistanceWeight { get; init; } = 0.24D;
    public double EndpointBaseConfidence { get; init; } = 0.54D;
    public double EndpointToSegmentBaseConfidence { get; init; } = 0.60D;
    public double EndpointDistanceWeight { get; init; } = 0.30D;
    public double CollinearBaseConfidence { get; init; } = 0.40D;
    public double CollinearAxisWeight { get; init; } = 0.22D;
    public double ProximityBaseConfidence { get; init; } = 0.20D;
    public double ProximityDistanceWeight { get; init; } = 0.34D;
    public double AxisOverlapWeight { get; init; } = 0.20D;
    public double GapDistanceWeight { get; init; } = 0.28D;

    /// <summary>
    /// Una conexión directa cuerpo-red se conserva únicamente cuando supera
    /// este nivel, aun si existe un pin intermedio.
    /// </summary>
    public double RedundantBodyWireRetentionConfidence { get; init; } = 0.92D;

    /// <summary>
    /// Confianza mínima para conservar una unión compacta que sólo dispone de
    /// una relación lineal.
    /// </summary>
    public double MinimumSingleJunctionEdgeConfidence { get; init; } = 0.82D;

    public void Validate()
    {
        ValidateProbability(
            MinimumComponentConfidence,
            nameof(MinimumComponentConfidence));

        ValidatePositive(
            MaximumNeighborsPerNode,
            nameof(MaximumNeighborsPerNode));

        ValidatePositiveFinite(
            MinimumSearchRadiusPixels,
            nameof(MinimumSearchRadiusPixels));

        ValidatePositiveFinite(
            MaximumSearchRadiusPixels,
            nameof(MaximumSearchRadiusPixels));

        if (MaximumSearchRadiusPixels <
            MinimumSearchRadiusPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumSearchRadiusPixels));
        }

        ValidatePositiveFinite(
            SearchRadiusScaleFactor,
            nameof(SearchRadiusScaleFactor));

        ValidateNonNegativeFinite(
            TouchTolerancePixels,
            nameof(TouchTolerancePixels));

        ValidateNonNegativeFinite(
            EndpointTolerancePixels,
            nameof(EndpointTolerancePixels));

        ValidateNonNegativeFinite(
            EndpointToSegmentTolerancePixels,
            nameof(EndpointToSegmentTolerancePixels));

        ValidateNonNegativeFinite(
            JunctionConnectionTolerancePixels,
            nameof(JunctionConnectionTolerancePixels));

        ValidateNonNegativeFinite(
            CollinearAxisTolerancePixels,
            nameof(CollinearAxisTolerancePixels));

        ValidateNonNegativeFinite(
            MaximumCollinearGapPixels,
            nameof(MaximumCollinearGapPixels));

        ValidateNonNegativeFinite(
            MaximumConnectionGapPixels,
            nameof(MaximumConnectionGapPixels));

        ValidateNonNegativeFinite(
            MaximumBodyPinGapPixels,
            nameof(MaximumBodyPinGapPixels));

        ValidateProbability(
            MinimumBodyPinAlignment,
            nameof(MinimumBodyPinAlignment));

        ValidateProbability(
            MinimumEdgeConfidence,
            nameof(MinimumEdgeConfidence));

        ValidatePositiveFinite(
            MinimumWireAspectRatio,
            nameof(MinimumWireAspectRatio));

        ValidatePositiveFinite(
            MaximumWireThicknessPixels,
            nameof(MaximumWireThicknessPixels));

        ValidatePositiveFinite(
            MaximumPinLengthPixels,
            nameof(MaximumPinLengthPixels));

        ValidatePositiveFinite(
            MaximumRecoverableTerminalLengthPixels,
            nameof(MaximumRecoverableTerminalLengthPixels));

        ValidateNonNegativeFinite(
            MaximumRecoveredBodyTerminalGapPixels,
            nameof(MaximumRecoveredBodyTerminalGapPixels));

        ValidateProbability(
            MinimumRecoveredBodyTerminalAlignment,
            nameof(MinimumRecoveredBodyTerminalAlignment));

        ValidatePositiveFinite(
            MinimumTerminalAspectRatio,
            nameof(MinimumTerminalAspectRatio));

        ValidatePositiveFinite(
            MaximumTerminalLengthPixels,
            nameof(MaximumTerminalLengthPixels));

        ValidatePositiveFinite(
            MaximumPinThicknessPixels,
            nameof(MaximumPinThicknessPixels));

        ValidatePositiveFinite(
            MaximumJunctionAreaPixels,
            nameof(MaximumJunctionAreaPixels));

        ValidatePositiveFinite(
            MaximumJunctionAspectRatio,
            nameof(MaximumJunctionAspectRatio));

        ValidateProbability(
            IntersectionBaseConfidence,
            nameof(IntersectionBaseConfidence));

        ValidateProbability(
            TouchBaseConfidence,
            nameof(TouchBaseConfidence));

        ValidateProbability(
            JunctionBaseConfidence,
            nameof(JunctionBaseConfidence));

        ValidateProbability(
            JunctionDistanceWeight,
            nameof(JunctionDistanceWeight));

        ValidateProbability(
            BodyPinBaseConfidence,
            nameof(BodyPinBaseConfidence));

        ValidateProbability(
            BodyPinDistanceWeight,
            nameof(BodyPinDistanceWeight));

        ValidateProbability(
            RecoveredBodyTerminalBaseConfidence,
            nameof(RecoveredBodyTerminalBaseConfidence));

        ValidateProbability(
            RecoveredBodyTerminalDistanceWeight,
            nameof(RecoveredBodyTerminalDistanceWeight));

        ValidateProbability(
            EndpointBaseConfidence,
            nameof(EndpointBaseConfidence));

        ValidateProbability(
            EndpointToSegmentBaseConfidence,
            nameof(EndpointToSegmentBaseConfidence));

        ValidateProbability(
            EndpointDistanceWeight,
            nameof(EndpointDistanceWeight));

        ValidateProbability(
            CollinearBaseConfidence,
            nameof(CollinearBaseConfidence));

        ValidateProbability(
            CollinearAxisWeight,
            nameof(CollinearAxisWeight));

        ValidateProbability(
            ProximityBaseConfidence,
            nameof(ProximityBaseConfidence));

        ValidateProbability(
            ProximityDistanceWeight,
            nameof(ProximityDistanceWeight));

        ValidateProbability(
            AxisOverlapWeight,
            nameof(AxisOverlapWeight));

        ValidateProbability(
            GapDistanceWeight,
            nameof(GapDistanceWeight));

        ValidateProbability(
            RedundantBodyWireRetentionConfidence,
            nameof(RedundantBodyWireRetentionConfidence));

        ValidateProbability(
            MinimumSingleJunctionEdgeConfidence,
            nameof(MinimumSingleJunctionEdgeConfidence));
    }

    private static void ValidatePositive(
        int value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }

    private static void ValidatePositiveFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }

    private static void ValidateNonNegativeFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }

    private static void ValidateProbability(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0D ||
            value > 1D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }
}