using BoardView.Contracts;

namespace BoardView.Configuration;

/// <summary>Implementación de producción del reloj del sistema.</summary>
public sealed class SystemClock : ISystemClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
