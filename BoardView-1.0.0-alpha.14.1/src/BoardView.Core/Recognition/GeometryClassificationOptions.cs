namespace BoardView.Core.Recognition;

/// <summary>Parámetros del motor de clasificación de geometría documental.</summary>
public sealed record GeometryClassificationOptions
{
    /// <summary>Tolerancia relativa utilizada para agrupar primitivas de tamaño equivalente.</summary>
    public double SizeToleranceRatio { get; init; } = 0.12D;

    /// <summary>Relación máxima entre lados para clasificar un rectángulo como candidato a pad.</summary>
    public double MaximumRectangleAspectRatio { get; init; } = 5D;

    /// <summary>Factor máximo de separación entre vecinos alineados, medido en tamaños de primitiva.</summary>
    public double AlignmentDistanceFactor { get; init; } = 18D;

    /// <summary>Valida los parámetros antes de ejecutar la clasificación.</summary>
    internal void Validate()
    {
        if (SizeToleranceRatio <= 0D || SizeToleranceRatio >= 0.50D)
        {
            throw new ArgumentOutOfRangeException(nameof(SizeToleranceRatio));
        }

        if (MaximumRectangleAspectRatio < 1D)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumRectangleAspectRatio));
        }

        if (AlignmentDistanceFactor <= 1D)
        {
            throw new ArgumentOutOfRangeException(nameof(AlignmentDistanceFactor));
        }
    }
}
