namespace BoardView.SemanticKernel;

/// <summary>Significado electrónico o documental inferido para una primitiva geométrica.</summary>
public enum PrimitiveSemantic
{
    Unknown = 0,
    Pad,
    Via,
    Hole,
    Copper,
    ComponentBody,
    Silkscreen,
    BoardOutline,
    Mechanical,
    Text,
}
