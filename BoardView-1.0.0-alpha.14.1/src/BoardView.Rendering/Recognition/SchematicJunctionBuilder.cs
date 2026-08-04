using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Reconstruye uniones eléctricas explícitas e implícitas entre conductores.
/// </summary>
/// <remarks>
/// El constructor trabaja exclusivamente con nodos existentes. No inventa
/// geometría ni agrega nodos sintéticos.
///
/// Reconoce:
/// <list type="bullet">
/// <item>puntos compactos clasificados como <c>Junction</c>;</item>
/// <item>extremos de conductor que terminan sobre otro segmento;</item>
/// <item>varios extremos convergentes sobre una misma coordenada;</item>
/// <item>uniones en T aunque el PDF no dibuje un punto negro.</item>
/// </list>
///
/// No conecta cruces interior-interior sin punto de unión.
/// </remarks>
public sealed class SchematicJunctionBuilder
{
    /// <summary>
    /// Devuelve las aristas adicionales necesarias para representar las
    /// uniones eléctricas de la página.
    /// </summary>
    public IReadOnlyList<SchematicElectricalEdge> Build(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyList<SchematicElectricalEdge> existingEdges,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(existingEdges);
        ArgumentNullException.ThrowIfNull(options);

        var existingPairs =
            existingEdges
                .Select(edge =>
                    (
                        Math.Min(edge.FirstNodeId, edge.SecondNodeId),
                        Math.Max(edge.FirstNodeId, edge.SecondNodeId)))
                .ToHashSet();

        var result =
            new List<SchematicElectricalEdge>();

        SchematicElectricalNode[] conductors =
            nodes
                .Where(IsConductor)
                .OrderBy(node => node.Id)
                .ToArray();

        ConnectExplicitJunctionNodes(
            nodes,
            conductors,
            existingPairs,
            result,
            options,
            cancellationToken);

        ConnectImplicitTJunctions(
            conductors,
            existingPairs,
            result,
            options,
            cancellationToken);

        ConnectConvergentEndpoints(
            conductors,
            existingPairs,
            result,
            options,
            cancellationToken);

        return result
            .OrderBy(edge => edge.FirstNodeId)
            .ThenBy(edge => edge.SecondNodeId)
            .ThenByDescending(edge => edge.Confidence)
            .ToArray();
    }

    /// <summary>
    /// Conecta cada punto compacto de unión con todos los conductores que
    /// realmente alcanzan su centro.
    /// </summary>
    private static void ConnectExplicitJunctionNodes(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyList<SchematicElectricalNode> conductors,
        ISet<(int First, int Second)> existingPairs,
        ICollection<SchematicElectricalEdge> result,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        SchematicElectricalNode[] junctions =
            nodes
                .Where(node =>
                    node.Kind ==
                    SchematicElectricalNodeKind.Junction)
                .OrderBy(node => node.Id)
                .ToArray();

        foreach (SchematicElectricalNode junction in junctions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidates =
                new List<JunctionCandidate>();

            foreach (SchematicElectricalNode conductor in conductors)
            {
                if (junction.Id == conductor.Id)
                {
                    continue;
                }

                double distance =
                    DistancePointToBounds(
                        junction.CenterX,
                        junction.CenterY,
                        conductor.Bounds);

                double tolerance =
                    Math.Max(
                        options.JunctionConnectionTolerancePixels,
                        MinimumDimension(conductor.Bounds) * 0.85D);

                if (distance > tolerance)
                {
                    continue;
                }

                double endpointDistance =
                    DistanceToClosestEndpoint(
                        junction.CenterX,
                        junction.CenterY,
                        conductor.Bounds);

                bool centerInsideSegment =
                    PointTouchesSegmentInterior(
                        junction.CenterX,
                        junction.CenterY,
                        conductor.Bounds,
                        tolerance);

                double distanceScore =
                    Clamp01(
                        1D -
                        distance /
                        Math.Max(1D, tolerance));

                double endpointScore =
                    Clamp01(
                        1D -
                        endpointDistance /
                        Math.Max(
                            1D,
                            options.EndpointTolerancePixels));

                double confidence =
                    Clamp01(
                        options.JunctionBaseConfidence +
                        distanceScore *
                        options.JunctionDistanceWeight +
                        endpointScore * 0.12D +
                        (centerInsideSegment ? 0.08D : 0D));

                candidates.Add(
                    new JunctionCandidate(
                        conductor,
                        confidence,
                        distance));
            }

            /*
             * Un punto compacto aislado no debe crear una red. Exigimos dos
             * conductores, salvo que la relación única tenga confianza muy
             * alta y el nodo represente un terminal real.
             */
            JunctionCandidate[] selected =
                candidates
                    .OrderByDescending(candidate => candidate.Confidence)
                    .ThenBy(candidate => candidate.DistancePixels)
                    .ThenBy(candidate => candidate.Node.Id)
                    .Take(6)
                    .ToArray();

            if (selected.Length < 2)
            {
                continue;
            }

            foreach (JunctionCandidate candidate in selected)
            {
                AddEdge(
                    junction,
                    candidate.Node,
                    SchematicElectricalEdgeKind.EndpointContact,
                    candidate.Confidence,
                    candidate.DistancePixels,
                    junction.CenterX,
                    junction.CenterY,
                    existingPairs,
                    result,
                    options);
            }
        }
    }

