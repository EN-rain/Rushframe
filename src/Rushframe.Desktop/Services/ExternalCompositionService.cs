using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Rushframe.Domain;
using Rushframe.Media.Native;

namespace Rushframe.Desktop.Services;

public sealed class ExternalCompositionService
{
    private const int MaximumDiagnosticCharacters = 128 * 1024;
    private readonly FfmpegMediaService _mediaService;

    public ExternalCompositionService(FfmpegMediaService mediaService)
    {
        _mediaService = mediaService;
    }

    public ExternalCompositionValidation Validate(ExternalCompositionSpec spec, string? rushframeProjectPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (spec.Kind == ExternalCompositionKind.Custom)
            errors.Add("Custom executable compositions are not allowed. Use the allowlisted Remotion or HyperFrames adapter.");
        if (string.IsNullOrWhiteSpace(spec.ProjectDirectory))
            errors.Add("Composition project directory is required.");
        var projectDirectory = ResolveDirectory(spec.ProjectDirectory, errors);
        if (projectDirectory != null && IsNetworkPath(projectDirectory))
            errors.Add("Composition projects must use a local drive, not a network or UNC path.");
        if (spec.Width < 2 || spec.Height < 2) errors.Add("Composition dimensions must be at least 2×2.");
        if (spec.DurationSeconds <= 0) errors.Add("Composition duration must be greater than zero.");
        if (spec.FrameRate.Numerator <= 0 || spec.FrameRate.Denominator <= 0) errors.Add("Composition frame rate is invalid.");

        string? outputPath = null;
        string? executablePath = null;
        if (projectDirectory != null)
        {
            outputPath = ResolveOutputPath(spec, projectDirectory, rushframeProjectPath, errors);
            executablePath = ResolveExecutable(spec.Kind, projectDirectory);
            if (executablePath == null)
                errors.Add($"The project-local {spec.Kind} executable is not installed. Install dependencies in the composition directory first; Rushframe will not download packages at runtime.");
            if (spec.Kind == ExternalCompositionKind.Remotion)
            {
                if (string.IsNullOrWhiteSpace(spec.EntryPoint)) errors.Add("Remotion entry point is required.");
                if (string.IsNullOrWhiteSpace(spec.CompositionId)) errors.Add("Remotion composition ID is required.");
            }
            if (spec.Kind == ExternalCompositionKind.HyperFrames && !File.Exists(Path.Combine(projectDirectory, "package.json")))
                warnings.Add("HyperFrames project has no package.json; verify that this is an initialized local composition.");
        }
        return new ExternalCompositionValidation(errors.Count == 0, projectDirectory, outputPath, executablePath, errors, warnings);
    }

    public async Task<ExternalCompositionRenderResult> RenderAsync(
        ExternalCompositionSpec spec,
        string? rushframeProjectPath,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(spec, rushframeProjectPath);
        if (!validation.Success || validation.ProjectDirectory == null || validation.OutputPath == null || validation.ExecutablePath == null)
        {
            spec.Status = ExternalCompositionStatus.Failed;
            spec.LastError = string.Join(" ", validation.Errors);
            return ExternalCompositionRenderResult.Fail(validation.Errors, validation.Warnings);
        }

        spec.Status = ExternalCompositionStatus.Rendering;
        spec.LastError = null;
        Directory.CreateDirectory(Path.GetDirectoryName(validation.OutputPath)!);
        var startInfo = new ProcessStartInfo
        {
            FileName = validation.ExecutablePath,
            WorkingDirectory = validation.ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        AddArguments(startInfo, spec, validation.OutputPath);

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException($"Failed to start the local {spec.Kind} renderer.");
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadBoundedAsync(process.StandardError, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            spec.Status = ExternalCompositionStatus.Failed;
            spec.LastError = "Composition render was canceled.";
            throw;
        }
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0 || !File.Exists(validation.OutputPath))
        {
            spec.Status = ExternalCompositionStatus.Failed;
            spec.LastError = string.IsNullOrWhiteSpace(stderr)
                ? $"{spec.Kind} exited with code {process.ExitCode}."
                : stderr;
            return ExternalCompositionRenderResult.Fail([spec.LastError], validation.Warnings, stdout, stderr);
        }

        var evidenceDirectory = Path.Combine(
            Path.GetDirectoryName(validation.OutputPath)!,
            ".rushframe-evidence",
            $"composition-{spec.Id}");
        var verification = await _mediaService.VerifyExportAsync(
            validation.OutputPath,
            spec.Width,
            spec.Height,
            spec.DurationSeconds,
            evidenceDirectory,
            evidenceTimestamps: null,
            cancellationToken);
        var hash = await ComputeFileHashAsync(validation.OutputPath, cancellationToken);
        spec.OutputPath = validation.OutputPath;
        spec.LastOutputSha256 = hash;
        spec.LastRenderedUtc = DateTimeOffset.UtcNow;
        spec.Status = verification.Status == MediaExportVerificationStatus.Failed
            ? ExternalCompositionStatus.Failed
            : ExternalCompositionStatus.Rendered;
        spec.LastError = verification.Status == MediaExportVerificationStatus.Failed
            ? string.Join(" ", verification.Errors)
            : null;
        return new ExternalCompositionRenderResult(
            verification.Status != MediaExportVerificationStatus.Failed,
            validation.OutputPath,
            hash,
            verification,
            validation.Errors,
            validation.Warnings,
            stdout,
            stderr);
    }

