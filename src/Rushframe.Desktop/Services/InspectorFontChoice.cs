namespace Rushframe.Desktop.Services;

public sealed record InspectorFontChoice(string DisplayName, string Value, bool IsProjectAsset)
{
    public override string ToString() => DisplayName;
}
