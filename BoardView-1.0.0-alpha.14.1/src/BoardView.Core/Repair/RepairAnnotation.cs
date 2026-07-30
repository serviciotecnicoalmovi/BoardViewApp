namespace BoardView.Core.Repair;

/// <summary>Nota persistente asociada a una referencia o ubicación de reparación.</summary>
public sealed class RepairAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Reference { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public RepairStatus Status { get; set; } = RepairStatus.Pending;
    public int? BoardPage { get; set; }
    public int? SchematicPage { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
