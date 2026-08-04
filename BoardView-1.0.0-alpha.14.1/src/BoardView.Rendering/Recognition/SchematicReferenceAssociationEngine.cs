using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Ancla referencias PDFium a nodos del grafo eléctrico.
/// </summary>
/// <remarks>
/// La referencia textual es la entidad primaria. La geometría sólo se utiliza
/// para elegir un nodo raíz conectado semánticamente con el símbolo.
/// Los métodos Associate permanecen como adaptadores para BoardReferenceIndex.
/// </remarks>
public sealed class SchematicReferenceAssociationEngine
{
    private readonly SchematicElectricalGraphBuilder graphBuilder;

    public SchematicReferenceAssociationEngine()
        : this(new SchematicElectricalGraphBuilder())
    {
    }

    public SchematicReferenceAssociationEngine(
        SchematicElectricalGraphBuilder graphBuilder)
    {
        this.graphBuilder =
            graphBuilder ?? throw new ArgumentNullException(nameof(graphBuilder));
    }

    public SchematicReferenceAnchorResult Anchor(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates,
        BoardReferenceAssociationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geometryIndex);

        SchematicElectricalGraph graph =
            graphBuilder.Build(
                geometryIndex,
                SchematicElectricalGraphBuilderOptions.Default,
                cancellationToken);