    private static void AddArguments(ProcessStartInfo startInfo, ExternalCompositionSpec spec, string outputPath)
    {
        switch (spec.Kind)
        {
            case ExternalCompositionKind.Remotion:
                startInfo.ArgumentList.Add("render");
                startInfo.ArgumentList.Add(spec.EntryPoint!);
                startInfo.ArgumentList.Add(spec.CompositionId!);
                startInfo.ArgumentList.Add(outputPath);
                startInfo.ArgumentList.Add("--width");
                startInfo.ArgumentList.Add(spec.Width.ToString());
                startInfo.ArgumentList.Add("--height");
                startInfo.ArgumentList.Add(spec.Height.ToString());
                startInfo.ArgumentList.Add("--fps");
                startInfo.ArgumentList.Add(spec.FrameRate.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                if (spec.TransparentBackground)
                {
                    startInfo.ArgumentList.Add("--codec");
                    startInfo.ArgumentList.Add("vp8");
                    startInfo.ArgumentList.Add("--image-format");
                    startInfo.ArgumentList.Add("png");
                }
                break;
            case ExternalCompositionKind.HyperFrames:
                startInfo.ArgumentList.Add("render");
                startInfo.ArgumentList.Add(".");
                startInfo.ArgumentList.Add("-o");
                startInfo.ArgumentList.Add(outputPath);
                if (spec.TransparentBackground)
                {
                    startInfo.ArgumentList.Add("--format");
                    startInfo.ArgumentList.Add("webm");
                }
                break;
            default:
                throw new InvalidOperationException("Custom composition execution is blocked.");
        }
    }

    private static string? ResolveDirectory(string value, ICollection<string> errors)
    {
        try
        {
            var path = Path.GetFullPath(value.Trim());
            if (!Directory.Exists(path)) errors.Add("Composition project directory does not exist.");
            return path;
        }
        catch (Exception ex)
        {
            errors.Add($"Composition project directory is invalid: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveOutputPath(
        ExternalCompositionSpec spec,
        string projectDirectory,
        string? rushframeProjectPath,
        ICollection<string> errors)
    {
        try
        {
            var output = string.IsNullOrWhiteSpace(spec.OutputPath)
                ? Path.Combine(projectDirectory, "renders", $"{SanitizeFileName(spec.Name)}.{(spec.TransparentBackground ? "webm" : "mp4")}")
                : Path.IsPathRooted(spec.OutputPath)
                    ? Path.GetFullPath(spec.OutputPath)
                    : Path.GetFullPath(Path.Combine(projectDirectory, spec.OutputPath));
            var allowedRoots = new List<string> { projectDirectory };
            if (!string.IsNullOrWhiteSpace(rushframeProjectPath))
            {
                var rushframeDirectory = Path.GetDirectoryName(Path.GetFullPath(rushframeProjectPath));
                if (!string.IsNullOrWhiteSpace(rushframeDirectory)) allowedRoots.Add(rushframeDirectory);
            }
            if (!allowedRoots.Any(root => IsContained(root, output)))
            {
                errors.Add("Composition output must stay inside the composition directory or the saved Rushframe project directory.");
                return null;
            }
            if (IsNetworkPath(output)) errors.Add("Composition output must use a local drive.");
            return output;
        }
        catch (Exception ex)
        {
            errors.Add($"Composition output path is invalid: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveExecutable(ExternalCompositionKind kind, string projectDirectory)
    {
        var fileName = kind switch
        {
            ExternalCompositionKind.Remotion => OperatingSystem.IsWindows() ? "remotion.cmd" : "remotion",
            ExternalCompositionKind.HyperFrames => OperatingSystem.IsWindows() ? "hyperframes.cmd" : "hyperframes",
            _ => string.Empty,
        };
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var candidate = Path.Combine(projectDirectory, "node_modules", ".bin", fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool IsContained(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNetworkPath(string path) =>
        path.StartsWith("\\\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal)
        || new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).DriveType == DriveType.Network;

    private static string SanitizeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "composition" : value.Trim();
        foreach (var character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '-');
        return name;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var result = new System.Text.StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            result.Append(buffer, 0, read);
            if (result.Length > MaximumDiagnosticCharacters)
                result.Remove(0, result.Length - MaximumDiagnosticCharacters);
        }
        return result.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = SHA256.Create();
        return Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}

public sealed record ExternalCompositionValidation(
    bool Success,
    string? ProjectDirectory,
    string? OutputPath,
    string? ExecutablePath,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

public sealed record ExternalCompositionRenderResult(
    bool Success,
    string? OutputPath,
    string? OutputSha256,
    MediaExportVerificationReport? Verification,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    string StandardOutput,
    string StandardError)
{
    public static ExternalCompositionRenderResult Fail(
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        string stdout = "",
        string stderr = "") =>
        new(false, null, null, null, errors, warnings, stdout, stderr);
}
