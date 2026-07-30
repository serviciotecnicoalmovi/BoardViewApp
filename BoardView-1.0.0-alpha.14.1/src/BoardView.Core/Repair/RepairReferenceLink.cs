namespace BoardView.Core.Repair;

/// <summary>Vincula una referencia con sus páginas conocidas en placa y esquemático.</summary>
public sealed class RepairReferenceLink
{
    public string Reference { get; set; } = string.Empty;
    public int? BoardPage { get; set; }
    public int? SchematicPage { get; set; }
}
