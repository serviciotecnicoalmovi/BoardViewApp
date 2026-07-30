namespace BoardView.SemanticKernel;

/// <summary>Umbrales físicos empleados por el clasificador semántico.</summary>
public sealed class SemanticKernelOptions
{
    /// <summary>Área relativa mínima para considerar un contorno como candidato de placa.</summary>
    public double BoardOutlineAreaRatio { get; init; } = 0.35D;

    /// <summary>Área relativa mínima para considerar una forma como cuerpo de componente.</summary>
    public double ComponentBodyAreaRatio { get; init; } = 0.00008D;

    /// <summary>Dimensión máxima en milímetros de una forma conductiva candidata a pad.</summary>
    public double MaximumPadDimensionMillimeters { get; init; } = 20D;

    /// <summary>Valida que las opciones sean finitas y físicamente coherentes.</summary>
    public void Validate()
    {
        if (!double.IsFinite(BoardOutlineAreaRatio) || BoardOutlineAreaRatio is <= 0D or > 1D)
        {
            throw new ArgumentOutOfRangeException(nameof(BoardOutlineAreaRatio));
        }

        if (!double.IsFinite(ComponentBodyAreaRatio) || ComponentBodyAreaRatio is <= 0D or >= 1D)
        {
            throw new ArgumentOutOfRangeException(nameof(ComponentBodyAreaRatio));
        }

        if (!double.IsFinite(MaximumPadDimensionMillimeters) || MaximumPadDimensionMillimeters <= 0D)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumPadDimensionMillimeters));
        }
    }
}
