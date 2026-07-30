namespace BoardView.SemanticKernel;

/// <summary>Resultado inmutable del análisis semántico de un documento normalizado.</summary>
public sealed class SemanticAnalysisResult
{
    /// <summary>Resultado vacío reutilizable.</summary>
    public static SemanticAnalysisResult Empty { get; } = new([], TimeSpan.Zero);

    /// <summary>Inicializa una instantánea semántica.</summary>
    public SemanticAnalysisResult(IReadOnlyList<SemanticPrimitive> primitives, TimeSpan elapsed)
    {
        Primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
        Elapsed = elapsed;
        Counts = primitives
            .GroupBy(static item => item.Semantic)
            .ToDictionary(static group => group.Key, static group => group.Count());
    }

    /// <summary>Primitivas clasificadas en el mismo orden estable de la base geométrica.</summary>
    public IReadOnlyList<SemanticPrimitive> Primitives { get; }

    /// <summary>Cantidades agrupadas por significado.</summary>
    public IReadOnlyDictionary<PrimitiveSemantic, int> Counts { get; }

    /// <summary>Tiempo total empleado en el análisis.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Obtiene la cantidad de una semántica sin lanzar excepciones.</summary>
    public int Count(PrimitiveSemantic semantic) => Counts.TryGetValue(semantic, out int value) ? value : 0;

    /// <summary>Resumen compacto para registros e interfaz.</summary>
    public string Summary =>
        $"{Count(PrimitiveSemantic.Pad):N0} pads · {Count(PrimitiveSemantic.Via):N0} vías · " +
        $"{Count(PrimitiveSemantic.Hole):N0} agujeros · {Count(PrimitiveSemantic.ComponentBody):N0} cuerpos · " +
        $"{Count(PrimitiveSemantic.BoardOutline):N0} contornos · {Count(PrimitiveSemantic.Unknown):N0} desconocidos";
}
