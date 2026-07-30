namespace BoardView.Core.Repair;

/// <summary>Estado de una observación durante el diagnóstico de una placa.</summary>
public enum RepairStatus
{
    Pending,
    Review,
    Suspect,
    Verified,
    Replaced,
    Resolved,
}
