namespace Rushframe.Media.Abstractions;

public enum TimelineExportFormat
{
    Mp4,
    WebM,
    Mov,
    Mkv,
}

public enum TimelineExportQuality
{
    Draft,
    Standard,
    High,
    Master,
}

public sealed record TimelineExportOptions(
    TimelineExportFormat Format = TimelineExportFormat.Mp4,
    TimelineExportQuality Quality = TimelineExportQuality.High,
    bool IncludeAudio = true,
    bool HardwareEncoding = false);
