namespace BoardView.Recognition.Footprints;

/// <summary>Familias de encapsulados reconocibles por la primera versión del motor.</summary>
public enum FootprintKind
{
    Unknown,
    Chip2,
    SingleRowConnector,
    DualRowConnector,
    Soic,
    Tssop,
    Qfn,
    Qfp,
    Bga,
    Ffc,
    Array,
    TestPoint,
}
