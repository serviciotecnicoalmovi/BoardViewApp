using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Reconstruye símbolos eléctricos completos mediante exploración dinámica del
/// índice geométrico.
/// </summary>
/// <remarks>
/// La versión 4 construye primero un <see cref="SchematicElectricalGraph"/> y
/// recorre exclusivamente sus aristas. El ensamblador ya no descubre vecinos
/// mediante consultas de proximidad durante la expansión del símbolo.
/// /// </remarks>
public sealed class SchematicSymbolAssembler
{
    public static SchematicSymbolAssemblerOptions DefaultOptions { get; } =
        new();

    public SchematicSymbolAssemblyResult Assemble(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates)
    {
        return Assemble(
            geometryIndex,
            candidates,
            DefaultOptions,
            CancellationToken.None);
    }

    /// <summary>
    /// Reconstruye símbolos desde anclajes semánticos ya resueltos.
    /// El BFS comienza exactamente en <see cref="SchematicReferenceAnchor.SeedNode"/>.
    /// </summary>
    public SchematicSymbolAssemblyResult Assemble(
        BoardGeometryIndex geometryIndex,
        SchematicElectricalGraph electricalGraph,
        SchematicReferenceAnchorResult anchors,
        SchematicSymbolAssemblerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geometryIndex);
        ArgumentNullException.ThrowIfNull(electricalGraph);
        ArgumentNullException.ThrowIfNull(anchors);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        if (geometryIndex.Count == 0 ||
            electricalGraph.NodeCount == 0 ||
            anchors.Count == 0)
        {
            return SchematicSymbolAssemblyResult.Empty;
        }

        SchematicReferenceAnchor[] anchorArray =
            anchors.Anchors
                .OrderBy(anchor => anchor.Candidate.Id)
                .ToArray();

        BoardReferenceCandidate[] references =
            anchorArray
                .Select(anchor => anchor.Candidate)
                .ToArray();

        var symbols =
            new List<SchematicSymbol>();

        foreach (SchematicReferenceAnchor anchor in anchorArray)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SchematicSymbol? symbol =
                Reconstruct(
                    geometryIndex,
                    electricalGraph,
                    anchor,
                    references,
                    options,
                    cancellationToken);

