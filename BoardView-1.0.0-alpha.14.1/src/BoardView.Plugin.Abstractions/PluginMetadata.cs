namespace BoardView.Plugin.Abstractions;

/// <summary>Describe un plugin compatible con BoardView.</summary>
public sealed record PluginMetadata(string Id, string Name, Version Version, string Vendor)
{
    /// <summary>Valida que los datos mínimos del manifiesto sean correctos.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentNullException.ThrowIfNull(Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(Vendor);
    }
}
