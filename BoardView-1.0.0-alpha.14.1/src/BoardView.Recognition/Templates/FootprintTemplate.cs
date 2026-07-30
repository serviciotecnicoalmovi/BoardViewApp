namespace BoardView.Recognition.Templates;

/// <summary>Describe, mediante rangos físicos, un encapsulado reconocible sin codificar reglas específicas en el motor.</summary>
public sealed class FootprintTemplate
{
    public string Name { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public int MinPads { get; init; }
    public int MaxPads { get; init; }
    public int MinRows { get; init; }
    public int MaxRows { get; init; }
    public int MinColumns { get; init; }
    public int MaxColumns { get; init; }
    public double MinPitch { get; init; }
    public double MaxPitch { get; init; }
    public double MinOccupancy { get; init; }
    public double MaxOccupancy { get; init; } = 1D;
    public double MinSymmetry { get; init; }
    public double MinAspectRatio { get; init; }
    public double MaxAspectRatio { get; init; } = double.MaxValue;
    public bool RequiresSquareMatrix { get; init; }
    public bool RequiresTwoRows { get; init; }
    public bool RequiresFourSides { get; init; }
    public double AcceptanceScore { get; init; } = 0.70D;
    public int Priority { get; init; }

    /// <summary>Valida que la plantilla pueda utilizarse con seguridad.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("La plantilla debe tener nombre.");
        if (MinPads < 1 || MaxPads < MinPads) throw new InvalidDataException($"Rango de pads inválido en {Name}.");
        if (MinRows < 1 || MaxRows < MinRows || MinColumns < 1 || MaxColumns < MinColumns)
            throw new InvalidDataException($"Rango de ejes inválido en {Name}.");
        if (MinPitch < 0D || MaxPitch < MinPitch) throw new InvalidDataException($"Rango de pitch inválido en {Name}.");
        if (AcceptanceScore is < 0D or > 1D) throw new InvalidDataException($"Score inválido en {Name}.");
    }
}
