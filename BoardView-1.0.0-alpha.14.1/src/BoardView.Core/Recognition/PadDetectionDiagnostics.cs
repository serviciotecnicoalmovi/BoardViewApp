namespace BoardView.Core.Recognition;

/// <summary>
/// Métricas completas de una ejecución del detector. Permite comprobar qué etapa descarta
/// cada primitiva sin modificar el modelo ni depender de un depurador.
/// </summary>
public sealed class PadDetectionDiagnostics
{
    /// <summary>Diagnóstico vacío reutilizable.</summary>
    public static PadDetectionDiagnostics Empty { get; } = new(
        0, 0, 0D, 0D, [], new Dictionary<GeometryPrimitiveKind, int>());

    /// <summary>Inicializa las métricas de detección.</summary>
    public PadDetectionDiagnostics(
        int sourceElementCount,
        int classifiedPrimitiveCount,
        double minimumAcceptedSizeMillimeters,
        double maximumAcceptedSizeMillimeters,
        IReadOnlyList<PadCandidateDiagnostic> candidates,
        IReadOnlyDictionary<GeometryPrimitiveKind, int> primitiveCounts)
    {
        SourceElementCount = sourceElementCount;
        ClassifiedPrimitiveCount = classifiedPrimitiveCount;
        MinimumAcceptedSizeMillimeters = minimumAcceptedSizeMillimeters;
        MaximumAcceptedSizeMillimeters = maximumAcceptedSizeMillimeters;
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        PrimitiveCounts = primitiveCounts ?? throw new ArgumentNullException(nameof(primitiveCounts));
    }

    /// <summary>Cantidad total de elementos recibidos desde <c>BoardDocument</c>.</summary>
    public int SourceElementCount { get; }

    /// <summary>Cantidad de elementos convertidos en primitivas clasificadas.</summary>
    public int ClassifiedPrimitiveCount { get; }

    /// <summary>Tamaño mínimo aplicado, expresado siempre en milímetros.</summary>
    public double MinimumAcceptedSizeMillimeters { get; }

    /// <summary>Tamaño máximo aplicado, expresado siempre en milímetros.</summary>
    public double MaximumAcceptedSizeMillimeters { get; }

    /// <summary>Resultado individual de cada primitiva evaluada como pad.</summary>
    public IReadOnlyList<PadCandidateDiagnostic> Candidates { get; }

    /// <summary>Distribución de primitivas por clase geométrica.</summary>
    public IReadOnlyDictionary<GeometryPrimitiveKind, int> PrimitiveCounts { get; }

    /// <summary>Cantidad de primitivas que llegaron a la evaluación de pad.</summary>
    public int CandidateCount => Candidates.Count;

    /// <summary>Cantidad aceptada antes de eliminar duplicados.</summary>
    public int AcceptedBeforeDeduplication => Candidates.Count(static item => item.Accepted);

    /// <summary>Obtiene la cantidad descartada por un motivo concreto.</summary>
    public int CountRejected(PadCandidateRejectionReason reason) =>
        Candidates.Count(item => !item.Accepted && item.RejectionReason == reason);

    /// <summary>Resumen compacto para la barra de estado.</summary>
    public string Summary =>
        $"{ClassifiedPrimitiveCount:N0} primitivas · {CandidateCount:N0} candidatos · " +
        $"{AcceptedBeforeDeduplication:N0} aceptados";

    /// <summary>Resumen detallado para registros y tooltips.</summary>
    public string DetailedSummary =>
        $"Elementos={SourceElementCount:N0}; primitivas={ClassifiedPrimitiveCount:N0}; " +
        $"candidatos={CandidateCount:N0}; aceptados={AcceptedBeforeDeduplication:N0}; " +
        $"pequeños={CountRejected(PadCandidateRejectionReason.TooSmall):N0}; " +
        $"grandes={CountRejected(PadCandidateRejectionReason.TooLarge):N0}; " +
        $"proporción={CountRejected(PadCandidateRejectionReason.InvalidAspectRatio):N0}; " +
        $"contorno={CountRejected(PadCandidateRejectionReason.OutlineWithoutPattern):N0}; " +
        $"confianza={CountRejected(PadCandidateRejectionReason.LowConfidence):N0}; " +
        $"rango={MinimumAcceptedSizeMillimeters:0.###}–{MaximumAcceptedSizeMillimeters:0.###} mm";
}
