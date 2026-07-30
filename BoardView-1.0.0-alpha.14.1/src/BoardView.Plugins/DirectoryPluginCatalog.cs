using BoardView.Core.Contracts;
using BoardView.Core.Plugins;

namespace BoardView.Plugins;

/// <summary>Descubre ensamblados candidatos. La carga aislada se implementará en la fase de plugins.</summary>
public sealed class DirectoryPluginCatalog : IPluginCatalog
{
    public IReadOnlyList<PluginDescriptor> Discover(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new PluginDescriptor(Path.GetFileNameWithoutExtension(path), path, true))
            .ToArray();
    }
}
