using System.IO;
using System.Text.Json;

namespace Rushframe.Desktop.Workspace;

public sealed class WorkspaceLayoutService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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
            if (layout?.Version == WorkspaceLayout.SchemaVersion) return layout;
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
