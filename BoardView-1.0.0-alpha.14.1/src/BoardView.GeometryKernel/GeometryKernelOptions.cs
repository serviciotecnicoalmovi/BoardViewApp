namespace BoardView.GeometryKernel;

/// <summary>
/// Configura las tolerancias numéricas utilizadas por el núcleo geométrico.
/// Todas las magnitudes lineales se expresan en milímetros.
/// </summary>
public sealed record GeometryKernelOptions
{
    /// <summary>Tolerancia usada para fusionar extremos geométricamente equivalentes.</summary>
    public double SnapTolerance { get; init; } = 0.01D;

    /// <summary>Tolerancia angular normalizada para comprobar perpendicularidad y paralelismo.</summary>
    public double AngularTolerance { get; init; } = 0.035D;

    /// <summary>Longitud mínima admitida para una arista.</summary>
    public double MinimumEdgeLength { get; init; } = 0.01D;

    /// <summary>Área mínima admitida para un contorno rectangular.</summary>
    public double MinimumRectangleArea { get; init; } = 0.0001D;
}
