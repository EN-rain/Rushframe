using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

internal sealed record SoundLibraryCatalogQuery(
    string Query = "",
    int Limit = 50,
    double? MaxDuration = null,
    double? MinLufs = null,
    double? MaxLufs = null,
    double? MinTempo = null,
    double? MaxTempo = null,
    string? Category = null,
    string? Mood = null,
    string? License = null,
    bool FavoritesOnly = false,
    bool IncludeOffline = false,
    bool LexicalOnly = false,
    string? SimilarToSoundId = null,
    string? CollectionId = null,
    string? ProjectId = null,
    bool RecentlyUsed = false)
{
    public bool MatchesProjectFallback(MediaAsset asset)
    {
        if (asset.Kind != MediaKind.Audio || FavoritesOnly || RecentlyUsed
            || !string.IsNullOrWhiteSpace(CollectionId) || !string.IsNullOrWhiteSpace(ProjectId)
            || MaxDuration.HasValue || MinLufs.HasValue || MaxLufs.HasValue
            || MinTempo.HasValue || MaxTempo.HasValue
            || !string.IsNullOrWhiteSpace(Category) || !string.IsNullOrWhiteSpace(Mood)
            || !string.IsNullOrWhiteSpace(License) || !string.IsNullOrWhiteSpace(SimilarToSoundId))
            return false;

        var query = Query?.Trim();
        return string.IsNullOrWhiteSpace(query)
               || Path.GetFileNameWithoutExtension(asset.OriginalPath)
                   .Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class SoundLibraryCatalogEntry
{
    [JsonPropertyName("sound_id")]
    public string SoundId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("content_hash")]
    public string ContentHash { get; init; } = string.Empty;

    [JsonPropertyName("duration")]
    public double Duration { get; init; }

    [JsonPropertyName("codec")]
    public string Codec { get; init; } = string.Empty;

    [JsonPropertyName("channels")]
    public int? Channels { get; init; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; init; }

    [JsonPropertyName("lufs")]
    public double? Lufs { get; init; }

    [JsonPropertyName("peak_db")]
    public double? PeakDb { get; init; }

    [JsonPropertyName("leading_silence")]
    public double? LeadingSilence { get; init; }

    [JsonPropertyName("trailing_silence")]
    public double? TrailingSilence { get; init; }

    [JsonPropertyName("category")]
    public string Category { get; init; } = "other";

    [JsonPropertyName("mood")]
    public string Mood { get; init; } = "neutral";

    [JsonPropertyName("tempo_bpm")]
    public double? TempoBpm { get; init; }

    [JsonPropertyName("license_name")]
    public string LicenseName { get; init; } = string.Empty;

    [JsonPropertyName("attribution")]
    public string Attribution { get; init; } = string.Empty;

    [JsonPropertyName("requires_attribution")]
    public bool RequiresAttribution { get; init; }

    [JsonPropertyName("favorite")]
    public bool Favorite { get; init; }

    [JsonPropertyName("offline")]
    public bool Offline { get; init; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = [];

    [JsonPropertyName("derivative_path")]
    public string? DerivativePath { get; init; }

    [JsonPropertyName("waveform_path")]
    public string? WaveformPath { get; init; }

    [JsonPropertyName("completed_features")]
    public List<string> CompletedFeatures { get; init; } = [];

    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("lexical_score")]
    public double LexicalScore { get; init; }

    [JsonPropertyName("semantic_score")]
    public double SemanticScore { get; init; }

    [JsonPropertyName("embedding_provider")]
    public string? EmbeddingProvider { get; init; }

    [JsonPropertyName("indexed_utc")]
    public string IndexedUtc { get; init; } = string.Empty;
}

internal sealed class SoundLibraryCatalogSearchResponse
{
    [JsonPropertyName("results")]
    public List<SoundLibraryCatalogEntry> Results { get; init; } = [];

    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "lexical";

    [JsonPropertyName("embedding_provider")]
    public string? EmbeddingProvider { get; init; }

    [JsonPropertyName("semantic_available")]
    public bool SemanticAvailable { get; init; }

    [JsonPropertyName("warning")]
    public string? Warning { get; init; }
}

internal sealed class SoundLibraryCatalogRoot
{
    [JsonPropertyName("root_id")]
    public string RootId { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("watch_enabled")]
    public bool WatchEnabled { get; init; }

    [JsonPropertyName("added_utc")]
    public string AddedUtc { get; init; } = string.Empty;
}

internal sealed class SoundLibraryCollection
{
    [JsonPropertyName("collection_id")]
    public string CollectionId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("project_id")]
    public string ProjectId { get; init; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public string CreatedUtc { get; init; } = string.Empty;

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(ProjectId)
        ? $"{Name} ({ItemCount})"
        : $"{Name} ({ItemCount}, project)";
}

internal sealed class SoundLibraryCatalogStatus
{
    [JsonPropertyName("catalog_path")]
    public string CatalogPath { get; init; } = string.Empty;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("sound_count")]
    public int SoundCount { get; init; }

    [JsonPropertyName("online_count")]
    public int OnlineCount { get; init; }

    [JsonPropertyName("favorite_count")]
    public int FavoriteCount { get; init; }

    [JsonPropertyName("root_count")]
    public int RootCount { get; init; }

    [JsonPropertyName("roots")]
    public List<SoundLibraryCatalogRoot> Roots { get; init; } = [];

    [JsonPropertyName("embedding_providers")]
    public List<string> EmbeddingProviders { get; init; } = [];

    [JsonPropertyName("preferred_embedding_provider")]
    public string? PreferredEmbeddingProvider { get; init; }
}

