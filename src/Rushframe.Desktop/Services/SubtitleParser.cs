using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using Rushframe.Domain;

namespace Rushframe.Desktop.Services;

public sealed record SubtitleCue(MediaTime Start, MediaTime End, string Text);

public static partial class SubtitleParser
{
    private const int MaximumCueCount = 10_000;

    public static async Task<IReadOnlyList<SubtitleCue>> ParseAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Subtitle file was not found.", path);
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return Parse(content);
    }

    public static IReadOnlyList<SubtitleCue> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[(normalized.IndexOf('\n') + 1)..];

        var cues = new List<SubtitleCue>();
        var blocks = BlankLineRegex().Split(normalized);
        foreach (var rawBlock in blocks)
        {
            if (cues.Count >= MaximumCueCount)
                throw new InvalidDataException($"Subtitle file exceeds the {MaximumCueCount} cue limit.");
            var lines = rawBlock.Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            if (lines.Count == 0) continue;
            if (lines[0].StartsWith("NOTE", StringComparison.OrdinalIgnoreCase)
                || lines[0].StartsWith("STYLE", StringComparison.OrdinalIgnoreCase)
                || lines[0].StartsWith("REGION", StringComparison.OrdinalIgnoreCase))
                continue;

            var timingIndex = lines.FindIndex(line => line.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0) continue;
            var timingParts = lines[timingIndex].Split("-->", 2, StringSplitOptions.TrimEntries);
            if (timingParts.Length != 2
                || !TryParseTimestamp(timingParts[0], out var start)
                || !TryParseTimestamp(timingParts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], out var end)
                || end <= start)
                continue;

            var text = string.Join('\n', lines.Skip(timingIndex + 1));
            text = WebUtility.HtmlDecode(HtmlTagRegex().Replace(text, string.Empty)).Trim();
            if (string.IsNullOrWhiteSpace(text)) continue;
            cues.Add(new SubtitleCue(MediaTime.FromSeconds(start), MediaTime.FromSeconds(end), text));
        }

        return cues.OrderBy(cue => cue.Start).ThenBy(cue => cue.End).ToList();
    }

    private static bool TryParseTimestamp(string value, out double seconds)
    {
        seconds = 0;
        var normalized = value.Trim().Replace(',', '.');
        var parts = normalized.Split(':');
        if (parts.Length is < 2 or > 3) return false;
        if (!double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var secondPart)
            || !int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            return false;
        var hours = 0;
        if (parts.Length == 3
            && !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hours))
            return false;
        if (minutes < 0 || minutes >= 60 || secondPart < 0 || secondPart >= 60 || hours < 0) return false;
        seconds = hours * 3600 + minutes * 60 + secondPart;
        return double.IsFinite(seconds);
    }

    [GeneratedRegex(@"\n\s*\n+", RegexOptions.Compiled)]
    private static partial Regex BlankLineRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();
}
