namespace BoardView.Core.Documents;

/// <summary>Clasificación funcional de una capa.</summary>
public enum LayerType
{
    Unknown = 0,
    Copper = 1,
    SolderMask = 2,
    Silkscreen = 3,
    Paste = 4,
    Mechanical = 5,
    Drill = 6,
    Outline = 7,
    Document = 8,
}
