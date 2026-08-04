namespace BoardView.Rendering.Recognition;

/// <summary>
/// Reconstruye la continuidad de conductores fragmentados sin crear conexiones
/// por simple cercanía.
/// </summary>
/// <remarks>
/// El ensamblador trabaja sobre los nodos ya clasificados como
/// <see cref="SchematicElectricalNodeKind.Wire"/>,
/// <see cref="SchematicElectricalNodeKind.Pin"/> o
/// <see cref="SchematicElectricalNodeKind.Terminal"/>.
///
/// No sustituye los nodos originales. Produce aristas topológicas adicionales
/// para que el grafo pueda recorrer una misma línea aunque el PDF la haya
/// dividido en varios fragmentos.
/// </remarks>
public sealed class SchematicWireAssembler
{
    /// <summary>
    /// Reconstruye las conexiones faltantes entre conductores.
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
                .OrderBy(node => node.Bounds.Left)
                .ThenBy(node => node.Bounds.Top)
                .ThenBy(node => node.Id)
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

        var recovered =
            new List<SchematicElectricalEdge>();

        double forwardWindow =
            Math.Max(
                options.MaximumCollinearGapPixels,
                Math.Max(
                    options.EndpointTolerancePixels,
                    options.EndpointToSegmentTolerancePixels)) +
            options.CollinearAxisTolerancePixels +
            options.MaximumWireThicknessPixels;

        for (int firstIndex = 0;
             firstIndex < conductors.Length;
             firstIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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
                    forwardWindow)
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

                WireConnection connection =
                    Evaluate(
                        first,
                        second,
                        options);

                if (!connection.IsConnected ||
                    connection.Confidence <
                    options.MinimumEdgeConfidence)
                {
                    continue;
                }

                recovered.Add(
                    new SchematicElectricalEdge(
                        firstId,
                        secondId,
                        connection.Kind,
                        connection.Confidence,
                        connection.DistancePixels,
                        connection.ContactX,
                        connection.ContactY));

