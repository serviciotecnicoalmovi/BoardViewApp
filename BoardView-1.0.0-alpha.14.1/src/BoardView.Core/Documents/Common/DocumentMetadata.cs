namespace BoardView.Core.Documents.Common;

/// <summary>
/// Almacén extensible de metadatos. Las claves no distinguen mayúsculas y minúsculas.
/// </summary>
public sealed class DocumentMetadata
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Vista de solo lectura de los valores registrados.</summary>
    public IReadOnlyDictionary<string, string> Values => values;

    /// <summary>Registra o reemplaza un valor.</summary>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        values[key.Trim()] = value;
    }

    /// <summary>Intenta obtener un valor.</summary>
    public bool TryGetValue(string key, out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return values.TryGetValue(key.Trim(), out value);
    }

    /// <summary>Elimina un valor.</summary>
    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return values.Remove(key.Trim());
    }
}
