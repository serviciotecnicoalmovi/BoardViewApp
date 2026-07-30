namespace BoardView.Core.Plugins;

/// <summary>Representa un ensamblado candidato a plugin.</summary>
public sealed record PluginDescriptor(string Name, string FilePath, bool IsEnabled);
