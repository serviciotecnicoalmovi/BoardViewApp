using BoardView.Core.Plugins;

namespace BoardView.Core.Contracts;

/// <summary>Descubre y describe plugins disponibles.</summary>
public interface IPluginCatalog
{
    IReadOnlyList<PluginDescriptor> Discover(string directoryPath);
}
