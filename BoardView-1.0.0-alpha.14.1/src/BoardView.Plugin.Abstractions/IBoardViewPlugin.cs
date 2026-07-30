using BoardView.Contracts;

namespace BoardView.Plugin.Abstractions;

/// <summary>Contrato estable implementado por todos los plugins externos de BoardView.</summary>
public interface IBoardViewPlugin : IDisposable
{
    /// <summary>Obtiene los metadatos inmutables del plugin.</summary>
    PluginMetadata Metadata { get; }

    /// <summary>Inicializa el plugin una sola vez.</summary>
    OperationResult Initialize(PluginInitializationContext context);
}
