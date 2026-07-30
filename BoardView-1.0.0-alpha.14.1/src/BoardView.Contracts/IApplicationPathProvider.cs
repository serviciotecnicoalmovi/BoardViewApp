namespace BoardView.Contracts;

/// <summary>Proporciona las rutas de escritura administradas por la aplicación.</summary>
public interface IApplicationPathProvider
{
    /// <summary>Directorio raíz de datos locales.</summary>
    string ApplicationDataDirectory { get; }

    /// <summary>Directorio donde se almacenan los registros.</summary>
    string LogDirectory { get; }

    /// <summary>Directorio de plugins externos.</summary>
    string PluginDirectory { get; }

    /// <summary>Crea, de forma idempotente, los directorios administrados.</summary>
    void EnsureDirectoriesExist();
}
