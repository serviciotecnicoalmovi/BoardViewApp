namespace BoardView.Core.Repair;

/// <summary>Persiste y recupera proyectos de reparación sin modificar los documentos originales.</summary>
public interface IRepairWorkspaceStore
{
    RepairWorkspaceProject Load(string filePath);
    void Save(string filePath, RepairWorkspaceProject project);
}
