using BoardView.Core.GeometryDatabase;

namespace BoardView.Core.Recognition;

/// <summary>Resultado inmutable de una ejecución del Pad Detection Engine.</summary>
public sealed class RecognitionResult
{
    /// <summary>Resultado vacío reutilizable.</summary>
    public static RecognitionResult Empty { get; } = new(
        [], [], [], [], GeometryDatabaseSnapshot.Empty, GeometryClassificationResult.Empty, PadDetectionDiagnostics.Empty, TimeSpan.Zero);

    /// <summary>Inicializa un resultado conservando compatibilidad con consumidores anteriores.</summary>
    public RecognitionResult(
        IReadOnlyList<RecognizedPad> pads,
        IReadOnlyList<RecognizedVia> vias,
        IReadOnlyList<RecognizedHole> holes,
        IReadOnlyList<RecognizedFootprint> footprints,
        TimeSpan elapsed)
        : this(
            pads,
            vias,
            holes,
            footprints,
            GeometryDatabaseSnapshot.Empty,
            GeometryClassificationResult.Empty,
            PadDetectionDiagnostics.Empty,
            elapsed)
    {
    }

    /// <summary>Inicializa un resultado conservando compatibilidad con el clasificador geométrico.</summary>
    public RecognitionResult(
        IReadOnlyList<RecognizedPad> pads,
        IReadOnlyList<RecognizedVia> vias,
        IReadOnlyList<RecognizedHole> holes,
        IReadOnlyList<RecognizedFootprint> footprints,
        GeometryClassificationResult geometryClassification,
        TimeSpan elapsed)
        : this(
            pads,
            vias,
            holes,
            footprints,
            GeometryDatabaseSnapshot.Empty,
            geometryClassification,
            PadDetectionDiagnostics.Empty,
            elapsed)
    {
    }

    /// <summary>Inicializa un resultado completo de detección geométrica.</summary>
    public RecognitionResult(
        IReadOnlyList<RecognizedPad> pads,
        IReadOnlyList<RecognizedVia> vias,
        IReadOnlyList<RecognizedHole> holes,
        IReadOnlyList<RecognizedFootprint> footprints,
        GeometryDatabaseSnapshot geometryDatabase,
        GeometryClassificationResult geometryClassification,
        PadDetectionDiagnostics diagnostics,
        TimeSpan elapsed)
    {
        Pads = pads ?? throw new ArgumentNullException(nameof(pads));
        Vias = vias ?? throw new ArgumentNullException(nameof(vias));
        Holes = holes ?? throw new ArgumentNullException(nameof(holes));
        Footprints = footprints ?? throw new ArgumentNullException(nameof(footprints));
        GeometryDatabase = geometryDatabase ?? throw new ArgumentNullException(nameof(geometryDatabase));
        GeometryClassification = geometryClassification ??
            throw new ArgumentNullException(nameof(geometryClassification));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        Elapsed = elapsed;
    }

    /// <summary>Gets the accepted pad candidates.</summary>
    public IReadOnlyList<RecognizedPad> Pads { get; }

    /// <summary>Gets the circular conductive candidates classified as vias.</summary>
    public IReadOnlyList<RecognizedVia> Vias { get; }

    /// <summary>Gets the explicit or geometrically inferred holes.</summary>
    public IReadOnlyList<RecognizedHole> Holes { get; }

    /// <summary>Gets pad-only groups containing at least two pads.</summary>
    public IReadOnlyList<RecognizedFootprint> Footprints { get; }

    /// <summary>Obtiene la base completa de geometría previa a cualquier filtro.</summary>
    public GeometryDatabaseSnapshot GeometryDatabase { get; }

    /// <summary>Obtiene el resultado previo del Geometry Classification Engine.</summary>
    public GeometryClassificationResult GeometryClassification { get; }

    /// <summary>Obtiene métricas y motivos de descarte de cada candidato.</summary>
    public PadDetectionDiagnostics Diagnostics { get; }

    /// <summary>Gets the total analysis time.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Resumen compacto utilizado por la interfaz y los registros.</summary>
    public string Summary =>
        $"{Pads.Count:N0} pads · {Vias.Count:N0} vías · {Holes.Count:N0} agujeros · " +
        $"{Footprints.Count:N0} footprints · {GeometryClassification.Primitives.Count:N0} clasificadas · " +
        $"{GeometryDatabase.TotalCount:N0} registros geométricos";
}
