using System.Text.Json;
using BoardView.Core.Repair;

namespace BoardView.Infrastructure.Repair;

/// <summary>Almacena sesiones de reparación en JSON legible y versionado.</summary>
public sealed class JsonRepairWorkspaceStore : IRepairWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public RepairWorkspaceProject Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<RepairWorkspaceProject>(json, SerializerOptions)
            ?? throw new InvalidDataException("El proyecto de reparación no contiene datos válidos.");
    }

    public void Save(string filePath, RepairWorkspaceProject project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(project);
        project.UpdatedUtc = DateTimeOffset.UtcNow;
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(project, SerializerOptions));
        File.Move(temporaryPath, filePath, true);
    }
}
