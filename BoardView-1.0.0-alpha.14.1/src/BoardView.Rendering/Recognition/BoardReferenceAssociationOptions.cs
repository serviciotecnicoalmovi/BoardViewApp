using BoardView.Rendering.Geometry;

namespace BoardView.Rendering.Recognition;

/// <summary>
/// Configuración del motor de asociación entre referencias y componentes.
/// </summary>
public sealed record BoardReferenceAssociationOptions
{
    /// <summary>
    /// Configuración predeterminada.
    /// </summary>
    public static BoardReferenceAssociationOptions Default { get; } =
        new();

    /// <summary>
    /// Distancia máxima entre texto y componente.
    /// </summary>
    public double MaximumDistancePixels { get; init; } =
        160D;

    /// <summary>
    /// Confianza mínima aceptada para un candidato textual.
    /// </summary>
    public double MinimumCandidateConfidence { get; init; } =
        0.50D;

    /// <summary>
    /// Confianza mínima del componente geométrico.
    /// </summary>
    public double MinimumComponentConfidence { get; init; } =
        0.50D;

    /// <summary>
    /// Puntuación mínima para aceptar una asociación.
    /// </summary>
    public double MinimumAssociationScore { get; init; } =
        0.55D;

    /// <summary>
    /// Cantidad máxima de componentes evaluados por candidato.
    /// </summary>
    public int MaximumComponentsPerCandidate { get; init; } =
        24;

    /// <summary>
    /// Permite que varias referencias apunten al mismo componente.
    /// </summary>
    public bool AllowMultipleReferencesPerComponent { get; init; }

    /// <summary>
    /// Excluye geometría clasificada como ruido.
    /// </summary>
    public bool ExcludeNoise { get; init; } =
        true;

    /// <summary>
    /// Excluye texto y serigrafía como componentes de destino.
    /// </summary>
    public bool ExcludeTextLikeComponents { get; init; } =
        true;

    /// <summary>
    /// Peso de la cercanía espacial.
    /// </summary>
    public double DistanceWeight { get; init; } =
        0.45D;

    /// <summary>
    /// Peso de la alineación geométrica.
    /// </summary>
    public double AlignmentWeight { get; init; } =
        0.20D;

    /// <summary>
    /// Peso de la confianza del candidato.
    /// </summary>
    public double CandidateConfidenceWeight { get; init; } =
        0.15D;

    /// <summary>
    /// Peso de la confianza del componente.
    /// </summary>
    public double ComponentConfidenceWeight { get; init; } =
        0.10D;

    /// <summary>
    /// Peso de la prioridad semántica.
    /// </summary>
    public double SemanticWeight { get; init; } =
        0.10D;

    /// <summary>
    /// Tipos explícitamente permitidos. Null permite cualquier tipo no excluido.
    /// </summary>
    public IReadOnlySet<BoardGeometryComponentType>? AllowedComponentTypes
    {
        get;
        init;
    }

    /// <summary>
    /// Valida la configuración.
    /// </summary>
    public void Validate()
    {
        ValidatePositiveFinite(
            MaximumDistancePixels,
            nameof(MaximumDistancePixels));

        ValidateFraction(
            MinimumCandidateConfidence,
            nameof(MinimumCandidateConfidence));

        ValidateFraction(
            MinimumComponentConfidence,
            nameof(MinimumComponentConfidence));

        ValidateFraction(
            MinimumAssociationScore,
            nameof(MinimumAssociationScore));

        if (MaximumComponentsPerCandidate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumComponentsPerCandidate),
                MaximumComponentsPerCandidate,
                "La cantidad máxima debe ser mayor que cero.");
        }

        ValidateFraction(
            DistanceWeight,
            nameof(DistanceWeight));

        ValidateFraction(
            AlignmentWeight,
            nameof(AlignmentWeight));

        ValidateFraction(
            CandidateConfidenceWeight,
            nameof(CandidateConfidenceWeight));

        ValidateFraction(
            ComponentConfidenceWeight,
            nameof(ComponentConfidenceWeight));

        ValidateFraction(
            SemanticWeight,
            nameof(SemanticWeight));

        double totalWeight =
            DistanceWeight +
            AlignmentWeight +
            CandidateConfidenceWeight +
            ComponentConfidenceWeight +
            SemanticWeight;

        if (totalWeight <= 0D)
        {
            throw new InvalidOperationException(
                "La suma de pesos debe ser mayor que cero.");
        }
    }

    private static void ValidateFraction(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value < 0D ||
            value > 1D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "El valor debe estar entre cero y uno.");
        }
    }

    private static void ValidatePositiveFinite(
        double value,
        string parameterName)
    {
        if (!double.IsFinite(value) ||
            value <= 0D)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "El valor debe ser finito y mayor que cero.");
        }
    }
}
