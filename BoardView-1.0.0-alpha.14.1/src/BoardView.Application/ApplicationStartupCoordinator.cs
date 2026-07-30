using BoardView.Contracts;
using BoardView.Core.Contracts;

namespace BoardView.Application;

/// <summary>Ejecuta las tareas de infraestructura requeridas antes de mostrar la ventana principal.</summary>
public sealed class ApplicationStartupCoordinator
{
    private readonly IApplicationPathProvider pathProvider;
    private readonly IApplicationLogger logger;
    private readonly IPluginCatalog pluginCatalog;

    /// <summary>Inicializa el coordinador con dependencias explícitas.</summary>
    public ApplicationStartupCoordinator(
        IApplicationPathProvider pathProvider,
        IApplicationLogger logger,
        IPluginCatalog pluginCatalog)
    {
        this.pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.pluginCatalog = pluginCatalog ?? throw new ArgumentNullException(nameof(pluginCatalog));
    }

    /// <summary>Prepara directorios, registra el inicio y descubre plugins disponibles.</summary>
    public IReadOnlyList<BoardView.Core.Plugins.PluginDescriptor> Initialize()
    {
        pathProvider.EnsureDirectoriesExist();
        logger.Information($"{ApplicationInformation.DisplayName} iniciado.");
        IReadOnlyList<BoardView.Core.Plugins.PluginDescriptor> plugins = pluginCatalog.Discover(pathProvider.PluginDirectory);
        logger.Information($"Plugins candidatos detectados: {plugins.Count}.");
        return plugins;
    }
}
