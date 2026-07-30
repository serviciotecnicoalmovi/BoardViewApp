namespace BoardView.Core.Recognition;

/// <summary>Parámetros conservadores del detector geométrico de pads.</summary>
public sealed record PadDetectionOptions
{
    /// <summary>Fracción mínima de la dimensión menor del documento admitida para un pad.</summary>
    public double MinimumPadSizeRatio { get; init; } = 0.00008D;

    /// <summary>Fracción máxima de la dimensión menor del documento admitida para un pad.</summary>
    public double MaximumPadSizeRatio { get; init; } = 0.035D;

    /// <summary>Límite físico mínimo después de normalizar todas las coordenadas a milímetros.</summary>
    public double MinimumPadSizeMillimeters { get; init; } = 0.04D;

    /// <summary>Límite físico máximo después de normalizar todas las coordenadas a milímetros.</summary>
    public double MaximumPadSizeMillimeters { get; init; } = 20D;

    /// <summary>Relación máxima entre lados para aceptar un pad rectangular.</summary>
    public double MaximumPadAspectRatio { get; init; } = 6D;

    /// <summary>Multiplicador del tamaño medio empleado para agrupar pads en footprints.</summary>
    public double FootprintNeighborFactor { get; init; } = 4.5D;

    /// <summary>Cantidad máxima de pads admitida en un footprint inferido.</summary>
    public int MaximumPadsPerFootprint { get; init; } = 512;

    /// <summary>Valida que las opciones produzcan un análisis numéricamente estable.</summary>
    internal void Validate()
    {
        if (MinimumPadSizeRatio <= 0D || MaximumPadSizeRatio <= MinimumPadSizeRatio)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumPadSizeRatio));
        }

        if (MinimumPadSizeMillimeters <= 0D ||
            MaximumPadSizeMillimeters <= MinimumPadSizeMillimeters)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumPadSizeMillimeters));
        }

        if (MaximumPadAspectRatio < 1D)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPadAspectRatio));
        }

        if (FootprintNeighborFactor <= 1D)
        {
            throw new ArgumentOutOfRangeException(nameof(FootprintNeighborFactor));
        }

        if (MaximumPadsPerFootprint < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPadsPerFootprint));
        }
    }
}