                existingPairs.Add((firstId, secondId));
            }
        }

        return recovered
            .OrderBy(edge => edge.FirstNodeId)
            .ThenBy(edge => edge.SecondNodeId)
            .ToArray();
    }

    private static WireConnection Evaluate(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        Orientation firstOrientation =
            ResolveOrientation(first.Bounds);

        Orientation secondOrientation =
            ResolveOrientation(second.Bounds);

        /*
         * Primera regla: continuidad sobre el mismo eje. Esta regla fusiona
         * lógicamente los fragmentos que representan un solo conductor.
         */
        if (firstOrientation != Orientation.Compact &&
            firstOrientation == secondOrientation)
        {
            WireConnection collinear =
                EvaluateCollinear(
                    first,
                    second,
                    firstOrientation,
                    options);

            if (collinear.IsConnected)
            {
                return collinear;
            }
        }

        /*
         * Segunda regla: el extremo de un conductor cae sobre el interior del
         * otro. Representa T-junctions, pines tocando una red y segmentos
         * divididos en puntos de cambio de dirección.
         */
        WireConnection endpointToSegment =
            EvaluateEndpointToSegment(
                first,
                second,
                options);

        if (endpointToSegment.IsConnected)
        {
            return endpointToSegment;
        }

        /*
         * Tercera regla: contacto entre extremos. Se utiliza para esquinas y
         * pequeños huecos introducidos por la extracción geométrica.
         */
        return EvaluateEndpointContact(
            first,
            second,
            options);
    }

    private static WireConnection EvaluateCollinear(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        Orientation orientation,
        SchematicElectricalGraphBuilderOptions options)
    {
        double axisOffset;
        double gap;
        double contactX;
        double contactY;

        if (orientation == Orientation.Horizontal)
        {
            axisOffset =
                Math.Abs(
                    CenterY(first.Bounds) -
                    CenterY(second.Bounds));

            gap =
                IntervalGap(
                    first.Bounds.Left,
                    first.Bounds.Right,
                    second.Bounds.Left,
                    second.Bounds.Right);

            contactX =
                MidpointBetweenIntervals(
                    first.Bounds.Left,
                    first.Bounds.Right,
                    second.Bounds.Left,
                    second.Bounds.Right);

            contactY =
                (CenterY(first.Bounds) +
                 CenterY(second.Bounds)) / 2D;
        }
        else
        {
            axisOffset =
                Math.Abs(
                    CenterX(first.Bounds) -
                    CenterX(second.Bounds));

            gap =
                IntervalGap(
                    first.Bounds.Top,
                    first.Bounds.Bottom,
                    second.Bounds.Top,
                    second.Bounds.Bottom);

            contactX =
                (CenterX(first.Bounds) +
                 CenterX(second.Bounds)) / 2D;

            contactY =
                MidpointBetweenIntervals(
                    first.Bounds.Top,
                    first.Bounds.Bottom,
                    second.Bounds.Top,
                    second.Bounds.Bottom);
        }

        double thicknessTolerance =
            Math.Max(
                options.CollinearAxisTolerancePixels,
                Math.Max(
                    MinimumDimension(first.Bounds),
                    MinimumDimension(second.Bounds)) *
                0.75D);

        if (axisOffset > thicknessTolerance ||
            gap > options.MaximumCollinearGapPixels)
        {
            return WireConnection.None;
        }

        double axisScore =
            Clamp01(
                1D -
                axisOffset /
                Math.Max(1D, thicknessTolerance));

        double gapScore =
            Clamp01(
                1D -
                gap /
                Math.Max(
                    1D,
                    options.MaximumCollinearGapPixels));

        double confidence =
            Clamp01(
                options.CollinearBaseConfidence +
                axisScore *
                options.CollinearAxisWeight +
                gapScore *
                options.GapDistanceWeight +
                RoleBonus(first, second));

        return new WireConnection(
            true,
            SchematicElectricalEdgeKind.CollinearGap,
            confidence,
            gap,
            contactX,
            contactY);
    }

    private static WireConnection EvaluateEndpointToSegment(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        Projection firstProjection =
            BestEndpointProjection(
                first.Bounds,
                second.Bounds);

        Projection secondProjection =
            BestEndpointProjection(
                second.Bounds,
                first.Bounds);

        Projection best =
            firstProjection.DistancePixels <=
            secondProjection.DistancePixels
                ? firstProjection
                : secondProjection;

        double tolerance =
            Math.Max(
                options.EndpointToSegmentTolerancePixels,
                Math.Max(
                    MinimumDimension(first.Bounds),
                    MinimumDimension(second.Bounds)) *
                0.80D);

        if (best.DistancePixels > tolerance)
        {
            return WireConnection.None;
        }

        /*
         * Evita convertir un cruce interior de dos líneas en unión. Para una
         * conexión válida al menos uno de los puntos evaluados debe ser un
         * extremo del conductor fuente.
         */
        double distanceScore =
            Clamp01(
                1D -
                best.DistancePixels /
                Math.Max(1D, tolerance));

        double confidence =
            Clamp01(
                options.EndpointToSegmentBaseConfidence +
                distanceScore *
                options.EndpointDistanceWeight +
                RoleBonus(first, second));

        return new WireConnection(
            true,
            SchematicElectricalEdgeKind.EndpointContact,
            confidence,
            best.DistancePixels,
            best.ContactX,
            best.ContactY);
    }

    private static WireConnection EvaluateEndpointContact(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalGraphBuilderOptions options)
    {
        EndpointPair pair =
            ClosestEndpointPair(
                first.Bounds,
                second.Bounds);

        double tolerance =
            Math.Max(
                options.EndpointTolerancePixels,
                Math.Max(
                    MinimumDimension(first.Bounds),
                    MinimumDimension(second.Bounds)) *
                0.80D);

        if (pair.DistancePixels > tolerance)
        {
            return WireConnection.None;
        }

        double distanceScore =
            Clamp01(
                1D -
                pair.DistancePixels /
                Math.Max(1D, tolerance));

        double confidence =
            Clamp01(
                options.EndpointBaseConfidence +
                distanceScore *
                options.EndpointDistanceWeight +
                RoleBonus(first, second));

        return new WireConnection(
            true,
            SchematicElectricalEdgeKind.EndpointContact,
            confidence,
            pair.DistancePixels,
            (pair.FirstX + pair.SecondX) / 2D,
            (pair.FirstY + pair.SecondY) / 2D);
    }

    private static bool IsConductor(
        SchematicElectricalNode node)
    {
        return node.Kind is
            SchematicElectricalNodeKind.Wire or
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal;
    }

    private static double RoleBonus(
        SchematicElectricalNode first,
        SchematicElectricalNode second)
    {
        if (first.Kind == SchematicElectricalNodeKind.Wire &&
            second.Kind == SchematicElectricalNodeKind.Wire)
        {
            return 0.08D;
        }

        if (first.Kind == SchematicElectricalNodeKind.Pin ||
            second.Kind == SchematicElectricalNodeKind.Pin)
        {
            return 0.10D;
        }

        if (first.Kind == SchematicElectricalNodeKind.Terminal ||
            second.Kind == SchematicElectricalNodeKind.Terminal)
        {
            return 0.09D;
        }

        return 0.04D;
    }

    private static Projection BestEndpointProjection(
        BoardView.Rendering.Geometry.BoardGeometryBounds source,
        BoardView.Rendering.Geometry.BoardGeometryBounds target)
    {
        Projection best =
            new(
                double.MaxValue,
                0D,
                0D);

        foreach ((double x, double y) in Endpoints(source))
        {
            double targetX =
                Clamp(
                    x,
                    target.Left,
                    target.Right);

            double targetY =
                Clamp(
                    y,
                    target.Top,
                    target.Bottom);

            double distance =
                Distance(
                    x,
                    y,
                    targetX,
                    targetY);

            if (distance < best.DistancePixels)
            {
                best =
                    new Projection(
                        distance,
                        targetX,
                        targetY);
            }
        }

        return best;
    }

    private static EndpointPair ClosestEndpointPair(
        BoardView.Rendering.Geometry.BoardGeometryBounds first,
        BoardView.Rendering.Geometry.BoardGeometryBounds second)
    {
        EndpointPair best =
            new(
                double.MaxValue,
                0D,
                0D,
                0D,
                0D);

        foreach ((double firstX, double firstY) in Endpoints(first))
        {
            foreach ((double secondX, double secondY) in Endpoints(second))
            {
                double distance =
                    Distance(
                        firstX,
                        firstY,
                        secondX,
                        secondY);

                if (distance < best.DistancePixels)
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

    private static IReadOnlyList<(double X, double Y)> Endpoints(
        BoardView.Rendering.Geometry.BoardGeometryBounds bounds)
    {
        return ResolveOrientation(bounds) switch
        {
            Orientation.Horizontal =>
            [
                (bounds.Left, CenterY(bounds)),
                (bounds.Right, CenterY(bounds))
            ],

            Orientation.Vertical =>
            [
                (CenterX(bounds), bounds.Top),
                (CenterX(bounds), bounds.Bottom)
            ],

            _ =>
            [
                (bounds.Left, CenterY(bounds)),
                (bounds.Right, CenterY(bounds)),
                (CenterX(bounds), bounds.Top),
                (CenterX(bounds), bounds.Bottom)
            ]
        };
    }

    private static Orientation ResolveOrientation(
        BoardView.Rendering.Geometry.BoardGeometryBounds bounds)
    {
        if (bounds.Width >= bounds.Height * 1.50D)
        {
            return Orientation.Horizontal;
        }

        if (bounds.Height >= bounds.Width * 1.50D)
        {
            return Orientation.Vertical;
        }

        return Orientation.Compact;
    }

    private static double MinimumDimension(
        BoardView.Rendering.Geometry.BoardGeometryBounds bounds) =>
        Math.Max(
            1D,
            Math.Min(bounds.Width, bounds.Height));

    private static double CenterX(
        BoardView.Rendering.Geometry.BoardGeometryBounds bounds) =>
        bounds.Left + bounds.Width / 2D;

    private static double CenterY(
        BoardView.Rendering.Geometry.BoardGeometryBounds bounds) =>
        bounds.Top + bounds.Height / 2D;

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
            Math.Max(firstStart, secondStart);

        double overlapEnd =
            Math.Min(firstEnd, secondEnd);

        if (overlapStart <= overlapEnd)
        {
            return (overlapStart + overlapEnd) / 2D;
        }

        if (firstEnd < secondStart)
        {
            return (firstEnd + secondStart) / 2D;
        }

        return (secondEnd + firstStart) / 2D;
    }

    private static double Distance(
        double firstX,
        double firstY,
        double secondX,
        double secondY)
    {
        double deltaX =
            firstX - secondX;

        double deltaY =
            firstY - secondY;

        return Math.Sqrt(
            deltaX * deltaX +
            deltaY * deltaY);
    }

    private static double Clamp(
        double value,
        double minimum,
        double maximum) =>
        Math.Max(
            minimum,
            Math.Min(maximum, value));

    private static double Clamp01(double value) =>
        Clamp(value, 0D, 1D);

    private enum Orientation
    {
        Compact = 0,
        Horizontal = 1,
        Vertical = 2
    }

    private readonly record struct Projection(
        double DistancePixels,
        double ContactX,
        double ContactY);

    private readonly record struct EndpointPair(
        double DistancePixels,
        double FirstX,
        double FirstY,
        double SecondX,
        double SecondY);

    private readonly record struct WireConnection(
        bool IsConnected,
        SchematicElectricalEdgeKind Kind,
        double Confidence,
        double DistancePixels,
        double ContactX,
        double ContactY)
    {
        public static WireConnection None { get; } =
            new(
                false,
                SchematicElectricalEdgeKind.Unknown,
                0D,
                0D,
                0D,
                0D);
    }
}