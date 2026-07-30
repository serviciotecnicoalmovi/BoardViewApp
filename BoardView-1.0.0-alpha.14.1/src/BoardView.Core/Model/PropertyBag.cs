namespace BoardView.Core.Model;

/// <summary>Almacén de propiedades extensibles, insensible a mayúsculas y seguro para lectura concurrente.</summary>
public sealed class PropertyBag
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, string> Values => values;
    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        values[name.Trim()] = value?.Trim() ?? string.Empty;
    }
    public bool TryGet(string name, out string value) => values.TryGetValue(name, out value!);
    public string? GetOrDefault(string name) => values.TryGetValue(name, out string? value) ? value : null;
}
