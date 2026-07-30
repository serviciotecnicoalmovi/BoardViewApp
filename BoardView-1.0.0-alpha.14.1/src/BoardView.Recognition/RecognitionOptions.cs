namespace BoardView.Recognition;

/// <summary>Parámetros físicos y de agrupación del motor de reconocimiento.</summary>
public sealed class RecognitionOptions
{
    /// <summary>Multiplicador del tamaño mediano de pad usado como radio de vecindad.</summary>
    public double NeighborScale { get; init; } = 4.5D;

    /// <summary>Distancia máxima absoluta entre centros de pads vecinos, en milímetros.</summary>
    public double MaximumNeighborDistanceMillimeters { get; init; } = 3.5D;

    /// <summary>Tolerancia relativa para agrupar filas y columnas.</summary>
    public double AxisToleranceScale { get; init; } = 0.75D;

    /// <summary>Cantidad mínima de pads para formar un footprint.</summary>
    public int MinimumPadsPerFootprint { get; init; } = 2;

    /// <summary>Valida los límites para impedir configuraciones incoherentes.</summary>
    public void Validate()
    {
        if (NeighborScale <= 0D) throw new ArgumentOutOfRangeException(nameof(NeighborScale));
        if (MaximumNeighborDistanceMillimeters <= 0D) throw new ArgumentOutOfRangeException(nameof(MaximumNeighborDistanceMillimeters));
        if (AxisToleranceScale <= 0D) throw new ArgumentOutOfRangeException(nameof(AxisToleranceScale));
        if (MinimumPadsPerFootprint < 2) throw new ArgumentOutOfRangeException(nameof(MinimumPadsPerFootprint));
    }
}
