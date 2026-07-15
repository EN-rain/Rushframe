using Rushframe.Domain;

namespace Rushframe.Infrastructure;

/// <summary>
/// Resolves local filesystem paths after following existing reparse points. This
/// prevents an allowed-looking path from escaping its declared root through a
/// junction, symlink, UNC path, or mapped network drive.
/// </summary>
public static class LocalPhysicalPathGuard
{
    private static readonly Type DomainAssemblyMarker = typeof(ProjectId);

    public static string ResolveContainedExistingFile(string rootDirectory, string requestedPath)
    {
        var resolved = ResolveContained(rootDirectory, requestedPath, requireExisting: true);
        if (!File.Exists(resolved)) throw new FileNotFoundException("Contained file was not found.", resolved);
        return resolved;
    }

    public static string ResolveContainedExistingDirectory(string rootDirectory, string requestedPath)
    {
        var resolved = ResolveContained(rootDirectory, requestedPath, requireExisting: true);
        if (!Directory.Exists(resolved)) throw new DirectoryNotFoundException($"Contained directory was not found: {resolved}");
        return resolved;
    }

    public static string ResolveContainedOutput(string rootDirectory, string requestedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        var root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        EnsureLocalDrive(root);
        var candidate = Path.IsPathRooted(requestedPath)
            ? Path.GetFullPath(requestedPath)
            : Path.GetFullPath(Path.Combine(root, requestedPath));
        EnsureLexicallyContained(root, candidate);

        var physicalRoot = ResolveExistingPath(root);
        EnsureLocalDrive(physicalRoot);
        var parent = Path.GetDirectoryName(candidate)
                     ?? throw new InvalidOperationException("Output path has no parent directory.");
        var existingParent = FindExistingAncestor(parent);
        var physicalParent = ResolveExistingPath(existingParent);
        EnsureLocalDrive(physicalParent);
        EnsurePhysicallyContained(physicalRoot, physicalParent);

        if (File.Exists(candidate) || Directory.Exists(candidate))
        {
            var physicalCandidate = ResolveExistingPath(candidate);
            EnsureLocalDrive(physicalCandidate);
            EnsurePhysicallyContained(physicalRoot, physicalCandidate);
        }
        return candidate;
    }

    public static void EnsureLocalDrive(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)
            || fullPath.StartsWith("//", StringComparison.Ordinal))
            throw new InvalidOperationException("Path must use a local drive.");
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Path must be rooted on a local drive.");
        try
        {
            if (new DriveInfo(root).DriveType == DriveType.Network)
                throw new InvalidOperationException("Mapped network drives are not permitted.");
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("Path drive is invalid.");
        }
    }

    private static string ResolveContained(string rootDirectory, string requestedPath, bool requireExisting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPath);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Root directory was not found: {root}");
        EnsureLocalDrive(root);
        var candidate = Path.IsPathRooted(requestedPath)
            ? Path.GetFullPath(requestedPath)
            : Path.GetFullPath(Path.Combine(root, requestedPath));
        EnsureLexicallyContained(root, candidate);
        if (requireExisting && !File.Exists(candidate) && !Directory.Exists(candidate))
            throw new FileNotFoundException("Contained path was not found.", candidate);

        var physicalRoot = ResolveExistingPath(root);
        var physicalCandidate = ResolveExistingPath(candidate);
        EnsureLocalDrive(physicalRoot);
        EnsureLocalDrive(physicalCandidate);
        EnsurePhysicallyContained(physicalRoot, physicalCandidate);
        return physicalCandidate;
    }

    private static void EnsureLexicallyContained(string root, string candidate)
    {
        if (!IsWithin(root, candidate))
            throw new InvalidOperationException("Path escapes its declared root.");
    }

    private static void EnsurePhysicallyContained(string root, string candidate)
    {
        if (!IsWithin(root, candidate))
            throw new InvalidOperationException("Path escapes its declared root through a filesystem link.");
    }

    private static string FindExistingAncestor(string path)
    {
        var current = Path.GetFullPath(path);
        while (!Directory.Exists(current) && !File.Exists(current))
        {
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path parent directory could not be resolved.");
            current = parent;
        }
        return current;
    }

    private static string ResolveExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
                   ?? throw new InvalidOperationException("Filesystem path has no root.");
        var current = root;
        var relative = fullPath[root.Length..];
        foreach (var component in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, component);
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                var info = Directory.Exists(candidate)
                    ? (FileSystemInfo)new DirectoryInfo(candidate)
                    : new FileInfo(candidate);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    var target = info.ResolveLinkTarget(returnFinalTarget: true)
                                 ?? throw new InvalidOperationException($"Could not resolve filesystem link '{candidate}'.");
                    current = Path.GetFullPath(target.FullName);
                    continue;
                }
            }
            current = candidate;
        }
        return Path.GetFullPath(current);
    }

    private static bool IsWithin(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return !Path.IsPathRooted(relative)
               && !string.Equals(relative, "..", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
