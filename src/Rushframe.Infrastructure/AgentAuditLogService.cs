using System.Text.Json;
using Rushframe.Domain;

namespace Rushframe.Infrastructure;

public sealed class AgentAuditLogService
{
    private const long MaxLogBytes = 16L * 1024 * 1024;
    private const int MaxTailReadBytes = 4 * 1024 * 1024;
    private const int RetainedTailBytes = 8 * 1024 * 1024;
    private readonly string _path;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AgentAuditLogService(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public void Append(AgentAuditRecord record)
    {
        var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
        lock (_gate)
        {
            RotateIfNeeded(line.Length * sizeof(char));
            File.AppendAllText(_path, line, new System.Text.UTF8Encoding(false));
        }
    }

    public IReadOnlyList<AgentAuditRecord> ReadRecent(int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 500);
        if (!File.Exists(_path)) return [];

        string[] lines;
        lock (_gate)
        {
            lines = ReadTailLines(limit);
        }

        var records = new List<AgentAuditRecord>(Math.Min(limit, lines.Length));
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var record = JsonSerializer.Deserialize<AgentAuditRecord>(line, JsonOptions);
                if (record != null) records.Add(record);
            }
            catch (JsonException)
            {
                // Preserve readable entries even if a single line was interrupted during shutdown.
            }
        }
        return records;
    }

    private string[] ReadTailLines(int limit)
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytesToRead = (int)Math.Min(stream.Length, MaxTailReadBytes);
        stream.Seek(-bytesToRead, SeekOrigin.End);
        var buffer = new byte[bytesToRead];
        var read = 0;
        while (read < buffer.Length)
        {
            var current = stream.Read(buffer, read, buffer.Length - read);
            if (current == 0) break;
            read += current;
        }

        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var start = bytesToRead < stream.Length ? 1 : 0;
        return lines.Skip(start).TakeLast(limit).ToArray();
    }

    private void RotateIfNeeded(long incomingBytes)
    {
        if (!File.Exists(_path)) return;
        var info = new FileInfo(_path);
        if (info.Length + incomingBytes <= MaxLogBytes) return;

        var retained = (int)Math.Min(info.Length, RetainedTailBytes);
        using var source = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        source.Seek(-retained, SeekOrigin.End);
        var buffer = new byte[retained];
        var read = source.Read(buffer, 0, buffer.Length);
        var firstNewline = Array.IndexOf(buffer, (byte)'\n', 0, read);
        var offset = firstNewline >= 0 ? firstNewline + 1 : 0;
        var temp = _path + ".rotate.tmp";
        File.WriteAllBytes(temp, buffer[offset..read]);
        File.Move(temp, _path, overwrite: true);
    }
}
