using BoardView.Core.Formats;

namespace BoardView.Core.Contracts;

/// <summary>Expone los formatos conocidos y detecta el formato probable de un archivo.</summary>
public interface IFileFormatRegistry
{
    IReadOnlyList<FileFormatDescriptor> Formats { get; }
    FileFormatDescriptor? Detect(string filePath);
}
