using System.Text.Json;

namespace Rushframe.Domain.Editing;

public sealed class UpdateSequenceSettingsCommand : IEditCommand
{
    public required SequenceId SequenceId { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required FrameRate FrameRate { get; init; }
    public required CanvasBackground Background { get; init; }
    public required IReadOnlyList<LayoutGuide> LayoutGuides { get; init; }

    private SequenceSettingsSnapshot? _old;

    public string Description => "Update canvas and guide settings";

    public EditResult Execute(Sequence sequence)
    {
        if (sequence.Id != SequenceId) return EditResult.Fail("Sequence not found.");
        _old = SequenceSettingsSnapshot.From(sequence);
        Apply(sequence, Width, Height, FrameRate, Background, LayoutGuides);
        return EditResult.Ok();
    }

    public EditResult Undo(Sequence sequence)
    {
        if (sequence.Id != SequenceId || _old == null) return EditResult.Fail("Sequence settings snapshot is unavailable.");
        Apply(sequence, _old.Width, _old.Height, _old.FrameRate, _old.Background, _old.LayoutGuides);
        return EditResult.Ok();
    }

    private static void Apply(
        Sequence sequence,
        int width,
        int height,
        FrameRate frameRate,
        CanvasBackground background,
        IEnumerable<LayoutGuide> layoutGuides)
    {
        sequence.Width = Math.Max(2, width);
        sequence.Height = Math.Max(2, height);
        sequence.FrameRate = frameRate;
        sequence.Background = Clone(background);
        sequence.LayoutGuides.Clear();
        sequence.LayoutGuides.AddRange(Clone(layoutGuides));
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    private static List<LayoutGuide> Clone(IEnumerable<LayoutGuide> value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<List<LayoutGuide>>(json) ?? [];
    }

    private sealed record SequenceSettingsSnapshot(
        int Width,
        int Height,
        FrameRate FrameRate,
        CanvasBackground Background,
        List<LayoutGuide> LayoutGuides)
    {
        public static SequenceSettingsSnapshot From(Sequence sequence) => new(
            sequence.Width,
            sequence.Height,
            sequence.FrameRate,
            Clone(sequence.Background),
            Clone(sequence.LayoutGuides));
    }
}