            if (symbol is not null)
            {
                symbols.Add(symbol);
            }
        }

        return new SchematicSymbolAssemblyResult(symbols);
    }

    public SchematicSymbolAssemblyResult Assemble(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates,
        SchematicSymbolAssemblerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geometryIndex);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        BoardReferenceCandidate[] references =
            candidates
                .Where(candidate => candidate.IsReferenceLike)
                .OrderBy(candidate => candidate.Id)
                .ToArray();

        if (geometryIndex.Count == 0 ||
            references.Length == 0)
        {
            return SchematicSymbolAssemblyResult.Empty;
        }

        var graphBuilder =
            new SchematicElectricalGraphBuilder();

        SchematicElectricalGraph electricalGraph =
            graphBuilder.Build(
                geometryIndex,
                options.ElectricalGraphBuilderOptions,
                cancellationToken);

        var symbols =
            new List<SchematicSymbol>();

        foreach (BoardReferenceCandidate reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SchematicSymbol? symbol =
                ReconstructLegacy(
                    geometryIndex,
                    electricalGraph,
                    reference,
                    references,
                    options,
                    cancellationToken);

            if (symbol is not null)
            {
                symbols.Add(symbol);
            }
        }

        return new SchematicSymbolAssemblyResult(symbols);
    }

    private static SchematicSymbol? Reconstruct(
        BoardGeometryIndex geometryIndex,
        SchematicElectricalGraph electricalGraph,
        SchematicReferenceAnchor anchor,
        IReadOnlyList<BoardReferenceCandidate> allReferences,
        SchematicSymbolAssemblerOptions options,
        CancellationToken cancellationToken)
    {
        BoardReferenceCandidate reference =
            anchor.Candidate;

        SchematicReferenceFamily family =
            ResolveFamily(reference.NormalizedReference);

        AssemblyProfile profile =
            CreateProfile(
                family,
                reference,
                options);

        BoardGeometryIndexedComponent seed =
            anchor.SeedNode.Component;

        BoardReferenceCandidate[] neighboringReferences =
            allReferences
                .Where(other =>
                    other.Id != reference.Id &&
                    other.PageIndex == reference.PageIndex)
                .Where(other =>
                    DistanceBetweenBounds(
                        reference.Bounds,
                        other.Bounds) <=
                    profile.ReferenceOwnershipRadius)
                .ToArray();

        IReadOnlyList<BoardGeometryIndexedComponent> components =
            ExploreGraph(
                electricalGraph,
                reference,
                seed,
                neighboringReferences,
                family,
                profile,
                options,
                cancellationToken);

        if (components.Count == 0)
        {
            return null;
        }

        BoardGeometryBounds rawBounds =
            CombineBounds(components);

        BoardGeometryBounds logicalBounds =
            ExpandAndClamp(
                rawBounds,
                Math.Max(
                    profile.MinimumPadding,
                    reference.Bounds.Width *
                    profile.HorizontalPaddingFactor),
                Math.Max(
                    profile.MinimumPadding,
                    reference.Bounds.Height *
                    profile.TopPaddingFactor),
                Math.Max(
                    profile.MinimumPadding,
                    reference.Bounds.Height *
                    profile.BottomPaddingFactor),
                geometryIndex.PageWidth,
                geometryIndex.PageHeight);

        double confidence =
            Clamp01(
                (CalculateConfidence(
                    reference,
                    components,
                    logicalBounds,
                    family,
                    profile) * 0.72D) +
                (anchor.Confidence * 0.28D));

        if (confidence < options.MinimumAssemblyConfidence)
        {
            return null;
        }

        return new SchematicSymbol(
            reference.NormalizedReference,
            reference.PageIndex,
            logicalBounds,
            components,
            confidence);
    }

    private static SchematicSymbol? ReconstructLegacy(
        BoardGeometryIndex geometryIndex,
        SchematicElectricalGraph electricalGraph,
        BoardReferenceCandidate reference,
        IReadOnlyList<BoardReferenceCandidate> allReferences,
        SchematicSymbolAssemblerOptions options,
        CancellationToken cancellationToken)
    {
        SchematicReferenceFamily family =
            ResolveFamily(reference.NormalizedReference);

        AssemblyProfile profile =
            CreateProfile(
                family,
                reference,
                options);

        BoardGeometryIndexQueryOptions queryOptions =
            CreateQueryOptions(options);

        BoardGeometryIndexedComponent? seed =
            FindSeed(
                geometryIndex,
                reference,
                family,
                profile,
                options,
                queryOptions);

        if (seed is null)
        {
            return null;
        }

        BoardReferenceCandidate[] neighboringReferences =
            allReferences
                .Where(other =>
                    other.Id != reference.Id &&
                    other.PageIndex == reference.PageIndex)
                .Where(other =>
                    DistanceBetweenBounds(
                        reference.Bounds,
                        other.Bounds) <=
                    profile.ReferenceOwnershipRadius)
                .ToArray();

        IReadOnlyList<BoardGeometryIndexedComponent> components =
            ExploreGraph(
                electricalGraph,
                reference,
                seed,
                neighboringReferences,
                family,
                profile,
                options,
                cancellationToken);

        if (components.Count == 0)
        {
            return null;
        }

        BoardGeometryBounds rawBounds =
            CombineBounds(components);

        BoardGeometryBounds logicalBounds =
            ExpandAndClamp(
                rawBounds,
                Math.Max(
                    profile.MinimumPadding,
                    reference.Bounds.Width *
                    profile.HorizontalPaddingFactor),
                Math.Max(
                    profile.MinimumPadding,
                    reference.Bounds.Height *
                    profile.TopPaddingFactor),
                Math.Max(
                    profile.MinimumPadding,
                    reference.Bounds.Height *
                    profile.BottomPaddingFactor),
                geometryIndex.PageWidth,
                geometryIndex.PageHeight);

        double confidence =
            CalculateConfidence(
                reference,
                components,
                logicalBounds,
                family,
                profile);

        if (confidence <
            options.MinimumAssemblyConfidence)
        {
            return null;
        }

        return new SchematicSymbol(
            reference.NormalizedReference,
            reference.PageIndex,
            logicalBounds,
            components,
            confidence);
    }

    private static BoardGeometryIndexQueryOptions CreateQueryOptions(
        SchematicSymbolAssemblerOptions options)
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

    private static BoardGeometryIndexedComponent? FindSeed(
        BoardGeometryIndex geometryIndex,
        BoardReferenceCandidate reference,
        SchematicReferenceFamily family,
        AssemblyProfile profile,
        SchematicSymbolAssemblerOptions options,
        BoardGeometryIndexQueryOptions queryOptions)
    {
        IReadOnlyList<BoardGeometryIndexedComponent> candidates =
            geometryIndex.QueryNearest(
                reference.CenterX,
                reference.CenterY,
                profile.SeedSearchRadius,
                options.MaximumSeedCandidates,
                queryOptions);

        return candidates
            .Select(component =>
                new ScoredComponent(
                    component,
                    ScoreSeed(
                        reference,
                        component,
                        family,
                        profile,
                        options)))
            .Where(item =>
                item.Score >=
                options.MinimumSeedScore)
            .OrderByDescending(item => item.Score)
            .ThenBy(item =>
                DistanceBetweenBounds(
                    reference.Bounds,
                    item.Component.Bounds))
            .ThenBy(item => item.Component.Id)
            .Select(item => item.Component)
            .FirstOrDefault();
    }

    /// <summary>
    /// Recorre el subgrafo eléctrico alcanzable desde la semilla.
    /// </summary>
    /// <remarks>
    /// La expansión utiliza únicamente aristas previamente validadas por
    /// <see cref="SchematicElectricalGraphBuilder"/>. No se ejecutan consultas
    /// espaciales ni se crean conexiones nuevas dentro del ensamblador.
    /// </remarks>
    private static IReadOnlyList<BoardGeometryIndexedComponent> ExploreGraph(
        SchematicElectricalGraph electricalGraph,
        BoardReferenceCandidate owner,
        BoardGeometryIndexedComponent seed,
        IReadOnlyList<BoardReferenceCandidate> neighboringReferences,
        SchematicReferenceFamily family,
        AssemblyProfile profile,
        SchematicSymbolAssemblerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            electricalGraph);

        if (!electricalGraph.TryGetNode(
                seed.Id,
                out SchematicElectricalNode? seedNode) ||
            seedNode is null)
        {
            return Array.Empty<BoardGeometryIndexedComponent>();
        }

        var accepted =
            new Dictionary<int, SchematicElectricalNode>
            {
                [seedNode.Id] = seedNode
            };

        var visited =
            new HashSet<int>
            {
                seedNode.Id
            };

        var queue =
            new Queue<SchematicElectricalNode>();

        queue.Enqueue(
            seedNode);

        BoardGeometryBounds clusterBounds =
            seedNode.Bounds;

        int expandedNodes = 0;

        while (queue.Count > 0 &&
               accepted.Count <
               options.MaximumComponentsPerSymbol &&
               expandedNodes <
               options.MaximumExpandedNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SchematicElectricalNode current =
                queue.Dequeue();

            expandedNodes++;

            IReadOnlyList<SchematicElectricalEdge> edges =
                electricalGraph.GetEdges(
                    current.Id);

            foreach (SchematicElectricalEdge edge
                     in edges)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (edge.Confidence <
                    options.MinimumGraphEdgeConfidence)
                {
                    continue;
                }

                int neighborId =
                    edge.GetOtherNodeId(
                        current.Id);

                if (!visited.Add(
                        neighborId))
                {
                    continue;
                }

                if (!electricalGraph.TryGetNode(
                        neighborId,
                        out SchematicElectricalNode? neighbor) ||
                    neighbor is null)
                {
                    continue;
                }

                if (BelongsToNeighbor(
                        owner,
                        neighbor.Component,
                        neighboringReferences,
                        profile))
                {
                    continue;
                }

                if (!IsGraphNodeAccepted(
                        owner,
                        current,
                        neighbor,
                        edge,
                        family,
                        profile,
                        options))
                {
                    continue;
                }

                BoardGeometryBounds proposedBounds =
                    Union(
                        clusterBounds,
                        neighbor.Bounds);

                if (!IsExtentValid(
                        owner,
                        proposedBounds,
                        family,
                        profile))
                {
                    continue;
                }

                accepted[neighbor.Id] =
                    neighbor;

                clusterBounds =
                    proposedBounds;

                queue.Enqueue(
                    neighbor);

                if (accepted.Count >=
                    options.MaximumComponentsPerSymbol)
                {
                    break;
                }
            }
        }

        return accepted
            .Values
            .Select(
                node =>
                    node.Component)
            .OrderBy(
                component =>
                    component.Id)
            .ToArray();
    }

    /// <summary>
    /// Aplica únicamente restricciones semánticas y de propiedad al recorrido.
    /// La existencia de la conexión ya fue resuelta por el constructor del grafo.
    /// </summary>
    private static bool IsGraphNodeAccepted(
        BoardReferenceCandidate owner,
        SchematicElectricalNode current,
        SchematicElectricalNode neighbor,
        SchematicElectricalEdge edge,
        SchematicReferenceFamily family,
        AssemblyProfile profile,
        SchematicSymbolAssemblerOptions options)
    {
        if (IsTextFragment(
                owner,
                neighbor.Component,
                options))
        {
            return false;
        }

        if (neighbor.Kind is
                SchematicElectricalNodeKind.Unknown or
                SchematicElectricalNodeKind.Hole &&
            edge.Confidence <
                options.MinimumWeakNodeEdgeConfidence)
        {
            return false;
        }

        /*
         * Evita continuar indefinidamente por una red de wires. Un wire puede
         * incorporarse cuando nace de un cuerpo, pin, terminal o junction, o
         * cuando la arista tiene una confianza especialmente alta.
         */
        bool wireToWire =
            current.IsWireLike &&
            neighbor.IsWireLike;

        if (wireToWire &&
            edge.Confidence <
                options.MinimumWireChainEdgeConfidence)
        {
            return false;
        }

        double directionScore =
            DirectionScore(
                owner,
                neighbor.Component,
                family,
                profile);

        if (directionScore <
                options.MinimumGraphDirectionScore &&
            !neighbor.IsWireLike &&
            neighbor.Kind !=
                SchematicElectricalNodeKind.Junction)
        {
            return false;
        }

        return true;
    }

    private static double ScoreSeed(
        BoardReferenceCandidate reference,
        BoardGeometryIndexedComponent component,
        SchematicReferenceFamily family,
        AssemblyProfile profile,
        SchematicSymbolAssemblerOptions options)
    {
        if (IsTextFragment(
                reference,
                component,
                options))
        {
            return 0D;
        }

        double distance =
            DistanceBetweenBounds(
                reference.Bounds,
                component.Bounds);

        double distanceScore =
            Clamp01(
                1D -
                distance /
                Math.Max(
                    1D,
                    profile.SeedSearchRadius));

        return Clamp01(
            (distanceScore * 0.25D) +
            (DirectionScore(
                reference,
                component,
                family,
                profile) * 0.24D) +
            (AlignmentScore(
                reference,
                component,
                family) * 0.15D) +
            (TypeScore(
                family,
                component.Type) * 0.22D) +
            (ScaleScore(
                reference.Bounds,
                component.Bounds,
                family) * 0.14D));
    }

    private static double ScoreConnection(
        BoardReferenceCandidate owner,
        BoardGeometryIndexedComponent current,
        BoardGeometryIndexedComponent neighbor,
        BoardGeometryBounds clusterBounds,
        IReadOnlyList<BoardReferenceCandidate> neighboringReferences,
        SchematicReferenceFamily family,
        AssemblyProfile profile,
        SchematicSymbolAssemblerOptions options)
    {
        if (IsTextFragment(
                owner,
                neighbor,
                options) ||
            BelongsToNeighbor(
                owner,
                neighbor,
                neighboringReferences,
                profile))
        {
            return 0D;
        }

        double allowedGap =
            ResolveAllowedGap(
                current,
                neighbor,
                owner,
                family,
                profile);

        double edgeGap =
            DistanceBetweenBounds(
                current.Bounds,
                neighbor.Bounds);

        double clusterGap =
            DistanceBetweenBounds(
                clusterBounds,
                neighbor.Bounds);

        if (edgeGap > allowedGap &&
            clusterGap > allowedGap)
        {
            return 0D;
        }

        double proximityScore =
            Clamp01(
                1D -
                Math.Min(
                    edgeGap,
                    clusterGap) /
                Math.Max(
                    1D,
                    allowedGap));

        double localContinuity =
            ContinuityScore(
                current.Bounds,
                neighbor.Bounds,
                allowedGap);

        double clusterContinuity =
            ContinuityScore(
                clusterBounds,
                neighbor.Bounds,
                allowedGap);

        return Clamp01(
            (proximityScore * 0.30D) +
            (localContinuity * 0.26D) +
            (clusterContinuity * 0.15D) +
            (TypeScore(
                family,
                neighbor.Type) * 0.13D) +
            (DirectionScore(
                owner,
                neighbor,
                family,
                profile) * 0.10D) +
            (ScaleScore(
                owner.Bounds,
                neighbor.Bounds,
                family) * 0.06D));
    }

    private static double ResolveNodeRadius(
        BoardGeometryIndexedComponent node,
        BoardReferenceCandidate reference,
        SchematicReferenceFamily family,
        AssemblyProfile profile)
    {
        double componentScale =
            Math.Max(
                node.Bounds.Width,
                node.Bounds.Height);

        double referenceScale =
            Math.Max(
                reference.Bounds.Width,
                reference.Bounds.Height);

        double familyMultiplier =
            family switch
            {
                SchematicReferenceFamily.IntegratedCircuit => 1.45D,
                SchematicReferenceFamily.Connector => 1.35D,
                SchematicReferenceFamily.TestPoint => 0.80D,
                _ => 1D
            };

        return Math.Max(
            profile.MinimumNodeSearchRadius,
            Math.Max(
                componentScale *
                profile.ComponentRadiusFactor,
                referenceScale *
                profile.TextRadiusFactor) *
            familyMultiplier);
    }

    private static double ResolveAllowedGap(
        BoardGeometryIndexedComponent first,
        BoardGeometryIndexedComponent second,
        BoardReferenceCandidate reference,
        SchematicReferenceFamily family,
        AssemblyProfile profile)
    {
        double componentScale =
            Math.Max(
                Math.Max(
                    first.Bounds.Width,
                    first.Bounds.Height),
                Math.Max(
                    second.Bounds.Width,
                    second.Bounds.Height));

        double referenceScale =
            Math.Max(
                reference.Bounds.Width,
                reference.Bounds.Height);

        double familyMultiplier =
            family switch
            {
                SchematicReferenceFamily.IntegratedCircuit => 1.35D,
                SchematicReferenceFamily.Connector => 1.25D,
                SchematicReferenceFamily.TestPoint => 0.75D,
                _ => 1D
            };

        return Math.Max(
            profile.MinimumConnectionGap,
            Math.Min(
                profile.MaximumConnectionGap,
                Math.Max(
                    componentScale *
                    profile.ComponentGapFactor,
                    referenceScale *
                    profile.ReferenceGapFactor) *
                familyMultiplier));
    }

    private static bool BelongsToNeighbor(
        BoardReferenceCandidate owner,
        BoardGeometryIndexedComponent component,
        IReadOnlyList<BoardReferenceCandidate> neighboringReferences,
        AssemblyProfile profile)
    {
        double ownerDistance =
            DistanceBetweenBounds(
                owner.Bounds,
                component.Bounds);

        foreach (BoardReferenceCandidate neighbor in neighboringReferences)
        {
            double neighborDistance =
                DistanceBetweenBounds(
                    neighbor.Bounds,
                    component.Bounds);

            if (neighborDistance +
                profile.NeighborOwnershipMargin <
                ownerDistance)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExtentValid(
        BoardReferenceCandidate reference,
        BoardGeometryBounds proposedBounds,
        SchematicReferenceFamily family,
        AssemblyProfile profile)
    {
        double maximumWidth =
            Math.Max(
                profile.MinimumMaximumWidth,
                reference.Bounds.Width *
                profile.MaximumWidthFactor);

        double maximumHeight =
            Math.Max(
                profile.MinimumMaximumHeight,
                reference.Bounds.Height *
                profile.MaximumHeightFactor);

        if (proposedBounds.Width > maximumWidth ||
            proposedBounds.Height > maximumHeight)
        {
            return false;
        }

        double centerX =
            proposedBounds.Left +
            proposedBounds.Width / 2D;

        double centerY =
            proposedBounds.Top +
            proposedBounds.Height / 2D;

        if (Math.Abs(
                centerX -
                reference.CenterX) >
            maximumWidth *
            profile.MaximumCenterOffsetFactor)
        {
            return false;
        }

        if (family !=
                SchematicReferenceFamily.TestPoint &&
            centerY -
            reference.CenterY <
            -reference.Bounds.Height *
            profile.MaximumAboveReferenceFactor)
        {
            return false;
        }

        return true;
    }

    private static bool IsTextFragment(
        BoardReferenceCandidate reference,
        BoardGeometryIndexedComponent component,
        SchematicSymbolAssemblerOptions options)
    {
        bool centerInside =
            component.CenterX >=
                reference.Bounds.Left -
                options.TextFragmentTolerancePixels &&
            component.CenterX <=
                reference.Bounds.Right +
                options.TextFragmentTolerancePixels &&
            component.CenterY >=
                reference.Bounds.Top -
                options.TextFragmentTolerancePixels &&
            component.CenterY <=
                reference.Bounds.Bottom +
                options.TextFragmentTolerancePixels;

        if (!centerInside)
        {
            return false;
        }

        double referenceArea =
            Math.Max(
                1D,
                reference.Bounds.Width *
                reference.Bounds.Height);

        double componentArea =
            Math.Max(
                1D,
                component.Bounds.Width *
                component.Bounds.Height);

        return componentArea /
               referenceArea <=
               options.MaximumTextFragmentAreaRatio;
    }

    private static double DirectionScore(
        BoardReferenceCandidate reference,
        BoardGeometryIndexedComponent component,
        SchematicReferenceFamily family,
        AssemblyProfile profile)
    {
        double deltaX =
            component.CenterX -
            reference.CenterX;

        double deltaY =
            component.CenterY -
            reference.CenterY;

        double referenceWidth =
            Math.Max(
                1D,
                reference.Bounds.Width);

        double referenceHeight =
            Math.Max(
                1D,
                reference.Bounds.Height);

        bool below =
            deltaY >=
            -referenceHeight *
            profile.AllowedAboveReferenceFactor;

        bool verticallyNear =
            deltaY <=
            referenceHeight *
            profile.MaximumVerticalDistanceFactor;

        bool horizontallyNear =
            Math.Abs(deltaX) <=
            referenceWidth *
            profile.MaximumHorizontalDistanceFactor;

        bool lateral =
            Math.Abs(deltaX) <=
            referenceWidth *
            profile.MaximumSideDistanceFactor &&
            Math.Abs(deltaY) <=
            referenceHeight *
            profile.MaximumSideVerticalFactor;

        if (family ==
            SchematicReferenceFamily.TestPoint)
        {
            return horizontallyNear &&
                   Math.Abs(deltaY) <=
                   referenceHeight * 3D
                ? 1D
                : lateral
                    ? 0.78D
                    : 0.30D;
        }

        if (below &&
            verticallyNear &&
            horizontallyNear)
        {
            return 1D;
        }

        if (lateral)
        {
            return 0.84D;
        }

        return below &&
               verticallyNear
            ? 0.56D
            : 0.16D;
    }

    private static double AlignmentScore(
        BoardReferenceCandidate reference,
        BoardGeometryIndexedComponent component,
        SchematicReferenceFamily family)
    {
        double verticalAxisScore =
            Clamp01(
                1D -
                Math.Abs(
                    reference.CenterX -
                    component.CenterX) /
                Math.Max(
                    1D,
                    Math.Max(
                        reference.Bounds.Width,
                        component.Bounds.Width) *
                    2D));

        double horizontalAxisScore =
            Clamp01(
                1D -
                Math.Abs(
                    reference.CenterY -
                    component.CenterY) /
                Math.Max(
                    1D,
                    Math.Max(
                        reference.Bounds.Height,
                        component.Bounds.Height) *
                    2D));

        return family is
                SchematicReferenceFamily.Connector or
                SchematicReferenceFamily.IntegratedCircuit
            ? Math.Max(
                verticalAxisScore,
                horizontalAxisScore)
            : verticalAxisScore;
    }

    private static double ContinuityScore(
        BoardGeometryBounds first,
        BoardGeometryBounds second,
        double allowedGap)
    {
        double horizontalOverlap =
            AxisOverlap(
                first.Left,
                first.Right,
                second.Left,
                second.Right);

        double verticalOverlap =
            AxisOverlap(
                first.Top,
                first.Bottom,
                second.Top,
                second.Bottom);

        double gapScore =
            Clamp01(
                1D -
                DistanceBetweenBounds(
                    first,
                    second) /
                Math.Max(
                    1D,
                    allowedGap));

        return Clamp01(
            (Math.Max(
                 horizontalOverlap,
                 verticalOverlap) * 0.62D) +
            (gapScore * 0.38D));
    }

    private static double TypeScore(
        SchematicReferenceFamily family,
        BoardGeometryComponentType type)
    {
        if (family ==
            SchematicReferenceFamily.TestPoint)
        {
            return type switch
            {
                BoardGeometryComponentType.Pad => 1.00D,
                BoardGeometryComponentType.Copper => 0.92D,
                BoardGeometryComponentType.Unknown => 0.78D,
                BoardGeometryComponentType.ComponentBody => 0.72D,
                BoardGeometryComponentType.Hole => 0.58D,
                _ => 0D
            };
        }

        return type switch
        {
            BoardGeometryComponentType.ComponentBody => 1.00D,
            BoardGeometryComponentType.Unknown => 0.96D,
            BoardGeometryComponentType.Copper => 0.91D,
            BoardGeometryComponentType.Pad => 0.38D,
            BoardGeometryComponentType.Hole => 0.22D,
            _ => 0D
        };
    }

    private static double ScaleScore(
        BoardGeometryBounds referenceBounds,
        BoardGeometryBounds componentBounds,
        SchematicReferenceFamily family)
    {
        double ratio =
            Math.Max(
                1D,
                componentBounds.Width *
                componentBounds.Height) /
            Math.Max(
                1D,
                referenceBounds.Width *
                referenceBounds.Height);

        (double minimum, double preferredMaximum, double absoluteMaximum) =
            family switch
            {
                SchematicReferenceFamily.IntegratedCircuit =>
                    (0.45D, 280D, 1000D),

                SchematicReferenceFamily.Connector =>
                    (0.35D, 220D, 850D),

                SchematicReferenceFamily.TestPoint =>
                    (0.10D, 30D, 140D),

                SchematicReferenceFamily.Passive =>
                    (0.14D, 55D, 220D),

                _ =>
                    (0.16D, 110D, 420D)
            };

        if (ratio < minimum)
        {
            return Clamp01(
                ratio /
                minimum *
                0.35D);
        }

        if (ratio <= preferredMaximum)
        {
            return 1D;
        }

        if (ratio <= absoluteMaximum)
        {
            return Clamp01(
                1D -
                ((ratio -
                  preferredMaximum) /
                 Math.Max(
                     1D,
                     absoluteMaximum -
                     preferredMaximum)) *
                0.68D);
        }

        return 0.06D;
    }

    private static double CalculateConfidence(
        BoardReferenceCandidate reference,
        IReadOnlyList<BoardGeometryIndexedComponent> components,
        BoardGeometryBounds bounds,
        SchematicReferenceFamily family,
        AssemblyProfile profile)
    {
        double componentConfidence =
            components.Average(
                component =>
                    component.Confidence);

        double nodeCountScore =
            Clamp01(
                components.Count /
                Math.Max(
                    1D,
                    profile.PreferredNodeCount));

        double typeScore =
            components
                .Select(component =>
                    TypeScore(
                        family,
                        component.Type))
                .DefaultIfEmpty(0D)
                .Average();

        double proximityScore =
            Clamp01(
                1D -
                DistanceBetweenBounds(
                    reference.Bounds,
                    bounds) /
                Math.Max(
                    1D,
                    profile.SeedSearchRadius));

        double extentScore =
            IsExtentValid(
                reference,
                bounds,
                family,
                profile)
                ? 1D
                : 0D;

        return Clamp01(
            (reference.Confidence * 0.17D) +
            (componentConfidence * 0.23D) +
            (nodeCountScore * 0.22D) +
            (typeScore * 0.18D) +
            (proximityScore * 0.12D) +
            (extentScore * 0.08D));
    }

    private static SchematicReferenceFamily ResolveFamily(
        string normalizedReference)
    {
        string prefix =
            new(
                normalizedReference
                    .TakeWhile(char.IsLetter)
                    .ToArray());

        return prefix.ToUpperInvariant() switch
        {
            "C" or "R" or "L" =>
                SchematicReferenceFamily.Passive,

            "D" or "LED" =>
                SchematicReferenceFamily.Diode,

            "Q" or "T" =>
                SchematicReferenceFamily.Transistor,

            "U" or "IC" =>
                SchematicReferenceFamily.IntegratedCircuit,

            "J" or "CN" or "CON" or "X" =>
                SchematicReferenceFamily.Connector,

            "TP" or "PP" or "P" =>
                SchematicReferenceFamily.TestPoint,

            "F" or "FB" or "Y" or "XTAL" or "SW" or "K" =>
                SchematicReferenceFamily.Discrete,

            _ =>
                SchematicReferenceFamily.Unknown
        };
    }

    private static AssemblyProfile CreateProfile(
        SchematicReferenceFamily family,
        BoardReferenceCandidate reference,
        SchematicSymbolAssemblerOptions options)
    {
        double width =
            Math.Max(
                1D,
                reference.Bounds.Width);

        double height =
            Math.Max(
                1D,
                reference.Bounds.Height);

        double baseRadius =
            Math.Max(
                options.MinimumSeedSearchRadiusPixels,
                Math.Max(
                    width *
                    options.SeedHorizontalSearchFactor,
                    height *
                    options.SeedVerticalSearchFactor));

        return family switch
        {
            SchematicReferenceFamily.Passive =>
                new AssemblyProfile(
                    baseRadius,
                    7D,
                    1.35D,
                    0.90D,
                    5D,
                    28D,
                    0.95D,
                    0.75D,
                    3.2D,
                    13D,
                    42D,
                    82D,
                    0.38D,
                    0.52D,
                    0.32D,
                    0.58D,
                    0.85D,
                    width * 4.5D,
                    height * 0.70D,
                    6D,
                    6D),

            SchematicReferenceFamily.IntegratedCircuit =>
                new AssemblyProfile(
                    baseRadius * 1.55D,
                    11D,
                    1.60D,
                    1.15D,
                    8D,
                    46D,
                    1.35D,
                    1.10D,
                    8D,
                    22D,
                    110D,
                    180D,
                    0.62D,
                    0.78D,
                    0.48D,
                    0.74D,
                    1.00D,
                    width * 8D,
                    height,
                    14D,
                    14D),

            SchematicReferenceFamily.Connector =>
                new AssemblyProfile(
                    baseRadius * 1.40D,
                    10D,
                    1.55D,
                    1.05D,
                    7D,
                    42D,
                    1.25D,
                    1D,
                    7D,
                    20D,
                    95D,
                    155D,
                    0.58D,
                    0.72D,
                    0.44D,
                    0.70D,
                    0.95D,
                    width * 7D,
                    height * 0.90D,
                    12D,
                    12D),

            SchematicReferenceFamily.TestPoint =>
                new AssemblyProfile(
                    baseRadius * 0.80D,
                    5D,
                    1.05D,
                    0.70D,
                    3D,
                    18D,
                    0.70D,
                    0.55D,
                    3D,
                    7D,
                    28D,
                    48D,
                    0.52D,
                    0.48D,
                    0.24D,
                    0.38D,
                    0.45D,
                    width * 3.5D,
                    height * 0.50D,
                    3D,
                    3D),

            _ =>
                new AssemblyProfile(
                    baseRadius,
                    8D,
                    1.45D,
                    1D,
                    6D,
                    34D,
                    1.05D,
                    0.88D,
                    5D,
                    16D,
                    65D,
                    115D,
                    0.48D,
                    0.62D,
                    0.38D,
                    0.64D,
                    0.80D,
                    width * 5.5D,
                    height * 0.80D,
                    8D,
                    8D)
        };
    }

    private static BoardGeometryBounds CombineBounds(
        IReadOnlyList<BoardGeometryIndexedComponent> components)
    {
        BoardGeometryBounds bounds =
            components[0].Bounds;

        foreach (BoardGeometryIndexedComponent component in components.Skip(1))
        {
            bounds =
                Union(
                    bounds,
                    component.Bounds);
        }

        return bounds;
    }

    private static double AxisOverlap(
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

        return Clamp01(
            overlap /
            Math.Min(
                Math.Max(
                    1D,
                    firstEnd -
                    firstStart),
                Math.Max(
                    1D,
                    secondEnd -
                    secondStart)));
    }

    private static BoardGeometryBounds Union(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        return CreateBounds(
            Math.Min(
                first.Left,
                second.Left),
            Math.Min(
                first.Top,
                second.Top),
            Math.Max(
                first.Right,
                second.Right),
            Math.Max(
                first.Bottom,
                second.Bottom));
    }

    private static BoardGeometryBounds ExpandAndClamp(
        BoardGeometryBounds bounds,
        double horizontalPadding,
        double topPadding,
        double bottomPadding,
        double pageWidth,
        double pageHeight)
    {
        return CreateBounds(
            Math.Max(
                0D,
                bounds.Left -
                horizontalPadding),
            Math.Max(
                0D,
                bounds.Top -
                topPadding),
            Math.Min(
                pageWidth,
                bounds.Right +
                horizontalPadding),
            Math.Min(
                pageHeight,
                bounds.Bottom +
                bottomPadding));
    }

    private static BoardGeometryBounds CreateBounds(
        double left,
        double top,
        double right,
        double bottom)
    {
        int integerLeft =
            checked((int)Math.Floor(left));

        int integerTop =
            checked((int)Math.Floor(top));

        int integerRight =
            checked((int)Math.Ceiling(right));

        int integerBottom =
            checked((int)Math.Ceiling(bottom));

        return new BoardGeometryBounds(
            integerLeft,
            integerTop,
            Math.Max(
                1,
                integerRight -
                integerLeft),
            Math.Max(
                1,
                integerBottom -
                integerTop));
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
            horizontalDistance *
            horizontalDistance +
            verticalDistance *
            verticalDistance);
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

    private readonly record struct ScoredComponent(
        BoardGeometryIndexedComponent Component,
        double Score);

    private readonly record struct AssemblyProfile(
        double SeedSearchRadius,
        double MinimumNodeSearchRadius,
        double ComponentRadiusFactor,
        double TextRadiusFactor,
        double MinimumConnectionGap,
        double MaximumConnectionGap,
        double ComponentGapFactor,
        double ReferenceGapFactor,
        double MaximumWidthFactor,
        double MaximumHeightFactor,
        double MinimumMaximumWidth,
        double MinimumMaximumHeight,
        double AllowedAboveReferenceFactor,
        double MaximumAboveReferenceFactor,
        double HorizontalPaddingFactor,
        double TopPaddingFactor,
        double BottomPaddingFactor,
        double ReferenceOwnershipRadius,
        double NeighborOwnershipMargin,
        double MinimumPadding,
        double PreferredNodeCount)
    {
        public double MaximumVerticalDistanceFactor =>
            MaximumHeightFactor;

        public double MaximumHorizontalDistanceFactor =>
            MaximumWidthFactor;

        public double MaximumSideDistanceFactor =>
            MaximumWidthFactor *
            1.20D;

        public double MaximumSideVerticalFactor =>
            Math.Max(
                3D,
                MaximumHeightFactor *
                0.36D);

        public double MaximumCenterOffsetFactor =>
            0.85D;
    }
}

/// <summary>
/// Opciones de la reconstrucción dinámica.
/// </summary>
public sealed record SchematicSymbolAssemblerOptions
{
    public static SchematicSymbolAssemblerOptions Default { get; } =
        new();

    public double MinimumSeedSearchRadiusPixels { get; init; } = 64D;
    public double SeedHorizontalSearchFactor { get; init; } = 6D;
    public double SeedVerticalSearchFactor { get; init; } = 14D;
    public int MaximumSeedCandidates { get; init; } = 96;
    public int MaximumNeighborsPerNode { get; init; } = 48;
    public int MaximumComponentsPerSymbol { get; init; } = 48;
    public int MaximumExpandedNodes { get; init; } = 96;
    public double MinimumComponentConfidence { get; init; } = 0.12D;
    public double MinimumSeedScore { get; init; } = 0.40D;
    public double MinimumConnectionScore { get; init; } = 0.44D;
    public double MinimumAssemblyConfidence { get; init; } = 0.36D;
    public double TextFragmentTolerancePixels { get; init; } = 2D;
    public double MaximumTextFragmentAreaRatio { get; init; } = 0.90D;

    /// <summary>
    /// Configuración utilizada para construir el grafo eléctrico de la página.
    /// </summary>
    public SchematicElectricalGraphBuilderOptions ElectricalGraphBuilderOptions
    {
        get;
        init;
    } = SchematicElectricalGraphBuilderOptions.Default;

    /// <summary>Confianza mínima de una arista recorrible.</summary>
    public double MinimumGraphEdgeConfidence { get; init; } = 0.52D;

    /// <summary>
    /// Confianza exigida para nodos débiles como Unknown y Hole.
    /// </summary>
    public double MinimumWeakNodeEdgeConfidence { get; init; } = 0.68D;

    /// <summary>
    /// Confianza exigida para continuar una cadena wire-to-wire.
    /// </summary>
    public double MinimumWireChainEdgeConfidence { get; init; } = 0.72D;

    /// <summary>
    /// Dirección mínima para incorporar cuerpos que no sean wires ni junctions.
    /// </summary>
    public double MinimumGraphDirectionScore { get; init; } = 0.20D;

    public void Validate()
    {
        ValidatePositiveFinite(
            MinimumSeedSearchRadiusPixels,
            nameof(MinimumSeedSearchRadiusPixels));

        ValidatePositiveFinite(
            SeedHorizontalSearchFactor,
            nameof(SeedHorizontalSearchFactor));

        ValidatePositiveFinite(
            SeedVerticalSearchFactor,
            nameof(SeedVerticalSearchFactor));

        ValidatePositive(
            MaximumSeedCandidates,
            nameof(MaximumSeedCandidates));

        ValidatePositive(
            MaximumNeighborsPerNode,
            nameof(MaximumNeighborsPerNode));

        ValidatePositive(
            MaximumComponentsPerSymbol,
            nameof(MaximumComponentsPerSymbol));

        ValidatePositive(
            MaximumExpandedNodes,
            nameof(MaximumExpandedNodes));

        ValidateProbability(
            MinimumComponentConfidence,
            nameof(MinimumComponentConfidence));

        ValidateProbability(
            MinimumSeedScore,
            nameof(MinimumSeedScore));

        ValidateProbability(
            MinimumConnectionScore,
            nameof(MinimumConnectionScore));

        ValidateProbability(
            MinimumAssemblyConfidence,
            nameof(MinimumAssemblyConfidence));

        ArgumentNullException.ThrowIfNull(
            ElectricalGraphBuilderOptions);

        ElectricalGraphBuilderOptions.Validate();

        ValidateProbability(
            MinimumGraphEdgeConfidence,
            nameof(MinimumGraphEdgeConfidence));

        ValidateProbability(
            MinimumWeakNodeEdgeConfidence,
            nameof(MinimumWeakNodeEdgeConfidence));

        ValidateProbability(
            MinimumWireChainEdgeConfidence,
            nameof(MinimumWireChainEdgeConfidence));

        ValidateProbability(
            MinimumGraphDirectionScore,
            nameof(MinimumGraphDirectionScore));

        if (!double.IsFinite(
                TextFragmentTolerancePixels) ||
            TextFragmentTolerancePixels < 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TextFragmentTolerancePixels));
        }

        ValidatePositiveFinite(
            MaximumTextFragmentAreaRatio,
            nameof(MaximumTextFragmentAreaRatio));
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

public enum SchematicReferenceFamily
{
    Unknown = 0,
    Passive = 1,
    Diode = 2,
    Transistor = 3,
    IntegratedCircuit = 4,
    Connector = 5,
    TestPoint = 6,
    Discrete = 7
}

public sealed record SchematicSymbol(
    string Reference,
    int PageIndex,
    BoardGeometryBounds Bounds,
    IReadOnlyList<BoardGeometryIndexedComponent> Components,
    double Confidence)
{
    public double CenterX =>
        Bounds.Left +
        Bounds.Width /
        2D;

    public double CenterY =>
        Bounds.Top +
        Bounds.Height /
        2D;
}

public sealed class SchematicSymbolAssemblyResult
{
    private readonly IReadOnlyDictionary<string, SchematicSymbol> symbols;
    private readonly IReadOnlyCollection<SchematicSymbol> symbolValues;

    public static SchematicSymbolAssemblyResult Empty { get; } =
        new(
            Array.Empty<SchematicSymbol>());

    public SchematicSymbolAssemblyResult(
        IEnumerable<SchematicSymbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        this.symbols =
            symbols
                .GroupBy(
                    symbol => symbol.Reference,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(
                            symbol => symbol.Confidence)
                        .First())
                .ToDictionary(
                    symbol => symbol.Reference,
                    StringComparer.OrdinalIgnoreCase);

        symbolValues =
            this.symbols
                .Values
                .OrderBy(symbol => symbol.PageIndex)
                .ThenBy(
                    symbol => symbol.Reference,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    public int Count =>
        symbols.Count;

    public IReadOnlyCollection<SchematicSymbol> Symbols =>
        symbolValues;

    public bool TryGetByReference(
        string reference,
        out SchematicSymbol? symbol)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            symbol =
                null;

            return false;
        }

        return symbols.TryGetValue(
            reference.Trim(),
            out symbol);
    }
}