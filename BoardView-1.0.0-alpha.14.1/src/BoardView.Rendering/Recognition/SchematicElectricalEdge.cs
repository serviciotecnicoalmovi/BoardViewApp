namespace BoardView.Rendering.Recognition;

/// <summary>
/// Tipo de relación geométrica que originó una conexión eléctrica.
/// </summary>
public enum SchematicElectricalEdgeKind
{
    Unknown = 0,
    BoundsIntersection = 1,
    BoundsTouch = 2,
    EndpointContact = 3,
    CollinearGap = 4,
    Proximity = 5
}

/// <summary>
/// Arista no dirigida entre dos nodos del grafo eléctrico.
/// </summary>
public sealed record SchematicElectricalEdge
{
    public SchematicElectricalEdge(
        int firstNodeId,
        int secondNodeId,
        SchematicElectricalEdgeKind kind,
        double confidence,
        double distancePixels,
        double contactX,
        double contactY)
    {
        if (firstNodeId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstNodeId));
        }

        if (secondNodeId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(secondNodeId));
        }

        if (firstNodeId == secondNodeId)
        {
            throw new ArgumentException(
                "Una arista debe conectar dos nodos diferentes.");
        }

        if (!double.IsFinite(confidence) ||
            confidence < 0D ||
            confidence > 1D)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        if (!double.IsFinite(distancePixels) ||
            distancePixels < 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(distancePixels));
        }

        if (!double.IsFinite(contactX))
        {
            throw new ArgumentOutOfRangeException(nameof(contactX));
        }

        if (!double.IsFinite(contactY))
        {
            throw new ArgumentOutOfRangeException(nameof(contactY));
        }

        FirstNodeId =
            Math.Min(firstNodeId, secondNodeId);

        SecondNodeId =
            Math.Max(firstNodeId, secondNodeId);

        Kind = kind;
        Confidence = confidence;
        DistancePixels = distancePixels;
        ContactX = contactX;
        ContactY = contactY;
    }

    public int FirstNodeId { get; }

    public int SecondNodeId { get; }

    public SchematicElectricalEdgeKind Kind { get; }

    public double Confidence { get; }

    public double DistancePixels { get; }

    public double ContactX { get; }

    public double ContactY { get; }

    public bool Connects(int nodeId) =>
        FirstNodeId == nodeId ||
        SecondNodeId == nodeId;

    public int GetOtherNodeId(int nodeId)
    {
        if (FirstNodeId == nodeId)
        {
            return SecondNodeId;
        }

        if (SecondNodeId == nodeId)
        {
            return FirstNodeId;
        }

        throw new ArgumentException(
            "El nodo indicado no pertenece a esta arista.",
            nameof(nodeId));
    }
}
