using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class RecentProjectsServiceTests
{
    [Fact]
    public void Add_moves_project_to_front_and_removes_duplicates()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new RecentProjectsService(new SettingsService(directory));
            var first = Path.Combine(directory, "first.rushframe");
            var second = Path.Combine(directory, "second.rushframe");

            service.Add(first);
            service.Add(second);
            service.Add(first);

            Assert.Equal(new[] { Path.GetFullPath(first), Path.GetFullPath(second) }, service.Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Add_keeps_only_ten_recent_projects()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var service = new RecentProjectsService(new SettingsService(directory));
            for (var index = 0; index < 12; index++)
                service.Add(Path.Combine(directory, $"project-{index}.rushframe"));

            var paths = service.Load();
            Assert.Equal(10, paths.Count);
            Assert.EndsWith("project-11.rushframe", paths[0], StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("project-2.rushframe", paths[^1], StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Remove_and_clear_persist_changes()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var settings = new SettingsService(directory);
            var service = new RecentProjectsService(settings);
            var first = Path.Combine(directory, "first.rushframe");
            var second = Path.Combine(directory, "second.rushframe");
            service.Add(first);
            service.Add(second);

            service.Remove(second);
            Assert.Equal(new[] { Path.GetFullPath(first) }, new RecentProjectsService(settings).Load());

            service.Clear();
            Assert.Empty(new RecentProjectsService(settings).Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rushframe-recent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
