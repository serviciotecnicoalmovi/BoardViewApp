using BoardView.Core.Configuration;

namespace BoardView.Core.Contracts;

/// <summary>Carga y guarda la configuración persistente.</summary>
public interface ISettingsService
{
    ApplicationSettings Load();
    void Save(ApplicationSettings settings);
}
