using BoardView.Core.Geometry;

namespace BoardView.Core.GeometryDatabase;

/// <summary>Instantánea completa y consultable de la geometría de un documento.</summary>
public sealed class GeometryDatabaseSnapshot
{
    /// <summary>Instantánea vacía reutilizable.</summary>
    public static GeometryDatabaseSnapshot Empty { get; } = new([], Bounds2D.Empty, TimeSpan.Zero);

    /// <summary>Inicializa una instantánea geométrica.</summary>
    public GeometryDatabaseSnapshot(
        IReadOnlyList<GeometryDatabaseEntry> entries,
        Bounds2D bounds,
        TimeSpan elapsed)
    {
        Entries = entries ?? throw new ArgumentNullException(nameof(entries));
        Bounds = bounds;
        Elapsed = elapsed;
        Counts = entries
            .GroupBy(static entry => entry.Kind)
            .ToDictionary(static group => group.Key, static group => group.Count());
    }

    /// <summary>Todos los registros, sin filtros de visibilidad ni semántica electrónica.</summary>
    public IReadOnlyList<GeometryDatabaseEntry> Entries { get; }

    /// <summary>Cantidad de registros agrupados por tipo físico.</summary>
    public IReadOnlyDictionary<GeometryDatabasePrimitiveKind, int> Counts { get; }

    /// <summary>Límites globales de la geometría indexada.</summary>
    public Bounds2D Bounds { get; }

    /// <summary>Tiempo empleado en construir la instantánea.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Cantidad total de primitivas almacenadas.</summary>
    public int TotalCount => Entries.Count;

    /// <summary>Obtiene la cantidad de un tipo sin lanzar excepciones si no existe.</summary>
    public int Count(GeometryDatabasePrimitiveKind kind) => Counts.TryGetValue(kind, out int value) ? value : 0;

    /// <summary>Resumen compacto para registros y diagnóstico visual.</summary>
    public string Summary =>
        $"{TotalCount:N0} registros · " +
        $"{Count(GeometryDatabasePrimitiveKind.Rectangle):N0} rectángulos · " +
        $"{Count(GeometryDatabasePrimitiveKind.Ellipse):N0} elipses · " +
        $"{Count(GeometryDatabasePrimitiveKind.Polyline):N0} polilíneas · " +
        $"{Count(GeometryDatabasePrimitiveKind.Line):N0} líneas · " +
        $"{Count(GeometryDatabasePrimitiveKind.Text):N0} textos";
}
