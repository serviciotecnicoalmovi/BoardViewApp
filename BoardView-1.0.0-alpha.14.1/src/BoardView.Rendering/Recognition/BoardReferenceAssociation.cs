using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Representa la asociación entre una referencia textual y un componente
/// geométrico indexado.
/// </summary>
public sealed record BoardReferenceAssociation
{
    /// <summary>
    /// Inicializa una asociación validada.
    /// </summary>
    public BoardReferenceAssociation(
        BoardReferenceCandidate candidate,
        BoardGeometryIndexedComponent component,
        double score,
        double distancePixels,
        BoardReferenceAssociationRule rule)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);

        ArgumentNullException.ThrowIfNull(
            component);

        if (!double.IsFinite(score) ||
            score < 0D ||
            score > 1D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score),
                score,
                "La puntuación debe estar entre cero y uno.");
        }

        if (!double.IsFinite(distancePixels) ||
            distancePixels < 0D)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distancePixels),
                distancePixels,
                "La distancia debe ser un número finito no negativo.");
        }

        Candidate = candidate;
        Component = component;
        Score = score;
        DistancePixels = distancePixels;
        Rule = rule;
    }

    /// <summary>
    /// Candidato textual asociado.
    /// </summary>
    public BoardReferenceCandidate Candidate { get; }

    /// <summary>
    /// Componente geométrico asociado.
    /// </summary>
    public BoardGeometryIndexedComponent Component { get; }

    /// <summary>
    /// Confianza final de la asociación.
    /// </summary>
    public double Score { get; }

    /// <summary>
    /// Distancia entre centros, expresada en píxeles del render.
    /// </summary>
    public double DistancePixels { get; }

    /// <summary>
    /// Regla principal que produjo la asociación.
    /// </summary>
    public BoardReferenceAssociationRule Rule { get; }

    /// <summary>
    /// Referencia normalizada.
    /// </summary>
    public string Reference =>
        Candidate.NormalizedReference;

    /// <summary>
    /// Identificador del componente.
    /// </summary>
    public int ComponentId =>
        Component.Id;
}

/// <summary>
/// Regla principal utilizada para asociar texto y geometría.
/// </summary>
public enum BoardReferenceAssociationRule
{
    /// <summary>
    /// No se determinó una regla específica.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// La asociación se produjo por distancia mínima.
    /// </summary>
    NearestComponent = 1,

    /// <summary>
    /// La referencia y el componente están alineados horizontalmente.
    /// </summary>
    HorizontalAlignment = 2,

    /// <summary>
    /// La referencia y el componente están alineados verticalmente.
    /// </summary>
    VerticalAlignment = 3,

    /// <summary>
    /// La referencia se encuentra dentro o intersecta el componente.
    /// </summary>
    BoundsIntersection = 4,

    /// <summary>
    /// La asociación recibió una prioridad adicional por tipo.
    /// </summary>
    SemanticPriority = 5
}
