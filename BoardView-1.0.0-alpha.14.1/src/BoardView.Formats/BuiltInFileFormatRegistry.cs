using BoardView.Core.Contracts;
using BoardView.Core.Formats;

namespace BoardView.Formats;

/// <summary>Registro inicial de formatos. La detección por contenido se añadirá con los parsers.</summary>
public sealed class BuiltInFileFormatRegistry : IFileFormatRegistry
{
    private static readonly IReadOnlyList<FileFormatDescriptor> KnownFormats =
    [
        new("pdf", "Documento PDF", [".pdf"], "Documento", false),
        new("gerber", "Gerber", [".gbr", ".ger", ".gtl", ".gbl", ".gts", ".gbs", ".gto", ".gbo"], "Fabricación", false),
        new("excellon", "Excellon Drill", [".drl", ".xln"], "Fabricación", false),
        new("kicad-pcb", "KiCad PCB", [".kicad_pcb"], "Diseño", false),
        new("eagle", "Autodesk EAGLE", [".brd"], "Diseño", false),
        new("pcb", "Archivo PCB genérico", [".pcb", ".pbr"], "Diseño", false),
        new("ipc2581", "IPC-2581", [".xml", ".cvg"], "Intercambio", false),
        new("odbpp", "ODB++", [".tgz", ".tar", ".zip"], "Fabricación", false)
    ];

    public IReadOnlyList<FileFormatDescriptor> Formats => KnownFormats;

    public FileFormatDescriptor? Detect(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string extension = Path.GetExtension(filePath);
        return KnownFormats.FirstOrDefault(format =>
            format.Extensions.Any(candidate => string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase)));
    }
}
