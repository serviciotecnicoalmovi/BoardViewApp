namespace BoardView.Recognition.Templates;

/// <summary>Biblioteca de respaldo que garantiza reconocimiento incluso si faltan los archivos externos.</summary>
internal static class DefaultFootprintTemplates
{
    public static IEnumerable<FootprintTemplate> Create()
    {
        yield return T("CHIP-2", "Chip2", 2, 2, 1, 2, 1, 2, 0, 10, .8, 1, .5, 0, 20, priority: 100);
        yield return T("SOIC", "Soic", 6, 32, 2, 2, 3, 16, .8, 1.5, .7, 1, .65, 1.2, 6, twoRows: true, priority: 80);
        yield return T("TSSOP", "Tssop", 8, 64, 2, 2, 4, 32, .3, .79, .7, 1, .65, 1.2, 8, twoRows: true, priority: 90);
        yield return T("QFN", "Qfn", 8, 100, 3, 30, 3, 30, .25, 1.0, .15, .75, .65, .75, 1.35, fourSides: true, priority: 85);
        yield return T("QFP", "Qfp", 16, 256, 4, 80, 4, 80, .3, 1.27, .05, .65, .7, .6, 1.7, fourSides: true, priority: 84);
        yield return T("BGA", "Bga", 9, 2500, 3, 60, 3, 60, .3, 1.5, .55, 1, .7, .65, 1.55, square: true, priority: 95);
        yield return T("FFC", "Ffc", 4, 100, 1, 2, 2, 100, .2, 1.5, .6, 1, .5, 2, 100, priority: 70);
        yield return T("CONNECTOR-SINGLE", "SingleRowConnector", 3, 100, 1, 1, 3, 100, .5, 5, .8, 1, .4, 2, 100, priority: 30);
        yield return T("CONNECTOR-DUAL", "DualRowConnector", 4, 200, 2, 2, 2, 100, .5, 5, .6, 1, .45, 1, 100, twoRows: true, priority: 35);
        yield return T("ARRAY", "Array", 3, 2500, 1, 100, 1, 100, 0, 10, 0, 1, 0, 0, 100, acceptance: .45, priority: 1);
    }

    private static FootprintTemplate T(string name, string family, int minPads, int maxPads, int minRows, int maxRows,
        int minColumns, int maxColumns, double minPitch, double maxPitch, double minOcc, double maxOcc,
        double minSym, double minAspect, double maxAspect, bool square = false, bool twoRows = false,
        bool fourSides = false, double acceptance = .70, int priority = 0) => new()
    {
        Name = name, Family = family, MinPads = minPads, MaxPads = maxPads, MinRows = minRows, MaxRows = maxRows,
        MinColumns = minColumns, MaxColumns = maxColumns, MinPitch = minPitch, MaxPitch = maxPitch,
        MinOccupancy = minOcc, MaxOccupancy = maxOcc, MinSymmetry = minSym, MinAspectRatio = minAspect,
        MaxAspectRatio = maxAspect, RequiresSquareMatrix = square, RequiresTwoRows = twoRows,
        RequiresFourSides = fourSides, AcceptanceScore = acceptance, Priority = priority,
    };
}