internal sealed class SoundLibraryIndexWarning
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

internal sealed class SoundLibraryIndexResult
{
    [JsonPropertyName("indexed")]
    public List<string> Indexed { get; init; } = [];

    [JsonPropertyName("duplicates")]
    public List<string> Duplicates { get; init; } = [];

    [JsonPropertyName("skipped")]
    public List<string> Skipped { get; init; } = [];

    [JsonPropertyName("warnings")]
    public List<SoundLibraryIndexWarning> Warnings { get; init; } = [];

    [JsonPropertyName("roots")]
    public List<SoundLibraryCatalogRoot> Roots { get; init; } = [];

    [JsonPropertyName("embedding_provider")]
    public string EmbeddingProvider { get; init; } = string.Empty;
}

internal sealed class SoundLibraryCatalogService
{
    private const int MaximumOutputCharacters = 4 * 1024 * 1024;
    private const int MaximumErrorCharacters = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _repositoryRoot;
    private readonly SemaphoreSlim _processGate = new(1, 1);

    public SoundLibraryCatalogService(string repositoryRoot, string catalogPath)
    {
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        CatalogPath = Path.GetFullPath(catalogPath);
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
    }

    public string CatalogPath { get; }

    public async Task<SoundLibraryCatalogSearchResponse> SearchAsync(
        SoundLibraryCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-m", "rushframe_intelligence", "search-sfx", query.Query ?? string.Empty,
            "--catalog", CatalogPath,
            "--limit", Math.Clamp(query.Limit, 1, 50).ToString(CultureInfo.InvariantCulture),
        };
        AddOptionalNumber(args, "--max-duration", query.MaxDuration);
        AddOptionalNumber(args, "--min-lufs", query.MinLufs);
        AddOptionalNumber(args, "--max-lufs", query.MaxLufs);
        AddOptionalNumber(args, "--min-tempo", query.MinTempo);
        AddOptionalNumber(args, "--max-tempo", query.MaxTempo);
        AddOptionalText(args, "--category", query.Category);
        AddOptionalText(args, "--mood", query.Mood);
        AddOptionalText(args, "--license", query.License);
        AddOptionalText(args, "--similar-to", query.SimilarToSoundId);
        AddOptionalText(args, "--collection-id", query.CollectionId);
        AddOptionalText(args, "--project-id", query.ProjectId);
        if (query.RecentlyUsed) args.Add("--recently-used");
        if (query.FavoritesOnly) args.Add("--favorites-only");
        if (query.IncludeOffline) args.Add("--include-offline");
        if (query.LexicalOnly) args.Add("--lexical-only");
        return await RunJsonAsync<SoundLibraryCatalogSearchResponse>(args, cancellationToken);
    }

    public Task<SoundLibraryCatalogStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        RunJsonAsync<SoundLibraryCatalogStatus>(
            ["-m", "rushframe_intelligence", "sound-library-status", "--catalog", CatalogPath],
            cancellationToken);

    public Task<SoundLibraryCatalogEntry> GetSoundAsync(
        string soundId,
        CancellationToken cancellationToken = default) =>
        RunJsonAsync<SoundLibraryCatalogEntry>(
            [
                "-m", "rushframe_intelligence", "sound-library-get",
                "--catalog", CatalogPath,
                "--sound-id", soundId,
            ],
            cancellationToken);

    public Task<SoundLibraryCatalogEntry> GetSoundByPathAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        RunJsonAsync<SoundLibraryCatalogEntry>(
            [
                "-m", "rushframe_intelligence", "sound-library-get",
                "--catalog", CatalogPath,
                "--path", Path.GetFullPath(path),
            ],
            cancellationToken);

    public Task<SoundLibraryIndexResult> IndexFilesAsync(
        IReadOnlyList<string> paths,
        string? ffmpegPath,
        string? ffprobePath,
        bool enableSemantic,
        CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0) throw new ArgumentException("At least one sound path is required", nameof(paths));
        var args = CreateIndexArguments(ffmpegPath, ffprobePath, enableSemantic);
        foreach (var path in paths)
        {
            args.Add("--path");
            args.Add(Path.GetFullPath(path));
        }
        return RunJsonAsync<SoundLibraryIndexResult>(args, cancellationToken);
    }

    public Task<SoundLibraryIndexResult> IndexRootAsync(
        string root,
        string? ffmpegPath,
        string? ffprobePath,
        bool enableSemantic,
        bool watchEnabled = true,
        CancellationToken cancellationToken = default)
    {
        var args = CreateIndexArguments(ffmpegPath, ffprobePath, enableSemantic);
        args.Add("--root");
        args.Add(Path.GetFullPath(root));
        if (!watchEnabled) args.Add("--no-watch");
        return RunJsonAsync<SoundLibraryIndexResult>(args, cancellationToken);
    }

    public Task SetFavoriteAsync(
        string soundId,
        bool favorite,
        CancellationToken cancellationToken = default) =>
        RunJsonAsync<JsonElement>(
            [
                "-m", "rushframe_intelligence", "sound-library-favorite", soundId,
                "--catalog", CatalogPath,
                "--value", favorite ? "true" : "false",
            ],
            cancellationToken);

    public Task UpdateLicenseAsync(
        string soundId,
        string licenseName,
        string attribution,
        bool requiresAttribution,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-m", "rushframe_intelligence", "sound-library-license", soundId,
            "--catalog", CatalogPath,
            "--license", licenseName ?? string.Empty,
            "--attribution", attribution ?? string.Empty,
        };
        if (requiresAttribution) args.Add("--requires-attribution");
        return RunJsonAsync<JsonElement>(args, cancellationToken);
    }

    public Task RecordUsageAsync(
        string soundId,
        string projectId,
        string mediaAssetId,
        CancellationToken cancellationToken = default) =>
        RunJsonAsync<JsonElement>(
            [
                "-m", "rushframe_intelligence", "sound-library-usage",
                soundId, projectId, mediaAssetId,
                "--catalog", CatalogPath,
            ],
            cancellationToken);

    public Task<List<SoundLibraryCollection>> ListCollectionsAsync(
        string? projectId,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-m", "rushframe_intelligence", "sound-library-collections",
            "--catalog", CatalogPath,
        };
        AddOptionalText(args, "--project-id", projectId);
        return RunJsonAsync<List<SoundLibraryCollection>>(args, cancellationToken);
    }

    public async Task<string> CreateCollectionAsync(
        string name,
        string? projectId,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>
        {
            "-m", "rushframe_intelligence", "sound-library-create-collection", name,
            "--catalog", CatalogPath,
        };
        AddOptionalText(args, "--project-id", projectId);
        var response = await RunJsonAsync<CollectionMutationResponse>(args, cancellationToken);
        if (string.IsNullOrWhiteSpace(response.CollectionId))
            throw new InvalidOperationException("Sound-library worker did not return a collection ID.");
        return response.CollectionId;
    }

    public Task DeleteCollectionAsync(
        string collectionId,
        CancellationToken cancellationToken = default) =>
        RunJsonAsync<JsonElement>(
            [
                "-m", "rushframe_intelligence", "sound-library-delete-collection", collectionId,
                "--catalog", CatalogPath,
            ],
            cancellationToken);

    public Task AddToCollectionAsync(
        string collectionId,
        string soundId,
        CancellationToken cancellationToken = default) =>
        RunJsonAsync<JsonElement>(
            [
                "-m", "rushframe_intelligence", "sound-library-add-to-collection",
                collectionId, soundId,
                "--catalog", CatalogPath,
            ],
            cancellationToken);

    public Task RemoveFromCollectionAsync(
        string collectionId,
        string soundId,
        CancellationToken cancellationToken = default) =>
        RunJsonAsync<JsonElement>(
            [
                "-m", "rushframe_intelligence", "sound-library-remove-from-collection",
                collectionId, soundId,
                "--catalog", CatalogPath,
            ],
            cancellationToken);

    private List<string> CreateIndexArguments(
        string? ffmpegPath,
        string? ffprobePath,
        bool enableSemantic)
    {
        var args = new List<string>
        {
            "-m", "rushframe_intelligence", "index-sfx",
            "--catalog", CatalogPath,
        };
        AddOptionalText(args, "--ffmpeg", ffmpegPath);
        AddOptionalText(args, "--ffprobe", ffprobePath);
        if (!enableSemantic) args.Add("--no-clap");
        return args;
    }

    private async Task<T> RunJsonAsync<T>(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        await _processGate.WaitAsync(cancellationToken);
        try
        {
            var result = await RunPythonAsync(arguments, cancellationToken);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.StandardError)
                        ? $"Sound-library worker failed with exit code {result.ExitCode}."
                        : result.StandardError.Trim());
            if (result.StandardOutput.Length > MaximumOutputCharacters)
                throw new InvalidOperationException("Sound-library worker returned too much data.");
            return JsonSerializer.Deserialize<T>(result.StandardOutput, JsonOptions)
                   ?? throw new InvalidOperationException("Sound-library worker returned invalid JSON.");
        }
        finally
        {
            _processGate.Release();
        }
    }

    private async Task<ProcessResult> RunPythonAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var managedPython = Path.Combine(_repositoryRoot, ".tools", "intelligence-venv", "Scripts", "python.exe");
        foreach (var launcher in new[] { managedPython, "py", "python" })
        {
            if (Path.IsPathFullyQualified(launcher) && !File.Exists(launcher)) continue;
            var startInfo = new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = _repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (launcher == "py") startInfo.ArgumentList.Add("-3");
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            try
            {
                using var process = Process.Start(startInfo)
                                    ?? throw new InvalidOperationException("Python sound-library worker did not start.");
                var stdout = ReadBoundedAsync(process.StandardOutput, MaximumOutputCharacters, cancellationToken);
                var stderr = ReadBoundedAsync(process.StandardError, MaximumErrorCharacters, cancellationToken);
                try
                {
                    await Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdout, stderr);
                }
                catch
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                    throw;
                }
                return new ProcessResult(process.ExitCode, await stdout, await stderr);
            }
            catch (Win32Exception)
            {
                continue;
            }
        }
        throw new InvalidOperationException("Python was not found. Install Rushframe intelligence support or add Python to PATH.");
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var result = new System.Text.StringBuilder(Math.Min(maximumCharacters, 64 * 1024));
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return result.ToString();
            if (result.Length > maximumCharacters - read)
                throw new InvalidOperationException("Sound-library worker returned too much diagnostic data.");
            result.Append(buffer, 0, read);
        }
    }

    private static void AddOptionalText(List<string> args, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        args.Add(name);
        args.Add(value);
    }

    private static void AddOptionalNumber(List<string> args, string name, double? value)
    {
        if (!value.HasValue || !double.IsFinite(value.Value)) return;
        args.Add(name);
        args.Add(value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private sealed class CollectionMutationResponse
    {
        [JsonPropertyName("collection_id")]
        public string CollectionId { get; init; } = string.Empty;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
