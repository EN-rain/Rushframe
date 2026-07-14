using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rushframe.Desktop.Workspace;

public sealed class WorkspaceLayoutService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _filePath;

    public WorkspaceLayoutService(string settingsDirectory)
    {
        Directory.CreateDirectory(settingsDirectory);
        _filePath = Path.Combine(settingsDirectory, "workspace-layout.json");
    }

    public WorkspaceLayout Load()
    {
        try
        {
            var json = File.ReadAllText(_filePath);
            var layout = JsonSerializer.Deserialize<WorkspaceLayout>(json, JsonOptions);
            if (layout != null && layout.Version is >= 2 and <= WorkspaceLayout.SchemaVersion)
                return layout.Normalize();
        }
        catch
        {
        }

        return WorkspaceLayout.Default();
    }

    public void Save(WorkspaceLayout layout)
    {
        var tmp = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(layout, JsonOptions);
        File.WriteAllText(tmp, json);
        File.Move(tmp, _filePath, overwrite: true);
    }
}
