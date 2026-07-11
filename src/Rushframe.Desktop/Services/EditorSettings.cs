namespace Rushframe.Desktop.Services;

public sealed class EditorSettings
{
    public bool SnapEnabled { get; set; } = true;
    public bool RippleEnabled { get; set; }
    public double TimelineZoom { get; set; } = 1;
    public double UiScale { get; set; } = 1;
    public bool AutosaveEnabled { get; set; } = true;
    public int AutosaveIntervalSeconds { get; set; } = 30;
    public bool StartIntelligenceBackend { get; set; } = true;
    public int IntelligenceBackendPort { get; set; } = 7319;
    public string ProtectedGeminiApiKey { get; set; } = string.Empty;
    public int MaxAiInputSeconds { get; set; } = 900;
    public int MaxOutputDurationSeconds { get; set; } = 180;
}
