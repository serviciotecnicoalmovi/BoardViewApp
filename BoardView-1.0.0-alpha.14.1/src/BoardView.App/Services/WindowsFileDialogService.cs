using System.IO;
using Microsoft.Win32;

namespace BoardView.App.Services;

/// <summary>Implementación WPF de los selectores de documentos y proyectos.</summary>
public sealed class WindowsFileDialogService : IFileDialogService
{
    public string? SelectFile(string? initialDirectory)
    {
        OpenFileDialog dialog = CreateOpenDialog(
            "Abrir archivo de placa",
            "Archivos compatibles|*.pdf;*.pcb;*.pbr;*.brd;*.kicad_pcb;*.gbr;*.ger;*.gtl;*.gbl;*.drl;*.xln;*.xml;*.tgz;*.tar;*.zip|Todos los archivos|*.*",
            initialDirectory);
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectPdfFile(string title, string? initialDirectory)
    {
        OpenFileDialog dialog = CreateOpenDialog(title, "Documentos PDF|*.pdf|Todos los archivos|*.*", initialDirectory);
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectRepairProjectFile()
    {
        OpenFileDialog dialog = CreateOpenDialog("Abrir proyecto de reparación", "Proyecto BoardView Repair|*.bvrepair|JSON|*.json", null);
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectRepairProjectSavePath(string? currentPath)
    {
        SaveFileDialog dialog = new()
        {
            Title = "Guardar proyecto de reparación",
            Filter = "Proyecto BoardView Repair|*.bvrepair",
            DefaultExt = ".bvrepair",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = string.IsNullOrWhiteSpace(currentPath) ? "reparacion.bvrepair" : Path.GetFileName(currentPath),
        };
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            string? directory = Path.GetDirectoryName(currentPath);
            if (Directory.Exists(directory)) dialog.InitialDirectory = directory;
        }
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static OpenFileDialog CreateOpenDialog(string title, string filter, string? initialDirectory)
    {
        OpenFileDialog dialog = new()
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog;
    }
}
