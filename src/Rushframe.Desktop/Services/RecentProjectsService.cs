using System.IO;

namespace Rushframe.Desktop.Services;

public sealed class RecentProjectsService
{
    private const string SettingsSection = "recentProjects";
    private const int MaximumEntries = 10;
    private readonly SettingsService _settingsService;

    public RecentProjectsService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public IReadOnlyList<string> Load()
    {
        var settings = _settingsService.Load(SettingsSection, new RecentProjectsSettings());
        return settings.Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumEntries)
            .ToArray();
    }

    public void Add(string path)
    {
        var normalized = NormalizePath(path);
        var paths = Load()
            .Where(candidate => !string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            .Prepend(normalized)
            .Take(MaximumEntries)
            .ToList();
        Save(paths);
    }

    public void Remove(string path)
    {
        var normalized = NormalizePath(path);
        Save(Load()
            .Where(candidate => !string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
            .ToList());
    }

    public void Clear() => Save([]);

    private void Save(List<string> paths) =>
        _settingsService.Save(SettingsSection, new RecentProjectsSettings { Paths = paths });

    private static string NormalizePath(string path) => Path.GetFullPath(path.Trim());
}

public sealed class RecentProjectsSettings
{
    public List<string> Paths { get; init; } = [];
}
