namespace BoardView.Core.Repair;

/// <summary>Proyecto persistente que reúne placa, esquemático, vínculos y notas.</summary>
public sealed class RepairWorkspaceProject
{
    public string FormatVersion { get; set; } = "1.0";
    public string Name { get; set; } = "Nueva reparación";
    public string? BoardFilePath { get; set; }
    public string? SchematicFilePath { get; set; }
    public string LastReference { get; set; } = string.Empty;
    public int BoardPage { get; set; } = 1;
    public int SchematicPage { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<RepairAnnotation> Annotations { get; set; } = [];
    public List<RepairReferenceLink> ReferenceLinks { get; set; } = [];
    public List<RepairHistoryEntry> History { get; set; } = [];
}
