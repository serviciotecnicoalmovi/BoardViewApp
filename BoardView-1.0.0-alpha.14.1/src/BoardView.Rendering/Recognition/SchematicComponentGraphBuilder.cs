using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Familia eléctrica reconstruida a partir de una referencia esquemática.
/// </summary>
public enum SchematicComponentKind
{
    Unknown = 0,
    Capacitor = 1,
    Resistor = 2,
    Inductor = 3,
    Diode = 4,
    Led = 5,
    Fuse = 6,
    FerriteBead = 7,
    Crystal = 8,
    Switch = 9,
    Relay = 10,
    Transistor = 11,
    IntegratedCircuit = 12,
    Connector = 13,
    TestPoint = 14
}

/// <summary>
/// Orientación dominante de un componente reconstruido.
/// </summary>
public enum SchematicComponentOrientation
{
    Unknown = 0,
    Horizontal = 1,
    Vertical = 2,
    Compact = 3
}

/// <summary>
/// Terminal perteneciente a un componente eléctrico reconstruido.
/// </summary>
public sealed record SchematicComponentTerminal(
    int NodeId,
    int Number,
    BoardGeometryBounds Bounds,
    double CenterX,
    double CenterY,
    IReadOnlyList<string> NetNames,
    double Confidence);

/// <summary>
/// Componente eléctrico completo reconstruido desde el grafo.
/// </summary>
public sealed record SchematicGraphComponent(
    string Reference,
    SchematicComponentKind Kind,
    SchematicComponentOrientation Orientation,
    BoardGeometryBounds Bounds,
    int SeedNodeId,
    int? BodyNodeId,
    IReadOnlyList<int> MemberNodeIds,
    IReadOnlyList<SchematicComponentTerminal> Terminals,
    IReadOnlyList<string> NetNames,
    double Confidence)
{
    public int TerminalCount =>
        Terminals.Count;

    public bool IsConnected =>
        Terminals.Any(terminal =>
            terminal.NetNames.Count > 0);
}

/// <summary>
/// Resultado inmutable de la reconstrucción de componentes.
/// </summary>
public sealed class SchematicComponentGraphResult
{
    private readonly IReadOnlyDictionary<string, SchematicGraphComponent>
        byReference;

    public static SchematicComponentGraphResult Empty { get; } =
        new(Array.Empty<SchematicGraphComponent>());

    public SchematicComponentGraphResult(
        IEnumerable<SchematicGraphComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        SchematicGraphComponent[] array =
            components
                .OrderBy(
                    component =>
                        component.Reference,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        Components = array;

        byReference =
            array
                .GroupBy(
                    component =>
                        component.Reference,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(
                            component =>
                                component.Confidence)
                        .First())
                .ToDictionary(
                    component =>
                        component.Reference,
                    StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SchematicGraphComponent> Components { get; }

    public int Count =>
        Components.Count;

    public bool TryGetByReference(
        string reference,
        out SchematicGraphComponent? component)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            component = null;
            return false;
        }

        return byReference.TryGetValue(
            BoardReferenceCandidate.NormalizeReference(reference),
            out component);
    }
}

/// <summary>
/// Reconstruye componentes eléctricos completos sobre un
/// <see cref="SchematicElectricalGraph"/> ya consolidado.
/// </summary>
/// <remarks>
/// Este constructor no vuelve a detectar geometría. Consume exclusivamente:
/// <list type="bullet">
/// <item>el anclaje semántico de cada referencia;</item>
/// <item>los nodos del grafo eléctrico;</item>
/// <item>las aristas ya verificadas por el GraphBuilder.</item>
/// </list>
///
/// La reconstrucción se limita al cuerpo, pines y terminales propiedad de cada
/// referencia. Las redes se consultan para identificar los nombres conectados,
/// pero no se incorporan completas a los límites visuales del componente.
/// </remarks>
public sealed class SchematicComponentGraphBuilder
{
    private const int MaximumOwnershipDepth = 4;
    private const int MaximumNetworkDepth = 48;
    private const double MinimumOwnershipEdgeConfidence = 0.68D;
    private const double MinimumNetworkEdgeConfidence = 0.72D;

