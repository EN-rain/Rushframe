using System.Diagnostics;
using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class LocalOutputPathGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rushframe-output-guard-{Guid.NewGuid():N}");

    public LocalOutputPathGuardTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void relative_output_inside_allowed_root_is_accepted()
    {
        var allowed = Path.Combine(_root, "project");

        var result = LocalOutputPathGuard.Resolve(allowed, Path.Combine("exports", "video.mp4"));

        Assert.Equal(Path.GetFullPath(Path.Combine(allowed, "exports", "video.mp4")), result);
    }

    [Fact]
    public void lexical_parent_escape_is_rejected()
    {
        var allowed = Path.Combine(_root, "project");

        var error = Assert.Throws<InvalidOperationException>(() =>
            LocalOutputPathGuard.Resolve(allowed, Path.Combine("..", "outside.mp4")));

        Assert.Contains("stay inside", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void junction_escape_is_rejected()
    {
        var allowed = Path.Combine(_root, "project");
        var outside = Path.Combine(_root, "outside");
        var junction = Path.Combine(allowed, "linked");
        Directory.CreateDirectory(allowed);
        Directory.CreateDirectory(outside);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junction);
        startInfo.ArgumentList.Add(outside);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not create junction test fixture.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);

        var error = Assert.Throws<InvalidOperationException>(() =>
            LocalOutputPathGuard.Resolve(allowed, Path.Combine("linked", "escaped.mp4")));

        Assert.Contains("filesystem link", error.Message, StringComparison.OrdinalIgnoreCase);
        RemoveJunction(junction);
    }

    [Fact]
    public void guard_source_checks_mapped_network_drives_and_reparse_targets()
    {
        var source = File.ReadAllText(SourcePath(
            "src", "Rushframe.Infrastructure", "LocalPhysicalPathGuard.cs"));

        Assert.Contains("DriveType.Network", source, StringComparison.Ordinal);
        Assert.Contains("FileAttributes.ReparsePoint", source, StringComparison.Ordinal);
        Assert.Contains("ResolveLinkTarget", source, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        var junction = Path.Combine(_root, "project", "linked");
        if (Directory.Exists(junction)) RemoveJunction(junction);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void RemoveJunction(string junction)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("rmdir");
        startInfo.ArgumentList.Add(junction);
        using var process = Process.Start(startInfo);
        process?.WaitForExit();
    }

    private static string SourcePath(params string[] parts) =>
        Path.Combine(FindRepositoryRoot(), Path.Combine(parts));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rushframe.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the Rushframe repository root.");
    }
}