    /// <summary>
    /// Detecta una unión en T cuando un extremo cae sobre el interior de otro
    /// conductor. Un cruce interior-interior queda deliberadamente excluido.
    /// </summary>
    private static void ConnectImplicitTJunctions(
        IReadOnlyList<SchematicElectricalNode> conductors,
        ISet<(int First, int Second)> existingPairs,
        ICollection<SchematicElectricalEdge> result,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        for (int firstIndex = 0;
             firstIndex < conductors.Count;
             firstIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SchematicElectricalNode first =
                conductors[firstIndex];

            for (int secondIndex = firstIndex + 1;
                 secondIndex < conductors.Count;
                 secondIndex++)
            {
                SchematicElectricalNode second =
                    conductors[secondIndex];

                int firstId =
                    Math.Min(first.Id, second.Id);

                int secondId =
                    Math.Max(first.Id, second.Id);

                if (existingPairs.Contains((firstId, secondId)))
                {
                    continue;
                }

                EndpointProjection firstToSecond =
                    FindEndpointProjection(
                        first.Bounds,
                        second.Bounds);

                EndpointProjection secondToFirst =
                    FindEndpointProjection(
                        second.Bounds,
                        first.Bounds);

                EndpointProjection best =
                    firstToSecond.DistancePixels <=
                    secondToFirst.DistancePixels
                        ? firstToSecond
                        : secondToFirst;

                double tolerance =
                    Math.Max(
                        options.EndpointToSegmentTolerancePixels,
                        Math.Max(
                            MinimumDimension(first.Bounds),
                            MinimumDimension(second.Bounds)) *
                        0.80D);

                if (best.DistancePixels > tolerance ||
                    !best.ProjectionInsideTarget)
                {
                    continue;
                }

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
                        0.10D);

                AddEdge(
                    first,
                    second,
                    SchematicElectricalEdgeKind.EndpointContact,
                    confidence,
                    best.DistancePixels,
                    best.ContactX,
                    best.ContactY,
                    existingPairs,
                    result,
                    options);
            }
        }
    }

    /// <summary>
    /// Une conductores cuyos extremos convergen en un mismo punto. Esta regla
    /// recupera esquinas y nodos fragmentados en tres o más trazos.
    /// </summary>
    private static void ConnectConvergentEndpoints(
        IReadOnlyList<SchematicElectricalNode> conductors,
        ISet<(int First, int Second)> existingPairs,
        ICollection<SchematicElectricalEdge> result,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken)
    {
        var endpointRecords =
            conductors
                .SelectMany(node =>
                    GetEndpoints(node.Bounds)
                        .Select(point =>
                            new EndpointRecord(
                                node,
                                point.X,
                                point.Y)))
                .OrderBy(record => record.X)
                .ThenBy(record => record.Y)
                .ThenBy(record => record.Node.Id)
                .ToArray();

        double tolerance =
            Math.Max(
                options.EndpointTolerancePixels,
                options.TouchTolerancePixels);

        for (int firstIndex = 0;
             firstIndex < endpointRecords.Length;
             firstIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EndpointRecord first =
                endpointRecords[firstIndex];

            for (int secondIndex = firstIndex + 1;
                 secondIndex < endpointRecords.Length;
                 secondIndex++)
            {
                EndpointRecord second =
                    endpointRecords[secondIndex];

                if (second.X >
                    first.X + tolerance)
                {
                    break;
                }

                if (first.Node.Id == second.Node.Id)
                {
                    continue;
                }

                double distance =
                    Distance(
                        first.X,
                        first.Y,
                        second.X,
                        second.Y);

                if (distance > tolerance)
                {
                    continue;
                }

                double distanceScore =
                    Clamp01(
                        1D -
                        distance /
                        Math.Max(1D, tolerance));

                double confidence =
                    Clamp01(
                        options.EndpointBaseConfidence +
                        distanceScore *
                        options.EndpointDistanceWeight +
                        0.08D);

                AddEdge(
                    first.Node,
                    second.Node,
                    SchematicElectricalEdgeKind.EndpointContact,
                    confidence,
                    distance,
                    (first.X + second.X) / 2D,
                    (first.Y + second.Y) / 2D,
                    existingPairs,
                    result,
                    options);
            }
        }
    }

    private static void AddEdge(
        SchematicElectricalNode first,
        SchematicElectricalNode second,
        SchematicElectricalEdgeKind kind,
        double confidence,
        double distancePixels,
        double contactX,
        double contactY,
        ISet<(int First, int Second)> existingPairs,
        ICollection<SchematicElectricalEdge> result,
        SchematicElectricalGraphBuilderOptions options)
    {
        int firstId =
            Math.Min(first.Id, second.Id);

        int secondId =
            Math.Max(first.Id, second.Id);

        if (firstId == secondId ||
            existingPairs.Contains((firstId, secondId)) ||
            confidence < options.MinimumEdgeConfidence)
        {
            return;
        }

        result.Add(
            new SchematicElectricalEdge(
                firstId,
                secondId,
                kind,
                Clamp01(confidence),
                Math.Max(0D, distancePixels),
                contactX,
                contactY));

        existingPairs.Add((firstId, secondId));
    }

    private static EndpointProjection FindEndpointProjection(
        BoardGeometryBounds source,
        BoardGeometryBounds target)
    {
        EndpointProjection best =
            new(
                double.MaxValue,
                0D,
                0D,
                false);

        foreach ((double x, double y) in GetEndpoints(source))
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

            bool inside =
                IsInteriorPoint(
                    projectedX,
                    projectedY,
                    target);

            if (distance < best.DistancePixels)
            {
                best =
                    new EndpointProjection(
                        distance,
                        projectedX,
                        projectedY,
                        inside);
            }
        }

        return best;
    }

    private static bool PointTouchesSegmentInterior(
        double x,
        double y,
        BoardGeometryBounds bounds,
        double tolerance)
    {
        Orientation orientation =
            ResolveOrientation(bounds);

        if (orientation == Orientation.Horizontal)
        {
            return x >= bounds.Left - tolerance &&
                   x <= bounds.Right + tolerance &&
                   Math.Abs(y - CenterY(bounds)) <= tolerance;
        }

        if (orientation == Orientation.Vertical)
        {
            return y >= bounds.Top - tolerance &&
                   y <= bounds.Bottom + tolerance &&
                   Math.Abs(x - CenterX(bounds)) <= tolerance;
        }

        return x >= bounds.Left - tolerance &&
               x <= bounds.Right + tolerance &&
               y >= bounds.Top - tolerance &&
               y <= bounds.Bottom + tolerance;
    }

    private static bool IsInteriorPoint(
        double x,
        double y,
        BoardGeometryBounds bounds)
    {
        const double epsilon = 0.25D;

        Orientation orientation =
            ResolveOrientation(bounds);

        if (orientation == Orientation.Horizontal)
        {
            return x > bounds.Left + epsilon &&
                   x < bounds.Right - epsilon;
        }

        if (orientation == Orientation.Vertical)
        {
            return y > bounds.Top + epsilon &&
                   y < bounds.Bottom - epsilon;
        }

        return x > bounds.Left + epsilon &&
               x < bounds.Right - epsilon &&
               y > bounds.Top + epsilon &&
               y < bounds.Bottom - epsilon;
    }

    private static double DistanceToClosestEndpoint(
        double x,
        double y,
        BoardGeometryBounds bounds)
    {
        return GetEndpoints(bounds)
            .Select(point =>
                Distance(
                    x,
                    y,
                    point.X,
                    point.Y))
            .Min();
    }

    private static IReadOnlyList<(double X, double Y)> GetEndpoints(
        BoardGeometryBounds bounds)
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
        BoardGeometryBounds bounds)
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

    private static bool IsConductor(
        SchematicElectricalNode node)
    {
        return node.Kind is
            SchematicElectricalNodeKind.Wire or
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal;
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

    private static double MinimumDimension(
        BoardGeometryBounds bounds) =>
        Math.Max(
            1D,
            Math.Min(bounds.Width, bounds.Height));

    private static double CenterX(
        BoardGeometryBounds bounds) =>
        bounds.Left + bounds.Width / 2D;

    private static double CenterY(
        BoardGeometryBounds bounds) =>
        bounds.Top + bounds.Height / 2D;

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

    private readonly record struct JunctionCandidate(
        SchematicElectricalNode Node,
        double Confidence,
        double DistancePixels);

    private readonly record struct EndpointRecord(
        SchematicElectricalNode Node,
        double X,
        double Y);

    private readonly record struct EndpointProjection(
        double DistancePixels,
        double ContactX,
        double ContactY,
        bool ProjectionInsideTarget);
}