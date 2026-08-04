using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Reconstruye segmentos eléctricos continuos a partir de primitivas lineales
/// fragmentadas.
/// </summary>
/// <remarks>
/// El ensamblador no crea geometría sintética. Agrupa lógicamente nodos Wire,
/// Pin y Terminal que comparten orientación y eje, y genera únicamente las
/// aristas mínimas necesarias para convertir cada grupo en una cadena
/// transitable.
///
/// Esta etapa se ejecuta antes de JunctionBuilder y PinConnector para que ambos
/// trabajen sobre una continuidad conductora ya reconstruida.
/// </remarks>
public sealed class SchematicNetSegmentAssembler
{
    private const double VerifiedSegmentConfidence = 0.86D;

    /// <summary>
    /// Ensambla los segmentos colineales de la página.
    /// </summary>
    public IReadOnlyList<SchematicElectricalEdge> Assemble(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyList<SchematicElectricalEdge> existingEdges,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(existingEdges);
        ArgumentNullException.ThrowIfNull(options);

        SchematicElectricalNode[] conductors =
            nodes
                .Where(IsConductor)
                .Where(node =>
                    ResolveOrientation(node.Bounds) !=
                    SegmentOrientation.Compact)
                .OrderBy(node => node.Id)
                .ToArray();

        if (conductors.Length < 2)
        {
            return Array.Empty<SchematicElectricalEdge>();
        }

        var existingPairs =
            existingEdges
                .Select(edge =>
                    (
                        Math.Min(edge.FirstNodeId, edge.SecondNodeId),
                        Math.Max(edge.FirstNodeId, edge.SecondNodeId)))
                .ToHashSet();

        var horizontal =
            conductors
                .Where(node =>
                    ResolveOrientation(node.Bounds) ==
                    SegmentOrientation.Horizontal)
                .ToArray();

        var vertical =
            conductors
                .Where(node =>
                    ResolveOrientation(node.Bounds) ==
                    SegmentOrientation.Vertical)
                .ToArray();

        var result =
            new List<SchematicElectricalEdge>();

        AssembleOrientation(
            horizontal,
            SegmentOrientation.Horizontal,
            existingPairs,
            result,
            options,
            cancellationToken);

        AssembleOrientation(
            vertical,
            SegmentOrientation.Vertical,
            existingPairs,
            result,
            options,
            cancellationToken);

        return result
            .OrderBy(edge => edge.FirstNodeId)
            .ThenBy(edge => edge.SecondNodeId)
            .ToArray();
    }

