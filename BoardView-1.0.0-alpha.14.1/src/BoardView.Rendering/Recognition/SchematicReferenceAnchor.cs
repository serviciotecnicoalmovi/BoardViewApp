using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Anclaje semántico de una referencia textual dentro del grafo eléctrico.
/// </summary>
public sealed record SchematicReferenceAnchor(
    BoardReferenceCandidate Candidate,
    SchematicElectricalNode SeedNode,
    SchematicElectricalNode? SymbolBodyNode,
    double Confidence,
    double DistancePixels,
    SchematicReferenceAnchorRule Rule)
{
    public string Reference => Candidate.NormalizedReference;
    public BoardGeometryBounds TextBounds => Candidate.Bounds;
    public BoardGeometryBounds SeedBounds => SeedNode.Bounds;
    public bool HasSymbolBody => SymbolBodyNode is not null;
}

public enum SchematicReferenceAnchorRule
{
    Unknown = 0,
    BodyBelowReference = 1,
    PinBelowReference = 2,
    TerminalBelowReference = 3,
    LateralSymbol = 4,
    ConnectedBody = 5,
    TestPoint = 6,
    GraphTopology = 7
}

/// <summary>
/// Resultado inmutable del anclaje semántico.
/// </summary>
public sealed class SchematicReferenceAnchorResult
{
    private readonly IReadOnlyDictionary<int, SchematicReferenceAnchor> byCandidateId;
    private readonly IReadOnlyDictionary<string, SchematicReferenceAnchor> byReference;
    private readonly IReadOnlyCollection<SchematicReferenceAnchor> anchors;

    public static SchematicReferenceAnchorResult Empty { get; } =
        new(Array.Empty<SchematicReferenceAnchor>());

    public SchematicReferenceAnchorResult(
        IEnumerable<SchematicReferenceAnchor> anchors)
    {
        ArgumentNullException.ThrowIfNull(anchors);

        SchematicReferenceAnchor[] array =
            anchors.OrderBy(anchor => anchor.Candidate.Id).ToArray();

        byCandidateId =
            array.ToDictionary(anchor => anchor.Candidate.Id);

        byReference =
            array
                .GroupBy(anchor => anchor.Reference, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(anchor => anchor.Confidence)
                    .ThenBy(anchor => anchor.DistancePixels)
                    .First())
                .ToDictionary(anchor => anchor.Reference, StringComparer.OrdinalIgnoreCase);

        this.anchors = array;
    }

    public int Count => anchors.Count;
    public IReadOnlyCollection<SchematicReferenceAnchor> Anchors => anchors;

    public bool TryGetByCandidateId(
        int candidateId,
        out SchematicReferenceAnchor? anchor) =>
        byCandidateId.TryGetValue(candidateId, out anchor);

    public bool TryGetByReference(
        string reference,
        out SchematicReferenceAnchor? anchor)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            anchor = null;
            return false;
        }

        return byReference.TryGetValue(reference.Trim(), out anchor);
    }
}
