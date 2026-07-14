using System.IO;
using Rushframe.Application;
using Rushframe.Domain;
using Rushframe.Domain.Editing;
using Rushframe.Media.Abstractions;

namespace Rushframe.Desktop;

public partial class MainWindow
{
    private void MergeInstalledCapabilities(Project project)
    {
        foreach (var provider in _installedAssetProviders)
        {
            project.AssetProviders.RemoveAll(existing =>
                string.Equals(existing.Id, provider.Id, StringComparison.OrdinalIgnoreCase));
            project.AssetProviders.Add(provider);
        }

        foreach (var extension in _installedExtensions)
        {
            project.Extensions.RemoveAll(existing =>
                string.Equals(existing.Id, extension.Id, StringComparison.OrdinalIgnoreCase));
            project.Extensions.Add(extension);
        }
    }

    private async Task ShowCreativeAssetsAsync()
    {
        var selected = new Dialogs.CreativeAssetsDialog(
            this,
            _installedAssetProviders,
            _installedExtensions,
            _creativeAssetPackService,
            _extensionManifestService,
            Path.Combine(_appData, "asset-packs"),
            Path.Combine(_appData, "extensions")).Show();

        MergeInstalledCapabilities(_project);
        if (selected == null)
        {
            StatusText.Text = $"{_installedAssetProviders.Sum(provider => provider.Assets.Count)} creative assets and {_installedExtensions.Count} extension manifests available";
            return;
        }

        await InsertCreativeAssetAsync(selected);
    }

    private async Task InsertCreativeAssetAsync(CreativeAssetDescriptor descriptor)
    {
        var sequence = _project.MainSequence;
        if (sequence == null || _timeline == null) return;

        if (descriptor.Kind == CreativeAssetKind.Font)
        {
            if (_selectedInspectorItem?.Kind != ItemKind.Text)
            {
                StatusText.Text = "Select a text item before applying a font asset.";
                return;
            }
            if (!File.Exists(descriptor.LocalPath))
            {
                StatusText.Text = "The selected font file is unavailable.";
                return;
            }

            Execute(new SetPropertyCommand
            {
                ItemId = _selectedInspectorItem.Id,
                PropertyName = nameof(TimelineItem.FontFamily),
                NewValue = descriptor.LocalPath,
                Getter = item => item.FontFamily,
                Setter = (item, value) => item.FontFamily = value as string,
            });
            StatusText.Text = $"Applied font: {descriptor.Name}";
            return;
        }

        if (descriptor.Id.StartsWith("builtin.shape.", StringComparison.OrdinalIgnoreCase))
        {
            var track = EnsureAssetTrack(sequence, TrackKind.Overlay, "O1");
            var duration = MediaTime.FromSeconds(3);
            Execute(new AddClipCommand
            {
                TrackId = track.Id,
                Item = new TimelineItem
                {
                    Kind = ItemKind.Sticker,
                    StickerId = descriptor.Id,
                    TimelineStart = _timeline.PlayheadTime,
                    Duration = duration,
                    SourceDuration = duration,
                    FillColor = "#FFFFFF",
                    OutlineColor = "#000000",
                    OutlineWidth = 1,
                    Transform =
                    {
                        ScaleX = 0.45,
                        ScaleY = 0.45,
                    },
                },
            });
            StatusText.Text = $"Added {descriptor.Name} to the timeline";
            return;
        }

        if (string.IsNullOrWhiteSpace(descriptor.LocalPath) || !File.Exists(descriptor.LocalPath))
        {
            StatusText.Text = $"Local asset is unavailable: {descriptor.Name}";
            return;
        }

        var mediaKind = descriptor.Kind switch
        {
            CreativeAssetKind.Sound or CreativeAssetKind.Music => MediaKind.Audio,
            _ => MediaKind.Image,
        };
        var existing = _project.MediaLibrary.FirstOrDefault(asset =>
            string.Equals(asset.OriginalPath, descriptor.LocalPath, StringComparison.OrdinalIgnoreCase));
        var asset = existing;
        var addAsset = false;
        if (asset == null)
        {
            var duration = MediaTime.Zero;
            var width = 0;
            var height = 0;
            try
            {
                var probe = await _mediaService.ProbeAsync(descriptor.LocalPath);
                duration = MediaTime.FromSeconds(probe.Duration.TotalSeconds);
                var videoStream = probe.Streams.FirstOrDefault(stream => stream.Kind == MediaStreamKind.Video);
                width = videoStream?.Width ?? 0;
                height = videoStream?.Height ?? 0;
            }
            catch
            {
                if (mediaKind == MediaKind.Audio)
                {
                    StatusText.Text = $"Could not read audio asset: {descriptor.Name}";
                    return;
                }
            }

            asset = new MediaAsset
            {
                Kind = mediaKind,
                OriginalPath = descriptor.LocalPath,
                RelativeProjectPath = descriptor.LocalPath,
                Duration = duration,
                PixelWidth = width,
                PixelHeight = height,
            };
            addAsset = true;
        }

        using var mutation = _saveCoordinator.BeginMutation();
        if (addAsset)
        {
            _project.MediaLibrary.Add(asset);
            _project.IncrementRevision();
            RefreshMediaList();
        }

        var trackKind = descriptor.Kind switch
        {
            CreativeAssetKind.Music => TrackKind.Music,
            CreativeAssetKind.Sound => TrackKind.Audio,
            _ => TrackKind.Overlay,
        };
        var targetTrack = EnsureAssetTrack(sequence, trackKind, trackKind switch
        {
            TrackKind.Music => "Music 1",
            TrackKind.Audio => "A1",
            _ => "O1",
        });
        var timelineDuration = asset.Duration.Seconds > 0
            ? asset.Duration
            : MediaTime.FromSeconds(mediaKind == MediaKind.Image ? 3 : 10);
        Execute(new AddClipCommand
        {
            TrackId = targetTrack.Id,
            Item = new TimelineItem
            {
                Kind = mediaKind == MediaKind.Audio
                    ? ItemKind.Clip
                    : descriptor.Kind is CreativeAssetKind.Sticker or CreativeAssetKind.Shape
                        ? ItemKind.Sticker
                        : ItemKind.Image,
                StickerId = descriptor.Kind is CreativeAssetKind.Sticker or CreativeAssetKind.Shape ? descriptor.Id : null,
                MediaAssetId = asset.Id,
                TimelineStart = _timeline.PlayheadTime,
                Duration = timelineDuration,
                SourceDuration = timelineDuration,
            },
        });
        StatusText.Text = $"Added licensed asset: {descriptor.Name}";
    }

    private static Track EnsureAssetTrack(Sequence sequence, TrackKind kind, string name)
    {
        var track = sequence.Tracks.FirstOrDefault(candidate => candidate.Kind == kind && !candidate.Locked);
        if (track != null) return track;

        track = new Track
        {
            Kind = kind,
            Name = name,
            Order = sequence.Tracks.Count,
        };
        sequence.Tracks.Add(track);
        return track;
    }
}
