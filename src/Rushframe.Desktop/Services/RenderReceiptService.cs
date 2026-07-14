using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rushframe.Domain;
using Rushframe.Domain.Serialization;
using Rushframe.Media.Abstractions;
using Rushframe.Media.Native;

namespace Rushframe.Desktop.Services;

public sealed class RenderReceiptService
{
    private readonly FfmpegMediaService _mediaService;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public RenderReceiptService(FfmpegMediaService mediaService)
    {
        _mediaService = mediaService;
    }

    public async Task<RenderReceiptDocument> CreateAsync(
        Project project,
        Sequence sequence,
        string outputPath,
        int width,
        int height,
        TimelineExportOptions options,
        string approvalSource,
        string? variantId = null,
        CancellationToken cancellationToken = default)
    {
        var outputHash = await ComputeFileHashAsync(outputPath, cancellationToken);
        var evidenceDirectory = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(outputPath))!,
            ".rushframe-evidence",
            $"{Path.GetFileNameWithoutExtension(outputPath)}-{outputHash[..12].ToLowerInvariant()}");
        var verification = await _mediaService.VerifyExportAsync(
            outputPath,
            width,
            height,
            sequence.Duration.Seconds,
            evidenceDirectory,
            BuildEvidenceTimestamps(sequence),
            cancellationToken);
        var runtime = await _mediaService.GetRuntimeVersionInfoAsync(cancellationToken);
        var sourceAssets = await BuildSourceRecordsAsync(project, sequence, cancellationToken);
        var timelineWarnings = AnalyzeTimeline(project, sequence, width, height, variantId);
        var projectSnapshot = ProjectSerializer.Serialize(project);
        var receipt = new RenderReceiptDocument
        {
            ReceiptId = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTimeOffset.UtcNow,
            RushframeVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            ProjectId = project.Id.ToString(),
            ProjectName = project.Name,
            ProjectRevision = project.Revision,
            ProjectGraphSha256 = ComputeHash(Encoding.UTF8.GetBytes(projectSnapshot)),
            SequenceId = sequence.Id.ToString(),
            SequenceName = sequence.Name,
            VariantId = variantId,
            ApprovalSource = approvalSource,
            Output = new RenderOutputRecord
            {
                Path = Path.GetFullPath(outputPath),
                Sha256 = outputHash,
                SizeBytes = new FileInfo(outputPath).Length,
                Width = width,
                Height = height,
                DurationSeconds = verification.DurationSeconds,
                Format = options.Format.ToString(),
                Quality = options.Quality.ToString(),
            },
            Runtime = new RenderRuntimeRecord
            {
                OperatingSystem = Environment.OSVersion.ToString(),
                Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                DotNet = Environment.Version.ToString(),
                Ffmpeg = runtime.Ffmpeg,
                Ffprobe = runtime.Ffprobe,
            },
            Sources = sourceAssets,
            Verification = verification,
            TimelineWarnings = timelineWarnings,
        };
        receipt.Status = verification.Status switch
        {
            MediaExportVerificationStatus.Passed => RenderVerificationStatus.Passed,
            MediaExportVerificationStatus.PassedWithWarnings => RenderVerificationStatus.PassedWithWarnings,
            _ => RenderVerificationStatus.Failed,
        };

        var receiptPath = outputPath + ".rushframe-receipt.json";
        receipt.ReceiptPath = Path.GetFullPath(receiptPath);
        var temporaryPath = receiptPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken);
        File.Move(temporaryPath, receiptPath, overwrite: true);

        project.RenderReceipts.Add(new RenderReceiptReference
        {
            ReceiptId = receipt.ReceiptId,
            OutputPath = Path.GetFullPath(outputPath),
            ReceiptPath = Path.GetFullPath(receiptPath),
            VariantId = variantId,
            ProjectRevision = project.Revision,
            OutputSha256 = outputHash,
            VerificationStatus = receipt.Status,
            CreatedUtc = receipt.CreatedUtc,
        });
        if (!string.IsNullOrWhiteSpace(variantId))
        {
            var variant = project.ExportVariants.FirstOrDefault(candidate => candidate.Id == variantId);
            if (variant != null)
            {
                variant.LastOutputPath = Path.GetFullPath(outputPath);
                variant.LastReceiptPath = Path.GetFullPath(receiptPath);
                variant.LastRenderedUtc = receipt.CreatedUtc;
                variant.Status = receipt.Status == RenderVerificationStatus.Failed
                    ? ExportVariantStatus.Failed
                    : ExportVariantStatus.Completed;
            }
        }
        UpdateWorkflow(project, receipt);
        return receipt;
    }

    private static IReadOnlyList<double> BuildEvidenceTimestamps(Sequence sequence)
    {
        var duration = sequence.Duration.Seconds;
        var frameDuration = 1.0 / Math.Max(1, sequence.FrameRate.Value);
        var boundaries = sequence.Tracks
            .SelectMany(track => track.Items)
            .SelectMany(item => new[] { item.TimelineStart.Seconds, item.TimelineEnd.Seconds })
            .Concat(sequence.Transitions.SelectMany(transition =>
            {
                var left = sequence.Tracks.SelectMany(track => track.Items).FirstOrDefault(item => item.Id == transition.LeftItemId);
                return left == null ? [] : new[] { left.TimelineEnd.Seconds };
            }))
            .Where(value => value > frameDuration && value < duration - frameDuration)
            .DistinctBy(value => Math.Round(value, 4))
            .OrderBy(value => value)
            .Take(30)
            .SelectMany(value => new[] { value - frameDuration, value, value + frameDuration })
            .Where(value => value > 0 && value < duration)
            .DistinctBy(value => Math.Round(value, 4))
            .ToArray();
        return boundaries;
    }

    private static void UpdateWorkflow(Project project, RenderReceiptDocument receipt)
    {
        project.Workflow.EnsureDefaults();
        var qa = project.Workflow.Stages.FirstOrDefault(stage => stage.Id == "final_qa");
        if (qa != null)
        {
            qa.StartedUtc ??= receipt.CreatedUtc;
            qa.CompletedUtc = receipt.CreatedUtc;
            qa.Status = receipt.Status == RenderVerificationStatus.Failed
                ? ProductionStageStatus.Failed
                : ProductionStageStatus.Completed;
            qa.Summary = $"Render verification: {receipt.Status}";
            qa.Outputs.Clear();
            qa.Outputs.Add($"receipt:{receipt.ReceiptId}");
            qa.ArtifactPaths.Clear();
            qa.ArtifactPaths.Add(receipt.ReceiptPath);
            qa.Warnings.Clear();
            qa.Warnings.AddRange(receipt.TimelineWarnings);
            qa.Warnings.AddRange(receipt.Verification.Warnings);
            qa.Warnings.AddRange(receipt.Verification.Errors);
            qa.Revision++;
        }
        var export = project.Workflow.Stages.FirstOrDefault(stage => stage.Id == "export");
        if (export != null)
        {
            export.StartedUtc ??= receipt.CreatedUtc;
            export.CompletedUtc = receipt.CreatedUtc;
            export.Status = receipt.Status == RenderVerificationStatus.Failed
                ? ProductionStageStatus.Failed
                : ProductionStageStatus.Completed;
            export.Summary = Path.GetFileName(receipt.Output.Path);
            export.Outputs.Clear();
            export.Outputs.Add(receipt.Output.Path);
            export.ArtifactPaths.Clear();
            export.ArtifactPaths.Add(receipt.ReceiptPath);
            export.Revision++;
        }
        project.Workflow.ActiveStageId = receipt.Status == RenderVerificationStatus.Failed ? "final_qa" : "export";
    }

    private static async Task<List<RenderSourceRecord>> BuildSourceRecordsAsync(
        Project project,
        Sequence sequence,
        CancellationToken cancellationToken)
    {
        var ids = sequence.Tracks
            .SelectMany(track => track.Items)
            .Where(item => item.MediaAssetId.HasValue)
            .Select(item => item.MediaAssetId!.Value)
            .Distinct()
            .ToArray();
        var records = new List<RenderSourceRecord>(ids.Length);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == id);
            if (asset == null)
            {
                records.Add(new RenderSourceRecord { MediaAssetId = id.ToString(), Offline = true, Error = "Asset missing from project library" });
                continue;
            }
            var record = new RenderSourceRecord
            {
                MediaAssetId = id.ToString(),
                Path = asset.OriginalPath,
                Kind = asset.Kind.ToString(),
                Offline = asset.IsOffline || !File.Exists(asset.OriginalPath),
            };
            if (!record.Offline)
            {
                try
                {
                    record.SizeBytes = new FileInfo(asset.OriginalPath).Length;
                    record.Sha256 = await ComputeFileHashAsync(asset.OriginalPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    record.Error = ex.Message;
                }
            }
            records.Add(record);
        }
        return records;
    }

    private static List<string> AnalyzeTimeline(Project project, Sequence sequence, int width, int height, string? variantId)
    {
        var warnings = new List<string>();
        var variant = string.IsNullOrWhiteSpace(variantId)
            ? project.ExportVariants.FirstOrDefault(candidate => candidate.SequenceId == sequence.Id)
            : project.ExportVariants.FirstOrDefault(candidate => candidate.Id == variantId);
        foreach (var track in sequence.Tracks)
        {
            foreach (var item in track.Items)
            {
                var location = $"{track.Name}/{item.Id}";
                if (item.Duration.Seconds <= 0) warnings.Add($"{location}: duration is zero or negative.");
                if (item.TimelineStart.Seconds < 0) warnings.Add($"{location}: starts before timeline zero.");
                if (item.FadeInDuration.Seconds + item.FadeOutDuration.Seconds > item.Duration.Seconds + 0.001)
                    warnings.Add($"{location}: audio fades exceed clip duration.");
                if (item.MediaAssetId is { } assetId)
                {
                    var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == assetId);
                    if (asset == null || asset.IsOffline || !File.Exists(asset.OriginalPath))
                        warnings.Add($"{location}: source media is offline.");
                    if (asset?.Kind == MediaKind.Audio
                        && (item.ColorCorrection != null || item.Masks.Count > 0 || item.Stabilization?.Enabled == true))
                        warnings.Add($"{location}: audio-only media contains visual modifiers.");
                }
                if (item.Kind == ItemKind.Text)
                    AnalyzeTextItem(item, location, width, height, variant, warnings);
                foreach (var effect in item.Effects.Where(effect => effect.Enabled && !FfmpegMediaService.SupportedEffectTypeIds.Contains(effect.EffectTypeId)))
                    warnings.Add($"{location}: effect '{effect.EffectTypeId}' is not supported by the exact renderer.");
                foreach (var mask in item.Masks.Where(mask => !FfmpegMediaService.SupportedMaskShapes.Contains(mask.Shape)))
                    warnings.Add($"{location}: mask '{mask.Shape}' is not supported by the exact renderer.");
            }
        }
        return warnings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void AnalyzeTextItem(
        TimelineItem item,
        string location,
        int width,
        int height,
        ExportVariant? variant,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(item.TextContent)) warnings.Add($"{location}: text content is empty.");
        var centerX = width / 2.0 + item.Transform.PositionX;
        var centerY = height / 2.0 + item.Transform.PositionY;
        var estimatedHalfWidth = Math.Min(width, Math.Max(item.FontSize, (item.TextContent?.Length ?? 1) * item.FontSize * 0.28));
        var estimatedHalfHeight = item.FontSize * 0.75;
        if (centerX - estimatedHalfWidth < 0 || centerX + estimatedHalfWidth > width
            || centerY - estimatedHalfHeight < 0 || centerY + estimatedHalfHeight > height)
            warnings.Add($"{location}: text may be clipped outside the canvas.");
        if (variant == null) return;
        var safeLeft = width * variant.SafeAreaLeftPercent / 100.0;
        var safeRight = width * (1 - variant.SafeAreaRightPercent / 100.0);
        var safeTop = height * variant.SafeAreaTopPercent / 100.0;
        var safeBottom = height * (1 - variant.SafeAreaBottomPercent / 100.0);
        if (centerX - estimatedHalfWidth < safeLeft || centerX + estimatedHalfWidth > safeRight
            || centerY - estimatedHalfHeight < safeTop || centerY + estimatedHalfHeight > safeBottom)
            warnings.Add($"{location}: text may overlap the {variant.Name} platform safe-area exclusion.");
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var algorithm = SHA256.Create();
        var hash = await algorithm.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeHash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

public sealed class RenderReceiptDocument
{
    public string ReceiptId { get; set; } = string.Empty;
    public string ReceiptPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string RushframeVersion { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public long ProjectRevision { get; set; }
    public string ProjectGraphSha256 { get; set; } = string.Empty;
    public string SequenceId { get; set; } = string.Empty;
    public string SequenceName { get; set; } = string.Empty;
    public string? VariantId { get; set; }
    public string ApprovalSource { get; set; } = string.Empty;
    public RenderVerificationStatus Status { get; set; }
    public RenderOutputRecord Output { get; set; } = new();
    public RenderRuntimeRecord Runtime { get; set; } = new();
    public List<RenderSourceRecord> Sources { get; set; } = [];
    public MediaExportVerificationReport Verification { get; set; } = new();
    public List<string> TimelineWarnings { get; set; } = [];
}

public sealed class RenderOutputRecord
{
    public string Path { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public string Format { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
}

public sealed class RenderRuntimeRecord
{
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public string DotNet { get; set; } = string.Empty;
    public string Ffmpeg { get; set; } = string.Empty;
    public string Ffprobe { get; set; } = string.Empty;
}

public sealed class RenderSourceRecord
{
    public string MediaAssetId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool Offline { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string? Error { get; set; }
}
