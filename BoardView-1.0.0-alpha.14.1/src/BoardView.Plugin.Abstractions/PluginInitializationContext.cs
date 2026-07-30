namespace BoardView.Plugin.Abstractions;

/// <summary>Contexto restringido entregado al plugin durante su inicialización.</summary>
public sealed record PluginInitializationContext(string ApplicationVersion, string PluginDirectory);
