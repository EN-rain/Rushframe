namespace Rushframe.Domain.Editing;

public sealed class SetTextContentCommand : IAtomicEditCommand
{
    public string Description => $"Set text content on {ItemId}";

    public required TimelineItemId ItemId { get; init; }
    public required string NewText { get; init; }

    private string? _oldText;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (item.Locked) return EditResult.Fail("Item is locked");

            _oldText = item.TextContent;
            item.TextContent = NewText;
            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            item.TextContent = _oldText;
            return EditResult.Ok();
        }
        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }
}

public sealed class SetTextPropertiesCommand : IAtomicEditCommand
{
    public string Description => $"Set text properties on {ItemId}";

    public required TimelineItemId ItemId { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSize { get; init; }
    public bool? FontBold { get; init; }
    public string? FontAlign { get; init; }
    public string? FillColor { get; init; }
    public string? OutlineColor { get; init; }
    public double? OutlineWidth { get; init; }
    public string? ShadowColor { get; init; }
    public double? ShadowOffsetX { get; init; }
    public double? ShadowOffsetY { get; init; }
    public double? ShadowBlur { get; init; }
    public double? ShadowOpacity { get; init; }

    private string? _oldFontFamily;
    private double _oldFontSize;
    private bool _oldFontBold;
    private string _oldFontAlign = "center";
    private string? _oldFillColor;
    private string? _oldOutlineColor;
    private double _oldOutlineWidth;
    private string? _oldShadowColor;
    private double _oldShadowOffsetX;
    private double _oldShadowOffsetY;
    private double _oldShadowBlur;
    private double _oldShadowOpacity;

    public EditResult Execute(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;
            if (track.Locked) return EditResult.Fail("Track is locked");
            if (item.Locked) return EditResult.Fail("Item is locked");

            if (item.Kind != ItemKind.Text) return EditResult.Fail("Item is not a text element");

            _oldFontFamily = item.FontFamily;
            _oldFontSize = item.FontSize;
            _oldFontBold = item.FontBold;
            _oldFontAlign = item.FontAlign;
            _oldFillColor = item.FillColor;
            _oldOutlineColor = item.OutlineColor;
            _oldOutlineWidth = item.OutlineWidth;
            _oldShadowColor = item.ShadowColor;
            _oldShadowOffsetX = item.ShadowOffsetX;
            _oldShadowOffsetY = item.ShadowOffsetY;
            _oldShadowBlur = item.ShadowBlur;
            _oldShadowOpacity = item.ShadowOpacity;

            if (FontFamily != null) item.FontFamily = FontFamily;
            if (FontSize.HasValue) item.FontSize = FontSize.Value;
            if (FontBold.HasValue) item.FontBold = FontBold.Value;
            if (FontAlign != null) item.FontAlign = FontAlign;
            if (FillColor != null) item.FillColor = FillColor;
            if (OutlineColor != null) item.OutlineColor = OutlineColor;
            if (OutlineWidth.HasValue) item.OutlineWidth = OutlineWidth.Value;
            if (ShadowColor != null) item.ShadowColor = ShadowColor;
            if (ShadowOffsetX.HasValue) item.ShadowOffsetX = ShadowOffsetX.Value;
            if (ShadowOffsetY.HasValue) item.ShadowOffsetY = ShadowOffsetY.Value;
            if (ShadowBlur.HasValue) item.ShadowBlur = ShadowBlur.Value;
            if (ShadowOpacity.HasValue) item.ShadowOpacity = ShadowOpacity.Value;

            return EditResult.Ok();
        }

        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }

    public EditResult Undo(Sequence sequence)
    {
        foreach (var track in sequence.Tracks)
        {
            var item = track.Items.FirstOrDefault(i => i.Id == ItemId);
            if (item == null) continue;

            item.FontFamily = _oldFontFamily;
            item.FontSize = _oldFontSize;
            item.FontBold = _oldFontBold;
            item.FontAlign = _oldFontAlign;
            item.FillColor = _oldFillColor;
            item.OutlineColor = _oldOutlineColor;
            item.OutlineWidth = _oldOutlineWidth;
            item.ShadowColor = _oldShadowColor;
            item.ShadowOffsetX = _oldShadowOffsetX;
            item.ShadowOffsetY = _oldShadowOffsetY;
            item.ShadowBlur = _oldShadowBlur;
            item.ShadowOpacity = _oldShadowOpacity;

            return EditResult.Ok();
        }
        return EditResult.Fail(new ItemNotFoundError(ItemId));
    }
}
