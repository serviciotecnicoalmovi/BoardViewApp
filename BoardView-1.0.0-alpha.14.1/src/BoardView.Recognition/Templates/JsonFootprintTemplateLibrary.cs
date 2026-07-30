using System.Text.Json;

namespace BoardView.Recognition.Templates;

/// <summary>Carga plantillas JSON externas y utiliza una biblioteca integrada cuando no existen archivos desplegados.</summary>
public sealed class JsonFootprintTemplateLibrary : IFootprintTemplateLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public JsonFootprintTemplateLibrary(string? sourceDirectory = null)
    {
        SourceDirectory = sourceDirectory ?? Path.Combine(AppContext.BaseDirectory, "Footprints");
        Templates = Load(SourceDirectory);
    }

    public IReadOnlyList<FootprintTemplate> Templates { get; }
    public string SourceDirectory { get; }

    private static IReadOnlyList<FootprintTemplate> Load(string directory)
    {
        List<FootprintTemplate> templates = [];
        if (Directory.Exists(directory))
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                string json = File.ReadAllText(file);
                FootprintTemplate? template = JsonSerializer.Deserialize<FootprintTemplate>(json, JsonOptions);
                if (template is null) throw new InvalidDataException($"La plantilla '{file}' está vacía.");
                template.Validate();
                templates.Add(template);
            }
        }

        if (templates.Count == 0) templates.AddRange(DefaultFootprintTemplates.Create());
        return templates.OrderByDescending(static template => template.Priority).ThenBy(static template => template.Name, StringComparer.Ordinal).ToArray();
    }
}
