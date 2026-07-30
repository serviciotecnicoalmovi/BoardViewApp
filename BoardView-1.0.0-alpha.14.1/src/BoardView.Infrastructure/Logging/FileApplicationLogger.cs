using System.Text;
using BoardView.Core.Contracts;

namespace BoardView.Infrastructure.Logging;

/// <summary>Logger seguro para múltiples hilos con rotación diaria por nombre de archivo.</summary>
public sealed class FileApplicationLogger : IApplicationLogger
{
    private readonly object synchronization = new();
    private readonly string logDirectory;
    private bool isDisposed;

    public FileApplicationLogger(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        this.logDirectory = logDirectory;
        Directory.CreateDirectory(logDirectory);
    }

    public void Information(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        StringBuilder line = new();
        line.Append(DateTimeOffset.Now.ToString("O"));
        line.Append(" [").Append(level).Append("] ").Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        lock (synchronization)
        {
            string path = Path.Combine(logDirectory, $"boardview-{DateTime.Today:yyyyMMdd}.log");
            File.AppendAllText(path, line.AppendLine().ToString(), Encoding.UTF8);
        }
    }

    public void Dispose() => isDisposed = true;
}
