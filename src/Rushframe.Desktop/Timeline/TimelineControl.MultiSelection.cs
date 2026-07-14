using System.Windows;
using System.Windows.Input;
using Rushframe.Domain;

namespace Rushframe.Desktop.Timeline;

public sealed partial class TimelineControl
{
    private readonly HashSet<TimelineItemId> _selectedItemIds = [];
    private readonly Dictionary<TimelineItemId, GroupDragSnapshot> _groupDragSnapshots = [];
    private MediaTime? _activeSnapGuideTime;
    private int _groupTargetTrackDelta;
    private bool _isBoxSelecting;
    private Point _selectionBoxStart;
    private Point _selectionBoxCurrent;

    private bool IsItemSelected(TimelineItem item) =>
        _selectedItemIds.Count == 0
            ? _selectedItem?.Id == item.Id
            : _selectedItemIds.Contains(item.Id);

    private IReadOnlyList<TimelineItem> ResolveSelectedItems()
    {
        if (Sequence == null || _selectedItemIds.Count == 0)
            return _selectedItem == null ? [] : [_selectedItem];

        return Sequence.Tracks
            .SelectMany(track => track.Items)
            .Where(item => _selectedItemIds.Contains(item.Id))
            .ToArray();
    }

    private void SelectPointerItem(TimelineItem item, int trackIndex, ModifierKeys modifiers)
    {
        var additive = modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Shift);
        if (!additive)
        {
            if (!_selectedItemIds.Contains(item.Id) || _selectedItemIds.Count <= 1)
            {
                _selectedItemIds.Clear();
                _selectedItemIds.Add(item.Id);
            }
        }
        else if (!_selectedItemIds.Add(item.Id))
        {
            _selectedItemIds.Remove(item.Id);
        }

