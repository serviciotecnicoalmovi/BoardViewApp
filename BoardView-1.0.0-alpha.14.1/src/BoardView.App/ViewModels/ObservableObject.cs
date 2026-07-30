using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BoardView.App.ViewModels;

/// <summary>
/// Proporciona notificación de cambios para los ViewModels de la aplicación.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Actualiza el campo indicado y notifica el cambio cuando el nuevo valor es diferente.
    /// </summary>
    /// <typeparam name="T">Tipo del valor almacenado.</typeparam>
    /// <param name="storage">Campo de respaldo de la propiedad.</param>
    /// <param name="value">Nuevo valor.</param>
    /// <param name="propertyName">Nombre de la propiedad que cambió.</param>
    /// <returns><see langword="true"/> cuando el valor fue actualizado.</returns>
    protected bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Notifica manualmente el cambio de una propiedad calculada o dependiente.
    /// </summary>
    /// <param name="propertyName">Nombre de la propiedad que cambió.</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
