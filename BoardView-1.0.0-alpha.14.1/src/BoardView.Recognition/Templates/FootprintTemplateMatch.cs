namespace BoardView.Recognition.Templates;

/// <summary>Resultado auditable de comparar un cluster contra una plantilla.</summary>
public sealed record FootprintTemplateMatch(
    string TemplateName,
    string Family,
    double Score,
    bool Accepted,
    IReadOnlyDictionary<string, double> Factors,
    string Status)
{
    public static FootprintTemplateMatch None { get; } = new("Sin plantilla", "Unknown", 0D, false,
        new Dictionary<string, double>(), "Sin coincidencia");
}