        _selectedItem = _selectedItemIds.Contains(item.Id)
            ? item
            : ResolveSelectedItems().FirstOrDefault();
        _selectedTrackIndex = _selectedItem == null
            ? -1
            : FindTrackIndex(_selectedItem.Id);
        _selectedTransition = null;
        ClipSelected?.Invoke(this, _selectedItem);
        TransitionSelected?.Invoke(this, null);
        SelectionChanged?.Invoke(this, ResolveSelectedItems());
        InvalidateVisual();
    }

    private int FindTrackIndex(TimelineItemId itemId)
    {
        if (Sequence == null) return -1;
        for (var index = 0; index < Sequence.Tracks.Count; index++)
        {
            if (Sequence.Tracks[index].Items.Any(item => item.Id == itemId)) return index;
        }
        return -1;
    }

    private void CaptureGroupDragSnapshots()
    {
        _groupDragSnapshots.Clear();
        if (Sequence == null || ResolveSelectedItems().Count <= 1) return;

        for (var trackIndex = 0; trackIndex < Sequence.Tracks.Count; trackIndex++)
        {
            var track = Sequence.Tracks[trackIndex];
            foreach (var item in track.Items.Where(IsItemSelected))
            {
                if (track.Locked || item.Locked) continue;
                _groupDragSnapshots[item.Id] = new GroupDragSnapshot(
                    item,
                    trackIndex,
                    item.TimelineStart,
                    item.Duration,
                    item.SourceStart);
            }
        }
    }

    private bool HasGroupDrag => _groupDragSnapshots.Count > 1;

    private bool UpdateGroupMovePreview(Point pointer)
    {
        if (!HasGroupDrag || _dragItem == null || _dragMode != DragMode.Move) return false;
        var anchor = _groupDragSnapshots[_dragItem.Id];
        var deltaSeconds = (pointer.X - _dragOriginMouseX) / Math.Max(1, _viewport.PixelsPerSecond);
        var rawAnchor = MediaTime.FromSeconds(Math.Max(0, anchor.Start.Seconds + deltaSeconds));
        var snappedAnchor = SnapMoveStart(rawAnchor, anchor.Duration, anchor.Item);
        var actualDelta = snappedAnchor.Seconds - anchor.Start.Seconds;
        var minimumStart = _groupDragSnapshots.Values.Min(snapshot => snapshot.Start.Seconds);
        actualDelta = Math.Max(actualDelta, -minimumStart);

        _groupTargetTrackDelta = ResolveGroupTrackDelta(pointer, anchor.TrackIndex);
        foreach (var snapshot in _groupDragSnapshots.Values)
            snapshot.Item.TimelineStart = MediaTime.FromSeconds(snapshot.Start.Seconds + actualDelta);

        _activeSnapGuideTime = snappedAnchor;
        InvalidateVisual();
        return true;
    }

    private bool UpdateGroupTrimPreview(Point pointer)
    {
        if (!HasGroupDrag || _dragItem == null || _dragMode is not (DragMode.TrimLeft or DragMode.TrimRight))
            return false;

        var anchor = _groupDragSnapshots[_dragItem.Id];
        var deltaSeconds = (pointer.X - _dragOriginMouseX) / Math.Max(1, _viewport.PixelsPerSecond);
        const double minimumDuration = 0.1;

        foreach (var snapshot in _groupDragSnapshots.Values)
        {
            if (_dragMode == DragMode.TrimRight)
            {
                snapshot.Item.Duration = MediaTime.FromSeconds(Math.Max(minimumDuration, snapshot.Duration.Seconds + deltaSeconds));
                continue;
            }

            var clampedDelta = Math.Clamp(deltaSeconds, -snapshot.Start.Seconds, snapshot.Duration.Seconds - minimumDuration);
            snapshot.Item.TimelineStart = MediaTime.FromSeconds(snapshot.Start.Seconds + clampedDelta);
            snapshot.Item.Duration = MediaTime.FromSeconds(snapshot.Duration.Seconds - clampedDelta);
            snapshot.Item.SourceStart = MediaTime.FromSeconds(
                Math.Max(0, snapshot.SourceStart.Seconds + clampedDelta * Math.Max(0.1, snapshot.Item.Speed)));
        }

        InvalidateVisual();
        return true;
    }

    private bool FinishGroupMove()
    {
        if (!HasGroupDrag || _dragMode != DragMode.Move) return false;
        var changes = _groupDragSnapshots.Values
            .Select(snapshot => new GroupMoveRequest(
                snapshot.Item,
                Math.Clamp(snapshot.TrackIndex + _groupTargetTrackDelta, 0, (Sequence?.Tracks.Count ?? 1) - 1),
                snapshot.Item.TimelineStart))
            .ToArray();
        RestoreGroupSnapshots();
        GroupMoveRequested?.Invoke(this, new GroupMoveRequestedEventArgs(changes));
        ResetGroupInteraction();
        return true;
    }

    private bool FinishGroupTrim()
    {
        if (!HasGroupDrag || _dragMode is not (DragMode.TrimLeft or DragMode.TrimRight)) return false;
        var changes = _groupDragSnapshots.Values
            .Select(snapshot => new GroupTrimRequest(
                snapshot.Item,
                snapshot.TrackIndex,
                snapshot.Item.TimelineStart,
                snapshot.Item.Duration,
                snapshot.Item.SourceStart))
            .ToArray();
        RestoreGroupSnapshots();
        GroupTrimRequested?.Invoke(this, new GroupTrimRequestedEventArgs(changes));
        ResetGroupInteraction();
        return true;
    }

    private void RestoreGroupSnapshots()
    {
        foreach (var snapshot in _groupDragSnapshots.Values)
        {
            snapshot.Item.TimelineStart = snapshot.Start;
            snapshot.Item.Duration = snapshot.Duration;
            snapshot.Item.SourceStart = snapshot.SourceStart;
        }
    }

    private void ResetGroupInteraction()
    {
        _isDraggingClip = false;
        _isTrimming = false;
        _dragMode = DragMode.None;
        _dragItem = null;
        _activeSnapGuideTime = null;
        _groupTargetTrackDelta = 0;
        _groupDragSnapshots.Clear();
        InvalidateVisual();
    }

    private int ResolveGroupTrackDelta(Point pointer, int anchorTrackIndex)
    {
        if (Sequence == null) return 0;
        var targetAnchor = _viewport.YToTrackIndex(pointer.Y);
        if (targetAnchor < 0) return 0;
        var delta = targetAnchor - anchorTrackIndex;
        foreach (var snapshot in _groupDragSnapshots.Values)
        {
            var targetIndex = snapshot.TrackIndex + delta;
            if (targetIndex < 0 || targetIndex >= Sequence.Tracks.Count) return 0;
            var targetTrack = Sequence.Tracks[targetIndex];
            if (targetTrack.Locked || !TrackCompatibility.IsItemCompatibleWithTrack(snapshot.Item.Kind, targetTrack.Kind))
                return 0;
        }
        return delta;
    }

    private void BeginBoxSelection(Point point)
    {
        _isBoxSelecting = true;
        _selectionBoxStart = point;
        _selectionBoxCurrent = point;
        CaptureMouse();
        InvalidateVisual();
    }

    private bool UpdateBoxSelection(Point point)
    {
        if (!_isBoxSelecting) return false;
        _selectionBoxCurrent = point;
        InvalidateVisual();
        return true;
    }

    private bool FinishBoxSelection()
    {
        if (!_isBoxSelecting || Sequence == null) return false;
        var selection = NormalizeSelectionBox();
        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
                       || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!additive) _selectedItemIds.Clear();

        for (var trackIndex = 0; trackIndex < Sequence.Tracks.Count; trackIndex++)
        {
            var y = _viewport.TrackIndexToY(trackIndex);
            foreach (var item in Sequence.Tracks[trackIndex].Items)
            {
                var rect = new Rect(
                    _viewport.GetClipX(item.TimelineStart),
                    y,
                    Math.Max(2, _viewport.GetClipWidth(item.Duration)),
                    _viewport.TrackHeight);
                if (selection.IntersectsWith(rect)) _selectedItemIds.Add(item.Id);
            }
        }

        _selectedItem = ResolveSelectedItems().FirstOrDefault();
        _selectedTrackIndex = _selectedItem == null ? -1 : FindTrackIndex(_selectedItem.Id);
        _isBoxSelecting = false;
        ReleaseMouseCapture();
        ClipSelected?.Invoke(this, _selectedItem);
        SelectionChanged?.Invoke(this, ResolveSelectedItems());
        InvalidateVisual();
        return true;
    }

    private Rect NormalizeSelectionBox() => new(
        Math.Min(_selectionBoxStart.X, _selectionBoxCurrent.X),
        Math.Min(_selectionBoxStart.Y, _selectionBoxCurrent.Y),
        Math.Abs(_selectionBoxCurrent.X - _selectionBoxStart.X),
        Math.Abs(_selectionBoxCurrent.Y - _selectionBoxStart.Y));

    private void DrawSelectionBox(System.Windows.Media.DrawingContext drawingContext)
    {
        if (!_isBoxSelecting) return;
        var rect = NormalizeSelectionBox();
        drawingContext.DrawRectangle(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(38, 167, 122, 255)),
            new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(187, 149, 255)), 1),
            rect);
    }

    private void DrawActiveSnapGuide(System.Windows.Media.DrawingContext drawingContext)
    {
        if (_activeSnapGuideTime == null) return;
        var x = _viewport.TimeToPixel(_activeSnapGuideTime.Value);
        if (x < _viewport.TrackHeaderWidth || x > RenderSize.Width) return;
        var pen = new System.Windows.Media.Pen(
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(196, 181, 253)),
            1.25);
        drawingContext.DrawLine(pen, new Point(x, _viewport.RulerHeight), new Point(x, RenderSize.Height));
    }

    private sealed record GroupDragSnapshot(
        TimelineItem Item,
        int TrackIndex,
        MediaTime Start,
        MediaTime Duration,
        MediaTime SourceStart);
}

public sealed record GroupMoveRequest(TimelineItem Item, int TrackIndex, MediaTime NewStart);
public sealed record GroupTrimRequest(
    TimelineItem Item,
    int TrackIndex,
    MediaTime NewStart,
    MediaTime NewDuration,
    MediaTime NewSourceStart);

public sealed class GroupMoveRequestedEventArgs(IReadOnlyList<GroupMoveRequest> changes) : EventArgs
{
    public IReadOnlyList<GroupMoveRequest> Changes { get; } = changes;
}

public sealed class GroupTrimRequestedEventArgs(IReadOnlyList<GroupTrimRequest> changes) : EventArgs
{
    public IReadOnlyList<GroupTrimRequest> Changes { get; } = changes;
}