        return Anchor(graph, candidates, options, cancellationToken);
    }

    public SchematicReferenceAnchorResult Anchor(
        SchematicElectricalGraph graph,
        IEnumerable<BoardReferenceCandidate> candidates,
        BoardReferenceAssociationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        BoardReferenceCandidate[] candidateArray =
            candidates
                .Where(candidate => candidate.IsReferenceLike)
                .OrderBy(candidate => candidate.Id)
                .ToArray();

        ValidateCandidateIdentifiers(candidateArray);

        var proposals = new List<AnchorProposal>();

        foreach (BoardReferenceCandidate candidate in candidateArray)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!CanEvaluate(candidate, graph, options))
            {
                continue;
            }

            SchematicReferenceFamily family =
                ResolveFamily(candidate.NormalizedReference);

            double searchDistance =
                ResolveSearchDistance(candidate, family, options);

            foreach (SchematicElectricalNode node in graph.Nodes)
            {
                double distance =
                    DistanceBetweenBounds(candidate.Bounds, node.Bounds);

                if (distance > searchDistance)
                {
                    continue;
                }

                AnchorProposal proposal =
                    CreateProposal(
                        graph,
                        candidate,
                        node,
                        family,
                        searchDistance);

                if (proposal.Score >= options.MinimumAssociationScore)
                {
                    proposals.Add(proposal);
                }
            }
        }

        return new SchematicReferenceAnchorResult(
            ResolveProposals(proposals, options, cancellationToken));
    }

    public BoardReferenceAssociationResult Associate(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates) =>
        Associate(
            geometryIndex,
            candidates,
            BoardReferenceAssociationOptions.Default,
            CancellationToken.None);

    public BoardReferenceAssociationResult Associate(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates,
        BoardReferenceAssociationOptions options) =>
        Associate(geometryIndex, candidates, options, CancellationToken.None);

    public BoardReferenceAssociationResult Associate(
        BoardGeometryIndex geometryIndex,
        IEnumerable<BoardReferenceCandidate> candidates,
        BoardReferenceAssociationOptions options,
        CancellationToken cancellationToken)
    {
        BoardReferenceCandidate[] candidateArray =
            candidates.OrderBy(candidate => candidate.Id).ToArray();

        SchematicReferenceAnchorResult anchors =
            Anchor(
                geometryIndex,
                candidateArray,
                options,
                cancellationToken);

        return CreateAssociationResult(
            candidateArray,
            anchors);
    }

    /// <summary>
    /// Proyecta anclajes semánticos al índice histórico de referencias sin
    /// reconstruir el grafo ni volver a resolver semillas.
    /// </summary>
    public BoardReferenceAssociationResult CreateAssociationResult(
        IEnumerable<BoardReferenceCandidate> candidates,
        SchematicReferenceAnchorResult anchors)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(anchors);

        BoardReferenceCandidate[] candidateArray =
            candidates
                .OrderBy(candidate => candidate.Id)
                .ToArray();

        BoardReferenceAssociation[] associations =
            anchors.Anchors
                .Select(anchor => new BoardReferenceAssociation(
                    anchor.Candidate,
                    anchor.SeedNode.Component,
                    anchor.Confidence,
                    anchor.DistancePixels,
                    ConvertRule(anchor.Rule)))
                .ToArray();

        return new BoardReferenceAssociationResult(
            candidateArray,
            associations);
    }

    private static AnchorProposal CreateProposal(
        SchematicElectricalGraph graph,
        BoardReferenceCandidate candidate,
        SchematicElectricalNode node,
        SchematicReferenceFamily family,
        double searchDistance)
    {
        double distance =
            DistanceBetweenBounds(candidate.Bounds, node.Bounds);

        double distanceScore =
            Clamp01(1D - distance / Math.Max(1D, searchDistance));

        double directionScore =
            DirectionScore(candidate, node, family);

        double roleScore =
            RoleScore(family, node.Kind);

        double fragmentPenalty =
            TextFragmentPenalty(candidate, node);

        ConnectedBody body =
            FindConnectedBody(graph, node, candidate, family);

        double topologyScore =
            TopologyScore(graph, node, body);

        double scaleScore =
            ScaleScore(candidate.Bounds, node.Bounds, family);

        double score =
            Clamp01(
                candidate.Confidence * 0.16D +
                node.Confidence * 0.10D +
                distanceScore * 0.16D +
                directionScore * 0.22D +
                roleScore * 0.16D +
                topologyScore * 0.14D +
                scaleScore * 0.06D -
                fragmentPenalty * 0.72D);

        return new AnchorProposal(
            candidate,
            node,
            body.BodyNode,
            score,
            distance,
            ResolveRule(family, node, directionScore, body),
            topologyScore,
            directionScore);
    }

    private static ConnectedBody FindConnectedBody(
        SchematicElectricalGraph graph,
        SchematicElectricalNode seed,
        BoardReferenceCandidate candidate,
        SchematicReferenceFamily family)
    {
        if (seed.Kind == SchematicElectricalNodeKind.SymbolBody)
        {
            return new ConnectedBody(seed, 1D, 0);
        }

        var visited = new HashSet<int> { seed.Id };
        var queue = new Queue<(int Id, int Depth, double Confidence)>();
        queue.Enqueue((seed.Id, 0, 1D));

        SchematicElectricalNode? bestBody = null;
        double bestScore = 0D;
        int bestDepth = -1;

        while (queue.Count > 0)
        {
            (int id, int depth, double pathConfidence) = queue.Dequeue();

            if (depth >= 4)
            {
                continue;
            }

            foreach (SchematicElectricalEdge edge in graph.GetEdges(id))
            {
                if (edge.Confidence < 0.52D)
                {
                    continue;
                }

                int otherId = edge.GetOtherNodeId(id);

                if (!visited.Add(otherId) ||
                    !graph.TryGetNode(otherId, out SchematicElectricalNode? other) ||
                    other is null)
                {
                    continue;
                }

                int nextDepth = depth + 1;
                double nextConfidence = Math.Min(pathConfidence, edge.Confidence);

                if (other.Kind == SchematicElectricalNodeKind.SymbolBody)
                {
                    double score =
                        Clamp01(
                            nextConfidence * 0.62D +
                            DirectionScore(candidate, other, family) * 0.28D +
                            (1D - nextDepth / 5D) * 0.10D);

                    if (score > bestScore)
                    {
                        bestBody = other;
                        bestScore = score;
                        bestDepth = nextDepth;
                    }

                    continue;
                }

                if (other.Kind is
                    SchematicElectricalNodeKind.Wire or
                    SchematicElectricalNodeKind.Pin or
                    SchematicElectricalNodeKind.Terminal or
                    SchematicElectricalNodeKind.Junction or
                    SchematicElectricalNodeKind.Unknown)
                {
                    queue.Enqueue((other.Id, nextDepth, nextConfidence));
                }
            }
        }

        return new ConnectedBody(bestBody, bestScore, bestDepth);
    }

    private static double TopologyScore(
        SchematicElectricalGraph graph,
        SchematicElectricalNode node,
        ConnectedBody body)
    {
        IReadOnlyList<SchematicElectricalEdge> edges = graph.GetEdges(node.Id);

        double degree = Clamp01(edges.Count / 4D);
        double strongEdges =
            edges.Count == 0
                ? 0D
                : edges
                    .Select(edge => edge.Confidence)
                    .OrderByDescending(value => value)
                    .Take(3)
                    .Average();

        return Clamp01(
            degree * 0.22D +
            strongEdges * 0.30D +
            body.Confidence * 0.48D);
    }

    private static double RoleScore(
        SchematicReferenceFamily family,
        SchematicElectricalNodeKind kind)
    {
        if (family == SchematicReferenceFamily.TestPoint)
        {
            return kind switch
            {
                SchematicElectricalNodeKind.Junction => 1.00D,
                SchematicElectricalNodeKind.Pad => 0.96D,
                SchematicElectricalNodeKind.Terminal => 0.90D,
                SchematicElectricalNodeKind.Pin => 0.84D,
                SchematicElectricalNodeKind.Wire => 0.66D,
                _ => 0.20D
            };
        }

        return kind switch
        {
            SchematicElectricalNodeKind.SymbolBody => 1.00D,
            SchematicElectricalNodeKind.Pin => 0.94D,
            SchematicElectricalNodeKind.Terminal => 0.90D,
            SchematicElectricalNodeKind.Junction => 0.62D,
            SchematicElectricalNodeKind.Wire => 0.42D,
            SchematicElectricalNodeKind.Unknown => 0.38D,
            SchematicElectricalNodeKind.Pad => 0.24D,
            SchematicElectricalNodeKind.Hole => 0.06D,
            _ => 0.12D
        };
    }

    private static double DirectionScore(
        BoardReferenceCandidate candidate,
        SchematicElectricalNode node,
        SchematicReferenceFamily family)
    {
        double dx = node.CenterX - candidate.CenterX;
        double dy = node.CenterY - candidate.CenterY;
        double width = Math.Max(1D, candidate.Bounds.Width);
        double height = Math.Max(1D, candidate.Bounds.Height);

        if (family == SchematicReferenceFamily.TestPoint)
        {
            return Math.Abs(dx) <= width * 2D &&
                   Math.Abs(dy) <= height * 3D
                ? 1D
                : 0.32D;
        }

        if (dy >= height * 0.20D &&
            dy <= height * 9D &&
            Math.Abs(dx) <= width * 1.60D)
        {
            return 1D;
        }

        if (Math.Abs(dx) >= width * 0.35D &&
            Math.Abs(dx) <= width * 3.20D &&
            Math.Abs(dy) <= height * 2.80D)
        {
            return 0.82D;
        }

        // Por encima del texto suele estar la red o su etiqueta.
        if (dy < -height * 0.15D)
        {
            return 0.08D;
        }

        return dy >= 0D && dy <= height * 12D
            ? 0.55D
            : 0.18D;
    }

    private static double TextFragmentPenalty(
        BoardReferenceCandidate candidate,
        SchematicElectricalNode node)
    {
        bool inside =
            node.CenterX >= candidate.Bounds.Left &&
            node.CenterX <= candidate.Bounds.Right &&
            node.CenterY >= candidate.Bounds.Top &&
            node.CenterY <= candidate.Bounds.Bottom;

        if (!inside)
        {
            return 0D;
        }

        double ratio =
            Math.Max(1D, node.Bounds.Width * node.Bounds.Height) /
            Math.Max(1D, candidate.Bounds.Width * candidate.Bounds.Height);

        if (ratio <= 0.30D) return 1D;
        if (ratio <= 0.75D) return 0.88D;
        if (ratio <= 1.50D) return 0.62D;

        return node.Kind is
            SchematicElectricalNodeKind.Wire or
            SchematicElectricalNodeKind.Junction
                ? 0.55D
                : 0.18D;
    }

    private static double ScaleScore(
        BoardGeometryBounds text,
        BoardGeometryBounds node,
        SchematicReferenceFamily family)
    {
        double ratio =
            Math.Max(1D, node.Width * node.Height) /
            Math.Max(1D, text.Width * text.Height);

        if (family == SchematicReferenceFamily.TestPoint)
        {
            if (ratio < 0.08D) return 0.10D;
            if (ratio <= 20D) return 1D;
            if (ratio <= 80D) return 0.60D;
            return 0.18D;
        }

        if (ratio < 0.15D) return 0.08D;
        if (ratio < 0.50D) return 0.30D;
        if (ratio <= 60D) return 1D;
        if (ratio <= 180D) return 0.62D;
        return 0.16D;
    }

    private static IReadOnlyList<SchematicReferenceAnchor> ResolveProposals(
        IEnumerable<AnchorProposal> proposals,
        BoardReferenceAssociationOptions options,
        CancellationToken cancellationToken)
    {
        AnchorProposal[] ordered =
            proposals
                .OrderByDescending(proposal => proposal.Score)
                .ThenByDescending(proposal => proposal.TopologyScore)
                .ThenByDescending(proposal => proposal.DirectionScore)
                .ThenBy(proposal => proposal.Distance)
                .ThenBy(proposal => proposal.Candidate.Id)
                .ThenBy(proposal => proposal.Seed.Id)
                .ToArray();

        var usedCandidates = new HashSet<int>();
        var usedSeeds = new HashSet<int>();
        var result = new List<SchematicReferenceAnchor>();

        foreach (AnchorProposal proposal in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!usedCandidates.Add(proposal.Candidate.Id))
            {
                continue;
            }

            if (!options.AllowMultipleReferencesPerComponent &&
                !usedSeeds.Add(proposal.Seed.Id))
            {
                usedCandidates.Remove(proposal.Candidate.Id);
                continue;
            }

            usedSeeds.Add(proposal.Seed.Id);

            result.Add(
                new SchematicReferenceAnchor(
                    proposal.Candidate,
                    proposal.Seed,
                    proposal.Body,
                    proposal.Score,
                    proposal.Distance,
                    proposal.Rule));
        }

        return result;
    }

    private static bool CanEvaluate(
        BoardReferenceCandidate candidate,
        SchematicElectricalGraph graph,
        BoardReferenceAssociationOptions options) =>
        candidate.IsReferenceLike &&
        candidate.Confidence >= options.MinimumCandidateConfidence &&
        candidate.CenterX >= 0D &&
        candidate.CenterY >= 0D &&
        candidate.CenterX <= graph.PageWidth &&
        candidate.CenterY <= graph.PageHeight;

    private static double ResolveSearchDistance(
        BoardReferenceCandidate candidate,
        SchematicReferenceFamily family,
        BoardReferenceAssociationOptions options)
    {
        double multiplier =
            family switch
            {
                SchematicReferenceFamily.IntegratedCircuit => 1.55D,
                SchematicReferenceFamily.Connector => 1.40D,
                SchematicReferenceFamily.TestPoint => 0.85D,
                _ => 1D
            };

        return Math.Max(
            options.MaximumDistancePixels,
            Math.Max(
                candidate.Bounds.Width * 3.5D,
                candidate.Bounds.Height * 10D) *
            multiplier);
    }

    private static SchematicReferenceAnchorRule ResolveRule(
        SchematicReferenceFamily family,
        SchematicElectricalNode node,
        double directionScore,
        ConnectedBody body)
    {
        if (family == SchematicReferenceFamily.TestPoint)
            return SchematicReferenceAnchorRule.TestPoint;

        if (body.BodyNode is not null &&
            body.BodyNode.Id != node.Id)
            return SchematicReferenceAnchorRule.ConnectedBody;

        if (node.Kind == SchematicElectricalNodeKind.SymbolBody &&
            directionScore >= 0.75D)
            return SchematicReferenceAnchorRule.BodyBelowReference;

        if (node.Kind == SchematicElectricalNodeKind.Pin &&
            directionScore >= 0.75D)
            return SchematicReferenceAnchorRule.PinBelowReference;

        if (node.Kind == SchematicElectricalNodeKind.Terminal &&
            directionScore >= 0.75D)
            return SchematicReferenceAnchorRule.TerminalBelowReference;

        return directionScore >= 0.75D
            ? SchematicReferenceAnchorRule.LateralSymbol
            : SchematicReferenceAnchorRule.GraphTopology;
    }

    private static BoardReferenceAssociationRule ConvertRule(
        SchematicReferenceAnchorRule rule) =>
        rule switch
        {
            SchematicReferenceAnchorRule.BodyBelowReference or
            SchematicReferenceAnchorRule.PinBelowReference or
            SchematicReferenceAnchorRule.TerminalBelowReference =>
                BoardReferenceAssociationRule.VerticalAlignment,

            SchematicReferenceAnchorRule.LateralSymbol =>
                BoardReferenceAssociationRule.HorizontalAlignment,

            SchematicReferenceAnchorRule.ConnectedBody or
            SchematicReferenceAnchorRule.TestPoint or
            SchematicReferenceAnchorRule.GraphTopology =>
                BoardReferenceAssociationRule.SemanticPriority,

            _ => BoardReferenceAssociationRule.NearestComponent
        };

    private static SchematicReferenceFamily ResolveFamily(string reference)
    {
        string prefix =
            new(reference.TakeWhile(char.IsLetter).ToArray());

        return prefix.ToUpperInvariant() switch
        {
            "C" or "R" or "L" => SchematicReferenceFamily.Passive,
            "D" or "LED" => SchematicReferenceFamily.Diode,
            "Q" or "T" => SchematicReferenceFamily.Transistor,
            "U" or "IC" => SchematicReferenceFamily.IntegratedCircuit,
            "J" or "CN" or "CON" or "X" => SchematicReferenceFamily.Connector,
            "TP" or "PP" or "P" => SchematicReferenceFamily.TestPoint,
            "F" or "FB" or "Y" or "XTAL" or "SW" or "K" =>
                SchematicReferenceFamily.Discrete,
            _ => SchematicReferenceFamily.Unknown
        };
    }

    private static double DistanceBetweenBounds(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        double dx =
            first.Right < second.Left
                ? second.Left - first.Right
                : second.Right < first.Left
                    ? first.Left - second.Right
                    : 0D;

        double dy =
            first.Bottom < second.Top
                ? second.Top - first.Bottom
                : second.Bottom < first.Top
                    ? first.Top - second.Bottom
                    : 0D;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void ValidateCandidateIdentifiers(
        IReadOnlyList<BoardReferenceCandidate> candidates)
    {
        if (candidates.Select(candidate => candidate.Id).Distinct().Count() !=
            candidates.Count)
        {
            throw new ArgumentException(
                "Los identificadores de candidatos deben ser únicos.",
                nameof(candidates));
        }
    }

    private static double Clamp01(double value) =>
        Math.Max(0D, Math.Min(1D, value));

    private readonly record struct AnchorProposal(
        BoardReferenceCandidate Candidate,
        SchematicElectricalNode Seed,
        SchematicElectricalNode? Body,
        double Score,
        double Distance,
        SchematicReferenceAnchorRule Rule,
        double TopologyScore,
        double DirectionScore);

    private readonly record struct ConnectedBody(
        SchematicElectricalNode? BodyNode,
        double Confidence,
        int Depth);
}