using System.Windows;
using System.Windows.Input;
using Rushframe.Domain;

namespace Rushframe.Desktop.Timeline;

internal static class TimelineMediaDragData
{
    public const string MediaAssetIdFormat = "Rushframe.MediaAssetId";

    public static DataObject Create(MediaAssetId assetId)
    {
        var data = new DataObject();
        data.SetData(MediaAssetIdFormat, assetId.ToString());
        return data;
    }

    public static bool TryRead(IDataObject? data, out MediaAssetId assetId)
    {
        assetId = default;
        if (data == null || !data.GetDataPresent(MediaAssetIdFormat)) return false;
        if (data.GetData(MediaAssetIdFormat) is not string value || !Guid.TryParse(value, out var id)) return false;
        assetId = new MediaAssetId(id);
        return true;
    }
}

public sealed partial class TimelineControl
{
    public event EventHandler<TimelineMediaDropRequestedEventArgs>? MediaDropRequested;
    public event EventHandler<TimelineMediaDragPreviewEventArgs>? MediaDragPreviewRequested;
    public event EventHandler? MediaDragPreviewCleared;

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        UpdateExternalMediaDragEffect(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        UpdateExternalMediaDragEffect(e);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        MediaDragPreviewCleared?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        MediaDragPreviewCleared?.Invoke(this, EventArgs.Empty);
        if (!TimelineMediaDragData.TryRead(e.Data, out var assetId)) return;

        var position = e.GetPosition(this);
        if (position.X <= _viewport.TrackHeaderWidth || position.Y < _viewport.RulerHeight)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var time = _viewport.PixelToTime(ClampContentX(position.X));
        MediaDropRequested?.Invoke(this, new TimelineMediaDropRequestedEventArgs(
            assetId,
            GetTrackIndexAtY(position.Y),
            time));
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void UpdateExternalMediaDragEffect(DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (!TimelineMediaDragData.TryRead(e.Data, out var assetId)
            || position.X <= _viewport.TrackHeaderWidth
            || position.Y < _viewport.RulerHeight)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var preview = new TimelineMediaDragPreviewEventArgs(
            assetId,
            GetTrackIndexAtY(position.Y),
            _viewport.PixelToTime(ClampContentX(position.X)));
        MediaDragPreviewRequested?.Invoke(this, preview);
        e.Effects = preview.Accepted ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
}

public sealed class TimelineMediaDragPreviewEventArgs(
    MediaAssetId mediaAssetId,
    int trackIndex,
    MediaTime timelineStart) : EventArgs
{
    public MediaAssetId MediaAssetId { get; } = mediaAssetId;
    public int TrackIndex { get; } = trackIndex;
    public MediaTime TimelineStart { get; } = timelineStart;
    public bool Accepted { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class TimelineMediaDropRequestedEventArgs(
    MediaAssetId mediaAssetId,
    int trackIndex,
    MediaTime timelineStart) : EventArgs
{
    public MediaAssetId MediaAssetId { get; } = mediaAssetId;
    public int TrackIndex { get; } = trackIndex;
    public MediaTime TimelineStart { get; } = timelineStart;
}
