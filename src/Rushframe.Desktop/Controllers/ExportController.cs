using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Rushframe.Desktop.Dialogs;
using Rushframe.Desktop.Services;
using Rushframe.Domain;
using Rushframe.Media.Abstractions;
using Rushframe.Media.Native;

namespace Rushframe.Desktop.Controllers;

internal sealed class ExportController
{
    private const double HardOutputLimitSeconds = 180;
    private readonly Window _owner;
    private readonly FfmpegMediaService _mediaService;
    private readonly RenderReceiptService _receiptService;

    public ExportController(Window owner, FfmpegMediaService mediaService)
    {
        _owner = owner;
        _mediaService = mediaService;
        _receiptService = new RenderReceiptService(mediaService);
    }

    public async Task ExportAsync(
        Project project,
        Sequence sequence,
        CancellationToken cancellationToken,
        IProgress<MediaJobProgress> progress,
        Action<string> addActivity,
        Action<string> setStatus,
        Action<string> markProjectDirty)
    {
        if (sequence.Duration.Seconds > HardOutputLimitSeconds + 0.001)
        {
            MessageBox.Show(
                _owner,
                $"The timeline is {FormatDuration(sequence.Duration.Seconds)}. Rushframe output is limited to 03:00. Trim or remove content after 3 minutes before exporting.",
                "Output Too Long",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            setStatus("Export blocked: timeline exceeds the 3-minute limit");
            return;
        }

        var settings = new ExportSettingsDialog(_owner, sequence, ResolvePreviewAsset(project, sequence)).Show();
        if (settings == null) return;

        var (filter, extension) = settings.Options.Format switch
        {
            TimelineExportFormat.WebM => ("WebM Video (*.webm)|*.webm", ".webm"),
            TimelineExportFormat.Mov => ("QuickTime Video (*.mov)|*.mov", ".mov"),
            TimelineExportFormat.Mkv => ("Matroska Video (*.mkv)|*.mkv", ".mkv"),
            _ => ("MP4 Video (*.mp4)|*.mp4", ".mp4"),
        };
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"{project.Name}{extension}",
        };
        if (dialog.ShowDialog(_owner) != true) return;

        addActivity($"Export started: {Path.GetFileName(dialog.FileName)} ({settings.Width}×{settings.Height}, {settings.Options.Quality})");
        try
        {
            await _mediaService.ExportTimelineAsync(
                project,
                sequence,
                dialog.FileName,
                progress,
                cancellationToken,
                settings.Width,
                settings.Height,
                settings.Options);

            addActivity($"Render complete; verifying output: {Path.GetFileName(dialog.FileName)}");
            setStatus("Verifying export and writing render receipt…");
            var variant = project.ExportVariants.FirstOrDefault(candidate =>
                candidate.SequenceId == sequence.Id
                && candidate.Width == settings.Width
                && candidate.Height == settings.Height);
            var receipt = await _receiptService.CreateAsync(
                project,
                sequence,
                dialog.FileName,
                settings.Width,
                settings.Height,
                settings.Options,
                approvalSource: "manual-export-dialog",
                variantId: variant?.Id,
                cancellationToken);
            project.IncrementRevision();
            markProjectDirty("Render receipt and QA results added");
            addActivity($"Export verification {receipt.Status}: {Path.GetFileName(receipt.ReceiptPath)}");
            var resultText = receipt.Status == RenderVerificationStatus.Failed
                ? $"Export rendered, but verification failed.\n\nVideo:\n{dialog.FileName}\n\nReceipt:\n{receipt.ReceiptPath}\n\nReview the receipt before publishing."
                : $"Export complete and verified: {receipt.Status}.\n\nVideo:\n{dialog.FileName}\n\nReceipt:\n{receipt.ReceiptPath}\n\nOpen the containing folder?";
            var answer = MessageBox.Show(
                _owner,
                resultText,
                receipt.Status == RenderVerificationStatus.Failed ? "Export Verification Failed" : "Export Complete",
                receipt.Status == RenderVerificationStatus.Failed ? MessageBoxButton.OK : MessageBoxButton.YesNo,
                receipt.Status == RenderVerificationStatus.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{dialog.FileName}\"")
                {
                    UseShellExecute = true,
                });
            }
        }
        catch (OperationCanceledException)
        {
            addActivity($"Export canceled: {Path.GetFileName(dialog.FileName)}");
            setStatus("Export canceled");
            throw;
        }
        catch (Exception ex)
        {
            addActivity($"Export failed: {ex.Message}");
            MessageBox.Show(_owner, $"Render failed:\n{ex.Message}", "Render Error", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private static string FormatDuration(double seconds)
    {
        var value = TimeSpan.FromSeconds(seconds);
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private static MediaAsset? ResolvePreviewAsset(Project project, Sequence sequence)
    {
        var assetIds = sequence.Tracks
            .SelectMany(track => track.Items)
            .Where(item => item.MediaAssetId.HasValue)
            .OrderBy(item => item.TimelineStart.Seconds)
            .Select(item => item.MediaAssetId!.Value);

        foreach (var assetId in assetIds)
        {
            var asset = project.MediaLibrary.FirstOrDefault(candidate => candidate.Id == assetId);
            if (asset is { Kind: MediaKind.Video or MediaKind.Image } && File.Exists(asset.OriginalPath))
                return asset;
        }

        return null;
    }
}
