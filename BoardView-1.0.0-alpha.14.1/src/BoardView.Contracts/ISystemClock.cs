namespace BoardView.Contracts;

/// <summary>Abstracción del reloj del sistema para conseguir pruebas deterministas.</summary>
public interface ISystemClock
{
    /// <summary>Obtiene la fecha y hora UTC actual.</summary>
    DateTimeOffset UtcNow { get; }
}
