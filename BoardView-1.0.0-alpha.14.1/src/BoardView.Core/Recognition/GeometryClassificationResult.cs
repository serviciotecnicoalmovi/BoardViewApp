namespace BoardView.Core.Recognition;

/// <summary>Resultado inmutable del Geometry Classification Engine.</summary>
public sealed class GeometryClassificationResult
{
    /// <summary>Resultado vacío reutilizable.</summary>
    public static GeometryClassificationResult Empty { get; } = new([], TimeSpan.Zero);

    /// <summary>Inicializa el resultado de una clasificación geométrica.</summary>
    public GeometryClassificationResult(
        IReadOnlyList<ClassifiedGeometryPrimitive> primitives,
        TimeSpan elapsed)
    {
        Primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
        Elapsed = elapsed;
    }

    /// <summary>Primitivas clasificadas y ordenadas por posición.</summary>
    public IReadOnlyList<ClassifiedGeometryPrimitive> Primitives { get; }

    /// <summary>Tiempo empleado por el clasificador.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Obtiene las primitivas con evidencia conductiva suficiente.</summary>
    public IReadOnlyList<ClassifiedGeometryPrimitive> ConductiveCandidates =>
        Primitives.Where(static primitive => primitive.IsConductiveCandidate).ToArray();
}
