namespace BoardView.App.Services;

/// <summary>Abstrae los diálogos de archivos para mantener los ViewModels independientes de WPF.</summary>
public interface IFileDialogService
{
    string? SelectFile(string? initialDirectory);
    string? SelectPdfFile(string title, string? initialDirectory);
    string? SelectRepairProjectFile();
    string? SelectRepairProjectSavePath(string? currentPath);
}