    private static void AssembleOrientation(
        IReadOnlyList<SchematicElectricalNode> nodes,
        SegmentOrientation orientation,
        ISet<(int First, int Second)> existingPairs,
        ICollection<SchematicElectricalEdge> result,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        if (nodes.Count < 2)
        {
            return;
        }

        /*
         * Los grupos se forman por eje. No se redondea a una cuadrícula fija:
         * se usa una agrupación incremental con tolerancia basada en espesor.
         */
        var groups =
            new List<AxisGroup>();

        foreach (SchematicElectricalNode node in
                 nodes.OrderBy(GetAxisCoordinate))
        {
            cancellationToken.ThrowIfCancellationRequested();

            double axis =
                GetAxisCoordinate(node);

            AxisGroup? group =
                groups
                    .Where(candidate =>
                        Math.Abs(candidate.Axis - axis) <=
                        ResolveAxisTolerance(
                            candidate,
                            node,
                            options))
                    .OrderBy(candidate =>
                        Math.Abs(candidate.Axis - axis))
                    .FirstOrDefault();

            if (group is null)
            {
                groups.Add(
                    new AxisGroup(
                        axis,
                        [node]));
            }
            else
            {
                group.Add(node);
            }
        }

        foreach (AxisGroup group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SchematicElectricalNode[] ordered =
                group.Nodes
                    .OrderBy(node =>
                        GetStart(node.Bounds, orientation))
                    .ThenBy(node =>
                        GetEnd(node.Bounds, orientation))
                    .ThenBy(node => node.Id)
                    .ToArray();

            if (ordered.Length < 2)
            {
                continue;
            }

            /*
             * Conecta cada fragmento con el siguiente fragmento compatible.
             * Se evita crear una clique completa: una cadena es suficiente para
             * que el BFS atraviese el segmento y reduce aristas redundantes.
             */
            for (int index = 0;
                 index < ordered.Length - 1;
                 index++)
            {
                SchematicElectricalNode current =
                    ordered[index];

                SchematicElectricalNode next =
                    FindNextCompatible(
                        ordered,
                        index,
                        orientation,
                        options);

                if (next.Id == current.Id)
                {
                    continue;
                }

                double gap =
                    IntervalGap(
                        GetStart(current.Bounds, orientation),
                        GetEnd(current.Bounds, orientation),
                        GetStart(next.Bounds, orientation),
                        GetEnd(next.Bounds, orientation));

                if (gap >
                    options.MaximumCollinearGapPixels)
                {
                    continue;
                }

                int firstId =
                    Math.Min(current.Id, next.Id);

                int secondId =
                    Math.Max(current.Id, next.Id);

                if (existingPairs.Contains((firstId, secondId)))
                {
                    continue;
                }

                double axisOffset =
                    Math.Abs(
                        GetAxisCoordinate(current) -
                        GetAxisCoordinate(next));

                double axisTolerance =
                    ResolveAxisTolerance(
                        current,
                        next,
                        options);

                double axisScore =
                    Clamp01(
                        1D -
                        axisOffset /
                        Math.Max(1D, axisTolerance));

                double gapScore =
                    Clamp01(
                        1D -
                        gap /
                        Math.Max(
                            1D,
                            options.MaximumCollinearGapPixels));

                double confidence =
                    Clamp01(
                        Math.Max(
                            VerifiedSegmentConfidence,
                            options.CollinearBaseConfidence +
                            axisScore *
                            options.CollinearAxisWeight +
                            gapScore *
                            options.GapDistanceWeight +
                            RoleBonus(current, next)));

                (double contactX, double contactY) =
                    ResolveContactPoint(
                        current.Bounds,
                        next.Bounds,
                        orientation);

                result.Add(
                    new SchematicElectricalEdge(
                        firstId,
                        secondId,
                        SchematicElectricalEdgeKind.CollinearGap,
                        confidence,
                        gap,
                        contactX,
                        contactY));

                existingPairs.Add((firstId, secondId));
            }
        }
    }

    private static SchematicElectricalNode FindNextCompatible(
        IReadOnlyList<SchematicElectricalNode> ordered,
        int currentIndex,
        SegmentOrientation orientation,
        SchematicElectricalGraphBuilderOptions options)
    {
        SchematicElectricalNode current =
            ordered[currentIndex];

        double currentEnd =
            GetEnd(
                current.Bounds,
                orientation);

        for (int index = currentIndex + 1;
             index < ordered.Count;
             index++)
        {
            SchematicElectricalNode candidate =
                ordered[index];

            double gap =
                Math.Max(
                    0D,
                    GetStart(
                        candidate.Bounds,
                        orientation) -
                    currentEnd);

            if (gap >
                options.MaximumCollinearGapPixels)
            {
                break;
            }

            double axisOffset =
                Math.Abs(
                    GetAxisCoordinate(current) -
                    GetAxisCoordinate(candidate));

            if (axisOffset <=
                ResolveAxisTolerance(
                    current,
                    candidate,
                    options))
            {
                return candidate;
            }
        }

        return current;
    }

    private static double ResolveAxisTolerance(
        AxisGroup group,
        SchematicElectricalNode node,
        SchematicElectricalGraphBuilderOptions options)
    {
        double groupThickness =
            group.Nodes.Count == 0
                ? MinimumDimension(node.Bounds)
                : group.Nodes
                    .Select(item =>
                        MinimumDimension(item.Bounds))
                    .Average();

        return Math.Max(
            options.CollinearAxisTolerancePixels,
            Math.Max(
                groupThickness,
                MinimumDimension(node.Bounds)) *
            0.80D);
    }

