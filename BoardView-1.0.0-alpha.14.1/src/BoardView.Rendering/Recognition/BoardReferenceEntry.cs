using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Representa una referencia electrónica resuelta y enlazada con un componente
/// geométrico indexado.
/// </summary>
public sealed record BoardReferenceEntry
{
    /// <summary>
    /// Inicializa una entrada del índice de referencias.
    /// </summary>
    public BoardReferenceEntry(
        string reference,
        BoardGeometryIndexedComponent component,
        double confidence,
        double distancePixels,
        BoardReferenceAssociationRule associationRule,
        BoardReferenceCandidate candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            reference);

        ArgumentNullException.ThrowIfNull(
            component);

        ArgumentNullException.ThrowIfNull(
            candidate);

        if (!double.IsFinite(confidence) ||
            confidence < 0D ||
            confidence > 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                confidence,
                "La confianza debe estar entre cero y uno.");
        }

        if (!double.IsFinite(distancePixels) ||
            distancePixels < 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distancePixels),
                distancePixels,
                "La distancia debe ser finita y no negativa.");
        }

        Reference =
            BoardReferenceCandidate.NormalizeReference(
                reference);

        Component =
            component;

        Confidence =
            confidence;

        DistancePixels =
            distancePixels;

        AssociationRule =
            associationRule;

        Candidate =
            candidate;
    }

    /// <summary>
    /// Referencia electrónica normalizada.
    /// </summary>
    public string Reference { get; }

    /// <summary>
    /// Componente geométrico asociado.
    /// </summary>
    public BoardGeometryIndexedComponent Component { get; }

    /// <summary>
    /// Candidato textual que originó la entrada.
    /// </summary>
    public BoardReferenceCandidate Candidate { get; }

    /// <summary>
    /// Identificador del componente asociado.
    /// </summary>
    public int ComponentId =>
        Component.Id;

    /// <summary>
    /// Tipo geométrico clasificado.
    /// </summary>
    public BoardGeometryComponentType ComponentType =>
        Component.Type;

    /// <summary>
    /// Límites del componente en coordenadas del render original.
    /// </summary>
    public BoardGeometryBounds Bounds =>
        Component.Bounds;

    /// <summary>
    /// Centro horizontal del componente.
    /// </summary>
    public double CenterX =>
        Component.CenterX;

    /// <summary>
    /// Centro vertical del componente.
    /// </summary>
    public double CenterY =>
        Component.CenterY;

    /// <summary>
    /// Confianza final de la asociación.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    /// Distancia entre el texto y el componente.
    /// </summary>
    public double DistancePixels { get; }

    /// <summary>
    /// Regla principal utilizada durante la asociación.
    /// </summary>
    public BoardReferenceAssociationRule AssociationRule { get; }

    /// <summary>
    /// Número de página cero-basado.
    /// </summary>
    public int PageIndex =>
        Candidate.PageIndex;

    /// <summary>
    /// Prefijo alfabético de la referencia.
    /// </summary>
    public string Prefix =>
        Candidate.Prefix ??
        string.Empty;

    /// <summary>
    /// Crea una entrada desde una asociación validada.
    /// </summary>
    public static BoardReferenceEntry FromAssociation(
        BoardReferenceAssociation association)
    {
        ArgumentNullException.ThrowIfNull(
            association);

        return new BoardReferenceEntry(
            association.Reference,
            association.Component,
            association.Score,
            association.DistancePixels,
            association.Rule,
            association.Candidate);
    }
}