    /// <summary>
    /// Reconstruye todos los componentes anclados de la página.
    /// </summary>
    public SchematicComponentGraphResult Build(
        SchematicElectricalGraph graph,
        SchematicReferenceAnchorResult anchors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(anchors);

        if (graph.NodeCount == 0 ||
            anchors.Count == 0)
        {
            return SchematicComponentGraphResult.Empty;
        }

        var components =
            new List<SchematicGraphComponent>();

        foreach (SchematicReferenceAnchor anchor in
                 anchors.Anchors
                     .OrderBy(item => item.Candidate.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();

            SchematicGraphComponent? component =
                Reconstruct(
                    graph,
                    anchor,
                    cancellationToken);

            if (component is not null)
            {
                components.Add(component);
            }
        }

        return new SchematicComponentGraphResult(
            components);
    }

    private static SchematicGraphComponent? Reconstruct(
        SchematicElectricalGraph graph,
        SchematicReferenceAnchor anchor,
        CancellationToken cancellationToken)
    {
        SchematicElectricalNode seed =
            anchor.SymbolBodyNode ??
            anchor.SeedNode;

        IReadOnlyList<SchematicElectricalNode> ownedNodes =
            ExploreOwnedNodes(
                graph,
                seed,
                cancellationToken);

        if (ownedNodes.Count == 0)
        {
            return null;
        }

        SchematicElectricalNode[] visualNodes =
            ownedNodes
                .Where(IsVisualMember)
                .ToArray();

        if (visualNodes.Length == 0)
        {
            visualNodes =
            [
                seed
            ];
        }

        SchematicElectricalNode[] terminalNodes =
            ownedNodes
                .Where(node =>
                    node.Kind is
                        SchematicElectricalNodeKind.Pin or
                        SchematicElectricalNodeKind.Terminal)
                .OrderBy(node =>
                    ResolveTerminalSortCoordinate(
                        node,
                        visualNodes))
                .ThenBy(node => node.Id)
                .ToArray();

        SchematicComponentTerminal[] terminals =
            terminalNodes
                .Select((node, index) =>
                    CreateTerminal(
                        graph,
                        node,
                        index + 1,
                        cancellationToken))
                .ToArray();

        string[] netNames =
            terminals
                .SelectMany(
                    terminal =>
                        terminal.NetNames)
                .Distinct(
                    StringComparer.Ordinal)
                .OrderBy(
                    name => name,
                    StringComparer.Ordinal)
                .ToArray();

        BoardGeometryBounds bounds =
            CombineBounds(
                visualNodes.Select(
                    node =>
                        node.Bounds));

        SchematicComponentKind kind =
            ResolveKind(
                anchor.Reference);

        SchematicComponentOrientation orientation =
            ResolveOrientation(
                bounds,
                terminalNodes);

        /*
         * Protección final contra cajas de propiedad desproporcionadas. Si la
         * exploración produce una región incompatible con el ancla, se conserva
         * el nodo semilla para no desplazar el visor hacia otra zona de la página.
         */
        double maximumExtent =
            Math.Max(
                64D,
                Math.Max(
                    seed.Bounds.Width,
                    seed.Bounds.Height) *
                5D);

        if (bounds.Width > maximumExtent ||
            bounds.Height > maximumExtent ||
            DistanceBetweenBounds(
                seed.Bounds,
                bounds) >
            ResolveOwnershipRadius(seed.Bounds))
        {
            visualNodes =
            [
                seed
            ];

            terminalNodes =
                Array.Empty<SchematicElectricalNode>();

            terminals =
                Array.Empty<SchematicComponentTerminal>();

            netNames =
                Array.Empty<string>();

            bounds =
                seed.Bounds;
        }

        double confidence =
            CalculateConfidence(
                anchor,
                visualNodes,
                terminals);

        return new SchematicGraphComponent(
            anchor.Reference,
            kind,
            orientation,
            bounds,
            anchor.SeedNode.Id,
            anchor.SymbolBodyNode?.Id,
            ownedNodes
                .Select(node => node.Id)
                .Distinct()
                .OrderBy(id => id)
                .ToArray(),
            terminals,
            netNames,
            confidence);
    }

    /// <summary>
    /// Reúne únicamente nodos que pueden pertenecer físicamente al símbolo.
    /// Se detiene antes de expandirse por redes, etiquetas o símbolos vecinos.
    /// </summary>
    private static IReadOnlyList<SchematicElectricalNode> ExploreOwnedNodes(
        SchematicElectricalGraph graph,
        SchematicElectricalNode seed,
        CancellationToken cancellationToken)
    {
        var visited =
            new HashSet<int>
            {
                seed.Id
            };

        var queue =
            new Queue<(int NodeId, int Depth)>();

        queue.Enqueue(
            (seed.Id, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int currentId, int depth) =
                queue.Dequeue();

            if (depth >= MaximumOwnershipDepth)
            {
                continue;
            }

            foreach (SchematicElectricalEdge edge in
                     graph.GetEdges(currentId))
            {
                if (edge.Confidence <
                    MinimumOwnershipEdgeConfidence)
                {
                    continue;
                }

                int neighborId =
                    edge.GetOtherNodeId(
                        currentId);

                if (visited.Contains(neighborId) ||
                    !graph.TryGetNode(
                        neighborId,
                        out SchematicElectricalNode? neighbor) ||
                    neighbor is null)
                {
                    continue;
                }

                if (!CanOwnNode(
                        seed,
                        neighbor,
                        depth + 1))
                {
                    continue;
                }

                visited.Add(neighborId);

                queue.Enqueue(
                    (neighborId, depth + 1));
            }
        }

        return visited
            .Select(nodeId =>
            {
                graph.TryGetNode(
                    nodeId,
                    out SchematicElectricalNode? node);

                return node!;
            })
            .Where(node => node is not null)
            .OrderBy(node => node.Id)
            .ToArray();
    }

    private static bool CanOwnNode(
        SchematicElectricalNode seed,
        SchematicElectricalNode candidate,
        int depth)
    {
        if (candidate.Id ==
            seed.Id)
        {
            return true;
        }

        if (candidate.Kind ==
            SchematicElectricalNodeKind.SymbolBody)
        {
            return false;
        }

        if (candidate.Kind is
                SchematicElectricalNodeKind.NetLabel or
                SchematicElectricalNodeKind.Bus or
                SchematicElectricalNodeKind.BusEntry or
                SchematicElectricalNodeKind.Ground or
                SchematicElectricalNodeKind.PowerPort)
        {
            return false;
        }

        double ownershipRadius =
            ResolveOwnershipRadius(
                seed.Bounds);

        double distanceFromSeed =
            DistanceBetweenBounds(
                seed.Bounds,
                candidate.Bounds);

        if (candidate.Kind is
                SchematicElectricalNodeKind.Pin or
                SchematicElectricalNodeKind.Terminal)
        {
            /*
             * Un pin o terminal sólo pertenece al componente cuando continúa
             * dentro de su vecindad física. La versión anterior aceptaba todos
             * los terminales alcanzables y podía absorber componentes remotos
             * pertenecientes a la misma red.
             */
            return distanceFromSeed <=
                   ownershipRadius;
        }

        if (candidate.Kind is
                SchematicElectricalNodeKind.Wire or
                SchematicElectricalNodeKind.Junction)
        {
            return depth <= 2 &&
                   distanceFromSeed <=
                   ownershipRadius;
        }

        return false;
    }

    private static bool IsVisualMember(
        SchematicElectricalNode node)
    {
        return node.Kind is
            SchematicElectricalNodeKind.SymbolBody or
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal;
    }

    private static SchematicComponentTerminal CreateTerminal(
        SchematicElectricalGraph graph,
        SchematicElectricalNode terminalNode,
        int number,
        CancellationToken cancellationToken)
    {
        string[] netNames =
            FindConnectedNetNames(
                graph,
                terminalNode.Id,
                cancellationToken);

        return new SchematicComponentTerminal(
            terminalNode.Id,
            number,
            terminalNode.Bounds,
            terminalNode.CenterX,
            terminalNode.CenterY,
            netNames,
            terminalNode.Confidence);
    }

    /// <summary>
    /// Recorre la conectividad desde un terminal hasta localizar etiquetas de
    /// red. El recorrido puede atravesar conductores y uniones, pero se detiene
    /// al alcanzar otro cuerpo de símbolo.
    /// </summary>
    private static string[] FindConnectedNetNames(
        SchematicElectricalGraph graph,
        int startNodeId,
        CancellationToken cancellationToken)
    {
        var visited =
            new HashSet<int>
            {
                startNodeId
            };

        var names =
            new HashSet<string>(
                StringComparer.Ordinal);

        var queue =
            new Queue<(int NodeId, int Depth)>();

        queue.Enqueue(
            (startNodeId, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            (int currentId, int depth) =
                queue.Dequeue();

            if (depth >= MaximumNetworkDepth)
            {
                continue;
            }

            foreach (SchematicElectricalEdge edge in
                     graph.GetEdges(currentId))
            {
                if (edge.Confidence <
                    MinimumNetworkEdgeConfidence)
                {
                    continue;
                }

                int neighborId =
                    edge.GetOtherNodeId(
                        currentId);

                if (!visited.Add(neighborId) ||
                    !graph.TryGetNode(
                        neighborId,
                        out SchematicElectricalNode? neighbor) ||
                    neighbor is null)
                {
                    continue;
                }

                if (neighbor.Kind ==
                        SchematicElectricalNodeKind.NetLabel &&
                    !string.IsNullOrWhiteSpace(
                        neighbor.SemanticText))
                {
                    names.Add(
                        neighbor.SemanticText!);

                    continue;
                }

                if (neighbor.Kind ==
                    SchematicElectricalNodeKind.SymbolBody)
                {
                    continue;
                }

                if (neighbor.Kind is
                        SchematicElectricalNodeKind.Wire or
                        SchematicElectricalNodeKind.Pin or
                        SchematicElectricalNodeKind.Terminal or
                        SchematicElectricalNodeKind.Junction or
                        SchematicElectricalNodeKind.Ground or
                        SchematicElectricalNodeKind.PowerPort)
                {
                    queue.Enqueue(
                        (neighborId, depth + 1));
                }
            }
        }

        return names
            .OrderBy(
                name => name,
                StringComparer.Ordinal)
            .ToArray();
    }

    private static SchematicComponentKind ResolveKind(
        string reference)
    {
        string prefix =
            new(
                reference
                    .TakeWhile(
                        char.IsLetter)
                    .ToArray());

        return prefix.ToUpperInvariant() switch
        {
            "C" => SchematicComponentKind.Capacitor,
            "R" => SchematicComponentKind.Resistor,
            "L" => SchematicComponentKind.Inductor,
            "D" => SchematicComponentKind.Diode,
            "LED" => SchematicComponentKind.Led,
            "F" => SchematicComponentKind.Fuse,
            "FB" => SchematicComponentKind.FerriteBead,
            "Y" or "XTAL" => SchematicComponentKind.Crystal,
            "SW" => SchematicComponentKind.Switch,
            "K" => SchematicComponentKind.Relay,
            "Q" or "T" => SchematicComponentKind.Transistor,
            "U" or "IC" => SchematicComponentKind.IntegratedCircuit,
            "J" or "CN" or "CON" or "X" =>
                SchematicComponentKind.Connector,
            "TP" or "PP" or "P" =>
                SchematicComponentKind.TestPoint,
            _ => SchematicComponentKind.Unknown
        };
    }

    private static SchematicComponentOrientation ResolveOrientation(
        BoardGeometryBounds bounds,
        IReadOnlyList<SchematicElectricalNode> terminals)
    {
        if (terminals.Count >= 2)
        {
            double horizontalSpread =
                terminals.Max(node => node.CenterX) -
                terminals.Min(node => node.CenterX);

            double verticalSpread =
                terminals.Max(node => node.CenterY) -
                terminals.Min(node => node.CenterY);

            if (horizontalSpread >
                verticalSpread * 1.35D)
            {
                return SchematicComponentOrientation.Horizontal;
            }

            if (verticalSpread >
                horizontalSpread * 1.35D)
            {
                return SchematicComponentOrientation.Vertical;
            }
        }

        if (bounds.Width >
            bounds.Height * 1.35D)
        {
            return SchematicComponentOrientation.Horizontal;
        }

        if (bounds.Height >
            bounds.Width * 1.35D)
        {
            return SchematicComponentOrientation.Vertical;
        }

        return SchematicComponentOrientation.Compact;
    }

    private static double ResolveTerminalSortCoordinate(
        SchematicElectricalNode terminal,
        IReadOnlyList<SchematicElectricalNode> visualNodes)
    {
        BoardGeometryBounds bounds =
            CombineBounds(
                visualNodes.Select(
                    node => node.Bounds));

        bool horizontalComponent =
            bounds.Width >=
            bounds.Height;

        return horizontalComponent
            ? terminal.CenterX
            : terminal.CenterY;
    }

    private static double CalculateConfidence(
        SchematicReferenceAnchor anchor,
        IReadOnlyList<SchematicElectricalNode> visualNodes,
        IReadOnlyList<SchematicComponentTerminal> terminals)
    {
        double geometryConfidence =
            visualNodes.Count == 0
                ? anchor.SeedNode.Confidence
                : visualNodes.Average(
                    node =>
                        node.Confidence);

        double terminalScore =
            Math.Min(
                1D,
                terminals.Count / 2D);

        double bodyScore =
            anchor.SymbolBodyNode is null
                ? 0.55D
                : 1D;

        return Clamp01(
            anchor.Confidence * 0.42D +
            geometryConfidence * 0.28D +
            terminalScore * 0.20D +
            bodyScore * 0.10D);
    }

    private static double ResolveOwnershipRadius(
        BoardGeometryBounds seedBounds)
    {
        double dominantDimension =
            Math.Max(
                seedBounds.Width,
                seedBounds.Height);

        /*
         * El radio debe cubrir pines inmediatos, no una red completa. El límite
         * superior impide que un cuerpo grande convierta toda la página en su
         * zona de propiedad.
         */
        return Math.Clamp(
            dominantDimension * 1.75D,
            24D,
            180D);
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
            horizontalDistance * horizontalDistance +
            verticalDistance * verticalDistance);
    }

    private static BoardGeometryBounds CombineBounds(
        IEnumerable<BoardGeometryBounds> boundsCollection)
    {
        BoardGeometryBounds[] bounds =
            boundsCollection.ToArray();

        if (bounds.Length == 0)
        {
            return default;
        }

        double left =
            bounds.Min(item => item.Left);

        double top =
            bounds.Min(item => item.Top);

        double right =
            bounds.Max(item => item.Right);

        double bottom =
            bounds.Max(item => item.Bottom);

        int integerLeft =
            checked(
                (int)Math.Floor(left));

        int integerTop =
            checked(
                (int)Math.Floor(top));

        int integerRight =
            checked(
                (int)Math.Ceiling(right));

        int integerBottom =
            checked(
                (int)Math.Ceiling(bottom));

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