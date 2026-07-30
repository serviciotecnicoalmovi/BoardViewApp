using System.Text.Json;
using BoardView.Core.Configuration;
using BoardView.Core.Contracts;

namespace BoardView.Infrastructure.Configuration;

/// <summary>Persiste la configuración del usuario en JSON dentro de AppData.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string settingsPath;
    private readonly IApplicationLogger logger;

    public JsonSettingsService(string applicationDataDirectory, IApplicationLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataDirectory);
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Directory.CreateDirectory(applicationDataDirectory);
        settingsPath = Path.Combine(applicationDataDirectory, "settings.json");
    }

    public ApplicationSettings Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new ApplicationSettings();
            }

            string json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<ApplicationSettings>(json, SerializerOptions) ?? new ApplicationSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.Error("No fue posible cargar la configuración. Se usarán valores predeterminados.", exception);
            return new ApplicationSettings();
        }
    }

    public void Save(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(settingsPath, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Error("No fue posible guardar la configuración.", exception);
        }
    }
}
