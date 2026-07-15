namespace Rushframe.Desktop.Services;

public sealed class EditorSettings
{
    public bool SnapEnabled { get; set; } = true;
    public bool RippleEnabled { get; set; }
    public double TimelineZoom { get; set; } = 1;
    public double UiScale { get; set; } = 1;
    public int PreviewMaxFps { get; set; } = 30;
    public int PreviewMaxWidth { get; set; } = 960;
    public double PreviewLookAheadSeconds { get; set; } = 0.75;
    public bool AutosaveEnabled { get; set; } = true;
    public int AutosaveIntervalSeconds { get; set; } = 30;
    public bool StartIntelligenceBackend { get; set; } = true;
    public int IntelligenceBackendPort { get; set; } = 7319;
    public List<string> ProtectedGroqApiKeys { get; set; } = [];
    public List<ProtectedCloudflareCredential> ProtectedCloudflareCredentials { get; set; } = [];
    public Dictionary<string, int> AiProviderRotationCursors { get; set; } = [];
    public int MaxAiInputSeconds { get; set; } = 900;
    public int MaxOutputDurationSeconds { get; set; } = 180;
    public Dictionary<string, string> Keybindings { get; set; } = [];
}

public sealed class ProtectedCloudflareCredential
{
    public string ProtectedAccountId { get; set; } = string.Empty;
    public string ProtectedApiToken { get; set; } = string.Empty;
}