    private static double ResolveAxisTolerance(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        return Math.Max(
            options.CollinearAxisTolerancePixels,
            Math.Max(
                MinimumDimension(first.Bounds),
                MinimumDimension(second.Bounds)) *
            0.80D);
    }

    private static (double X, double Y) ResolveContactPoint(
        BoardGeometryBounds first,
        BoardGeometryBounds second,
        SegmentOrientation orientation)
    {
        if (orientation ==
            SegmentOrientation.Horizontal)
        {
            return (
                MidpointBetweenIntervals(
                    first.Left,
                    first.Right,
                    second.Left,
                    second.Right),
                (CenterY(first) +
                 CenterY(second)) / 2D);
        }

        return (
            (CenterX(first) +
             CenterX(second)) / 2D,
            MidpointBetweenIntervals(
                first.Top,
                first.Bottom,
                second.Top,
                second.Bottom));
    }

    private static bool IsConductor(
        SchematicElectricalNode node)
    {
        return node.Kind is
            SchematicElectricalNodeKind.Wire or
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal;
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

    private static double GetAxisCoordinate(
        SchematicElectricalNode node)
    {
        return ResolveOrientation(node.Bounds) switch
        {
            SegmentOrientation.Horizontal =>
                node.CenterY,

            SegmentOrientation.Vertical =>
                node.CenterX,

            _ =>
                0D
        };
    }

    private static double GetStart(
        BoardGeometryBounds bounds,
        SegmentOrientation orientation)
    {
        return orientation ==
               SegmentOrientation.Horizontal
            ? bounds.Left
            : bounds.Top;
    }

    private static double GetEnd(
        BoardGeometryBounds bounds,
        SegmentOrientation orientation)
    {
        return orientation ==
               SegmentOrientation.Horizontal
            ? bounds.Right
            : bounds.Bottom;
    }

    private static double RoleBonus(
        SchematicElectricalNode first,
        SchematicElectricalNode second)
    {
        if (first.Kind ==
                SchematicElectricalNodeKind.Pin ||
            second.Kind ==
                SchematicElectricalNodeKind.Pin)
        {
            return 0.08D;
        }

        if (first.Kind ==
                SchematicElectricalNodeKind.Terminal ||
            second.Kind ==
                SchematicElectricalNodeKind.Terminal)
        {
            return 0.07D;
        }

        return 0.05D;
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

    private static double MidpointBetweenIntervals(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd)
    {
        double overlapStart =
            Math.Max(
                firstStart,
                secondStart);

        double overlapEnd =
            Math.Min(
                firstEnd,
                secondEnd);

        if (overlapStart <= overlapEnd)
        {
            return
                (overlapStart + overlapEnd) / 2D;
        }

        if (firstEnd < secondStart)
        {
            return
                (firstEnd + secondStart) / 2D;
        }

        return
            (secondEnd + firstStart) / 2D;
    }

    private static double MinimumDimension(
        BoardGeometryBounds bounds)
    {
        return Math.Max(
            1D,
            Math.Min(
                bounds.Width,
                bounds.Height));
    }

    private static double CenterX(
        BoardGeometryBounds bounds)
    {
        return
            bounds.Left +
            bounds.Width / 2D;
    }

    private static double CenterY(
        BoardGeometryBounds bounds)
    {
        return
            bounds.Top +
            bounds.Height / 2D;
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

    private enum SegmentOrientation
    {
        Compact = 0,
        Horizontal = 1,
        Vertical = 2
    }

    private sealed class AxisGroup
    {
        private double axisSum;

        public AxisGroup(
            double initialAxis,
            IEnumerable<SchematicElectricalNode> nodes)
        {
            Nodes =
                nodes.ToList();

            axisSum =
                initialAxis *
                Nodes.Count;
        }

        public List<SchematicElectricalNode> Nodes { get; }

        public double Axis =>
            Nodes.Count == 0
                ? 0D
                : axisSum /
                  Nodes.Count;

        public void Add(
            SchematicElectricalNode node)
        {
            Nodes.Add(node);
            axisSum +=
                GetAxisCoordinate(node);
        }
    }
}