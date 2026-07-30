using BoardView.Contracts;

namespace BoardView.Configuration;

/// <summary>Resuelve y prepara las rutas persistentes de BoardView para el usuario actual.</summary>
public sealed class ApplicationPathProvider : IApplicationPathProvider
{
    /// <summary>Inicializa las rutas usando LocalApplicationData o un directorio raíz explícito para pruebas.</summary>
    public ApplicationPathProvider(string? rootDirectory = null)
    {
        ApplicationDataDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BoardView");
        LogDirectory = Path.Combine(ApplicationDataDirectory, "Logs");
        PluginDirectory = Path.Combine(ApplicationDataDirectory, "Plugins");
    }

    /// <inheritdoc />
    public string ApplicationDataDirectory { get; }

    /// <inheritdoc />
    public string LogDirectory { get; }

    /// <inheritdoc />
    public string PluginDirectory { get; }

    /// <inheritdoc />
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(PluginDirectory);
    }
}
