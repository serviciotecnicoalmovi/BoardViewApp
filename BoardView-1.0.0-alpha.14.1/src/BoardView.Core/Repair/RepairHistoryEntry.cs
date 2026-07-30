namespace BoardView.Core.Repair;

/// <summary>Entrada cronológica de navegación o edición de una sesión de reparación.</summary>
public sealed class RepairHistoryEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public int? BoardPage { get; set; }
    public int? SchematicPage { get; set; }
}
