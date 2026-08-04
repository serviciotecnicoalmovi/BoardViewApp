using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Reconstruye relaciones cuerpo-pin y pin-conductor a partir de extremos
/// geométricos verificables.
/// </summary>
/// <remarks>
/// El conector no clasifica primitivas ni recorre el grafo. Su única
/// responsabilidad es añadir las aristas eléctricas que representan:
///
/// <code>
/// SymbolBody -> Pin/Terminal -> Wire/Junction
/// </code>
///
/// Para evitar asociaciones cruzadas, cada pin selecciona un único cuerpo
/// propietario. El extremo opuesto se considera extremo eléctrico exterior y
/// sólo puede conectarse con conductores que realmente lo alcancen.
/// </remarks>
public sealed class SchematicPinConnector
{
    /// <summary>
    /// Genera conexiones de pines que todavía no existen en la colección.
    /// </summary>
    public IReadOnlyList<SchematicElectricalEdge> Connect(
        IReadOnlyList<SchematicElectricalNode> nodes,
        IReadOnlyCollection<SchematicElectricalEdge> existingEdges,
        SchematicElectricalGraphBuilderOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(existingEdges);
        ArgumentNullException.ThrowIfNull(options);

        var existingPairs = existingEdges
            .Select(edge => (edge.FirstNodeId, edge.SecondNodeId))
            .ToHashSet();

        SchematicElectricalNode[] bodies = nodes
            .Where(node => node.Kind == SchematicElectricalNodeKind.SymbolBody)
            .OrderBy(node => node.Id)
            .ToArray();

        SchematicElectricalNode[] pins = nodes
            .Where(IsPinCandidate)
            .OrderBy(node => node.Id)
            .ToArray();

        SchematicElectricalNode[] conductors = nodes
            .Where(node =>
                node.Kind is
                    SchematicElectricalNodeKind.Wire or
                    SchematicElectricalNodeKind.Junction or
                    SchematicElectricalNodeKind.Pin or
                    SchematicElectricalNodeKind.Terminal)
            .OrderBy(node => node.Id)
            .ToArray();

        var result = new List<SchematicElectricalEdge>();

        foreach (SchematicElectricalNode pin in pins)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PinGeometry geometry = CreatePinGeometry(pin.Bounds);

            BodyCandidate? owner = FindOwningBody(
                pin,
                geometry,
                bodies,
                options);

            if (owner is null)
            {
                continue;
            }

            AddEdgeIfMissing(
                result,
                existingPairs,
                owner.Value.Body.Id,
                pin.Id,
                SchematicElectricalEdgeKind.EndpointContact,
                owner.Value.Confidence,
                owner.Value.DistancePixels,
                owner.Value.ContactX,
                owner.Value.ContactY);

            PinEndpoint externalEndpoint =
                owner.Value.BodyTouchesFirstEndpoint
                    ? geometry.Second
                    : geometry.First;

            ConductorCandidate[] matches = conductors
                .Where(conductor =>
                    conductor.Id != pin.Id &&
                    conductor.Id != owner.Value.Body.Id)
                .Select(conductor => EvaluateExternalConnection(
                    externalEndpoint,
                    pin,
                    conductor,
                    options))
                .Where(candidate => candidate.IsConnected)
                .OrderByDescending(candidate => candidate.Confidence)
                .ThenBy(candidate => candidate.DistancePixels)
                .ThenBy(candidate => candidate.Node.Id)
                .Take(options.MaximumConductorsPerPin)
                .ToArray();

            foreach (ConductorCandidate match in matches)
            {
                AddEdgeIfMissing(
                    result,
                    existingPairs,
                    pin.Id,
                    match.Node.Id,
                    SchematicElectricalEdgeKind.EndpointContact,
                    match.Confidence,
                    match.DistancePixels,
                    match.ContactX,
                    match.ContactY);
            }
        }

