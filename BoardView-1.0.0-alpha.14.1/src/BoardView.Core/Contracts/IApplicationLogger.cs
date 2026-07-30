namespace BoardView.Core.Contracts;

/// <summary>Registra eventos operativos y errores de la aplicación.</summary>
public interface IApplicationLogger : IDisposable
{
    void Information(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}