        return result
            .OrderBy(edge => edge.FirstNodeId)
            .ThenBy(edge => edge.SecondNodeId)
            .ToArray();
    }

    private static bool IsPinCandidate(SchematicElectricalNode node)
    {
        if (node.Kind is
            SchematicElectricalNodeKind.Pin or
            SchematicElectricalNodeKind.Terminal)
        {
            return true;
        }

        if (node.Kind != SchematicElectricalNodeKind.Wire)
        {
            return false;
        }

        return Math.Max(node.Bounds.Width, node.Bounds.Height) <= 58D;
    }

    private static BodyCandidate? FindOwningBody(
        SchematicElectricalNode pin,
        PinGeometry geometry,
        IReadOnlyList<SchematicElectricalNode> bodies,
        SchematicElectricalGraphBuilderOptions options)
    {
        BodyCandidate[] candidates = bodies
            .Where(body => body.Id != pin.Id)
            .Select(body => EvaluateBodyCandidate(
                pin,
                geometry,
                body,
                options))
            .Where(candidate => candidate.IsConnected)
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.DistancePixels)
            .ThenBy(candidate => candidate.Body.Id)
            .ToArray();

        return candidates.Length == 0
            ? null
            : candidates[0];
    }

    private static BodyCandidate EvaluateBodyCandidate(
        SchematicElectricalNode pin,
        PinGeometry geometry,
        SchematicElectricalNode body,
        SchematicElectricalGraphBuilderOptions options)
    {
        EndpointDistance firstDistance = DistanceEndpointToBounds(
            geometry.First,
            body.Bounds);

        EndpointDistance secondDistance = DistanceEndpointToBounds(
            geometry.Second,
            body.Bounds);

        bool firstTouches = firstDistance.DistancePixels <= secondDistance.DistancePixels;
        EndpointDistance best = firstTouches ? firstDistance : secondDistance;

        if (best.DistancePixels > options.MaximumPinBodyEndpointGapPixels)
        {
            return BodyCandidate.NotConnected(body);
        }

        double alignment = CalculatePerpendicularAlignment(
            pin.Bounds,
            body.Bounds,
            geometry.IsHorizontal);

        if (alignment < options.MinimumPinBodyAlignment)
        {
            return BodyCandidate.NotConnected(body);
        }

        double distanceScore = Clamp01(
            1D - best.DistancePixels /
            Math.Max(1D, options.MaximumPinBodyEndpointGapPixels));

        double confidence = Clamp01(
            options.PinBodyBaseConfidence +
            distanceScore * options.PinBodyDistanceWeight +
            alignment * options.PinBodyAlignmentWeight);

        return new BodyCandidate(
            true,
            body,
            confidence,
            best.DistancePixels,
            best.ContactX,
            best.ContactY,
            firstTouches);
    }

    private static ConductorCandidate EvaluateExternalConnection(
        PinEndpoint endpoint,
        SchematicElectricalNode pin,
        SchematicElectricalNode conductor,
        SchematicElectricalGraphBuilderOptions options)
    {
        EndpointDistance distance = DistanceEndpointToBounds(
            endpoint,
            conductor.Bounds);

        if (distance.DistancePixels > options.MaximumPinConductorEndpointGapPixels)
        {
            return ConductorCandidate.NotConnected(conductor);
        }

        bool endpointInsideExpandedBounds =
            endpoint.X >= conductor.Bounds.Left - options.PinEndpointContainmentTolerancePixels &&
            endpoint.X <= conductor.Bounds.Right + options.PinEndpointContainmentTolerancePixels &&
            endpoint.Y >= conductor.Bounds.Top - options.PinEndpointContainmentTolerancePixels &&
            endpoint.Y <= conductor.Bounds.Bottom + options.PinEndpointContainmentTolerancePixels;

        double axisAlignment = CalculateAxisAlignment(
            pin.Bounds,
            conductor.Bounds);

        if (!endpointInsideExpandedBounds &&
            axisAlignment < options.MinimumPinConductorAlignment)
        {
            return ConductorCandidate.NotConnected(conductor);
        }

        double distanceScore = Clamp01(
            1D - distance.DistancePixels /
            Math.Max(1D, options.MaximumPinConductorEndpointGapPixels));

        double roleBonus = conductor.Kind switch
        {
            SchematicElectricalNodeKind.Junction => 0.10D,
            SchematicElectricalNodeKind.Wire => 0.08D,
            SchematicElectricalNodeKind.Terminal => 0.04D,
            SchematicElectricalNodeKind.Pin => 0.03D,
            _ => 0D
        };

        double confidence = Clamp01(
            options.PinConductorBaseConfidence +
            distanceScore * options.PinConductorDistanceWeight +
            axisAlignment * options.PinConductorAlignmentWeight +
            roleBonus);

        return new ConductorCandidate(
            confidence >= options.MinimumEdgeConfidence,
            conductor,
            confidence,
            distance.DistancePixels,
            distance.ContactX,
            distance.ContactY);
    }

    private static PinGeometry CreatePinGeometry(BoardGeometryBounds bounds)
    {
        double centerX = bounds.Left + bounds.Width / 2D;
        double centerY = bounds.Top + bounds.Height / 2D;
        bool horizontal = bounds.Width >= bounds.Height;

        return horizontal
            ? new PinGeometry(
                new PinEndpoint(bounds.Left, centerY),
                new PinEndpoint(bounds.Right, centerY),
                true)
            : new PinGeometry(
                new PinEndpoint(centerX, bounds.Top),
                new PinEndpoint(centerX, bounds.Bottom),
                false);
    }

    private static EndpointDistance DistanceEndpointToBounds(
        PinEndpoint endpoint,
        BoardGeometryBounds bounds)
    {
        double contactX = Clamp(endpoint.X, bounds.Left, bounds.Right);
        double contactY = Clamp(endpoint.Y, bounds.Top, bounds.Bottom);
        double deltaX = endpoint.X - contactX;
        double deltaY = endpoint.Y - contactY;

        return new EndpointDistance(
            Math.Sqrt(deltaX * deltaX + deltaY * deltaY),
            contactX,
            contactY);
    }

    private static double CalculatePerpendicularAlignment(
        BoardGeometryBounds pin,
        BoardGeometryBounds body,
        bool pinIsHorizontal)
    {
        return pinIsHorizontal
            ? IntervalOverlapRatio(pin.Top, pin.Bottom, body.Top, body.Bottom)
            : IntervalOverlapRatio(pin.Left, pin.Right, body.Left, body.Right);
    }

    private static double CalculateAxisAlignment(
        BoardGeometryBounds first,
        BoardGeometryBounds second)
    {
        return Math.Max(
            IntervalOverlapRatio(first.Left, first.Right, second.Left, second.Right),
            IntervalOverlapRatio(first.Top, first.Bottom, second.Top, second.Bottom));
    }

    private static double IntervalOverlapRatio(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd)
    {
        double overlap = Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart);

        if (overlap <= 0D)
        {
            return 0D;
        }

        double firstLength = Math.Max(1D, firstEnd - firstStart);
        double secondLength = Math.Max(1D, secondEnd - secondStart);

        return Clamp01(overlap / Math.Min(firstLength, secondLength));
    }

    private static void AddEdgeIfMissing(
        ICollection<SchematicElectricalEdge> target,
        ISet<(int First, int Second)> existingPairs,
        int firstNodeId,
        int secondNodeId,
        SchematicElectricalEdgeKind kind,
        double confidence,
        double distancePixels,
        double contactX,
        double contactY)
    {
        int first = Math.Min(firstNodeId, secondNodeId);
        int second = Math.Max(firstNodeId, secondNodeId);

        if (!existingPairs.Add((first, second)))
        {
            return;
        }

        target.Add(new SchematicElectricalEdge(
            first,
            second,
            kind,
            Clamp01(confidence),
            Math.Max(0D, distancePixels),
            contactX,
            contactY));
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(maximum, value));

    private static double Clamp01(double value) => Clamp(value, 0D, 1D);

    private readonly record struct PinEndpoint(double X, double Y);

    private readonly record struct PinGeometry(
        PinEndpoint First,
        PinEndpoint Second,
        bool IsHorizontal);

    private readonly record struct EndpointDistance(
        double DistancePixels,
        double ContactX,
        double ContactY);

    private readonly record struct BodyCandidate(
        bool IsConnected,
        SchematicElectricalNode Body,
        double Confidence,
        double DistancePixels,
        double ContactX,
        double ContactY,
        bool BodyTouchesFirstEndpoint)
    {
        public static BodyCandidate NotConnected(SchematicElectricalNode body) =>
            new(false, body, 0D, double.MaxValue, 0D, 0D, false);
    }

    private readonly record struct ConductorCandidate(
        bool IsConnected,
        SchematicElectricalNode Node,
        double Confidence,
        double DistancePixels,
        double ContactX,
        double ContactY)
    {
        public static ConductorCandidate NotConnected(SchematicElectricalNode node) =>
            new(false, node, 0D, double.MaxValue, 0D, 0D);
    }
}