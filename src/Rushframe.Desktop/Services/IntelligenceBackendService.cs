using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Rushframe.Desktop.Services;

public sealed class IntelligenceBackendService : IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private Process? _process;

    public int Port { get; }
    public Uri BaseUri => new($"http://127.0.0.1:{Port}/");
    public bool IsRunning => _process is { HasExited: false };

    public IntelligenceBackendService(int port = 7319)
    {
        Port = port;
    }

    public async Task<bool> StartAsync(
        string repoRoot,
        string? geminiApiKey,
        string? editorSessionToken = null,
        CancellationToken cancellationToken = default)
    {
        if (await IsHealthyAsync(cancellationToken))
        {
            if (await IsSessionCompatibleAsync(editorSessionToken, cancellationToken)) return true;
            Stop();
            if (await IsHealthyAsync(cancellationToken))
            {
                // Another process owns this port but does not enforce the current editor session.
                // Refuse to reuse it instead of silently exposing unauthenticated MCP tools.
                return false;
            }
        }
        Stop();

        var managedPython = Path.Combine(repoRoot, ".tools", "intelligence-venv", "Scripts", "python.exe");
        foreach (var launcher in new[] { managedPython, "py", "python" })
        {
            if (Path.IsPathFullyQualified(launcher) && !File.Exists(launcher)) continue;
            var startInfo = new ProcessStartInfo
            {
                FileName = launcher,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (launcher == "py") startInfo.ArgumentList.Add("-3");
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add("rushframe_intelligence");
            startInfo.ArgumentList.Add("serve");
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(geminiApiKey))
                startInfo.Environment["GEMINI_API_KEY"] = geminiApiKey;
            if (!string.IsNullOrWhiteSpace(editorSessionToken))
                startInfo.Environment["RUSHFRAME_EDITOR_SESSION_TOKEN"] = editorSessionToken;

            try
            {
                _process = Process.Start(startInfo);
                if (_process == null) continue;
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    await Task.Delay(100, cancellationToken);
                    if (_process.HasExited) break;
                    if (await IsHealthyAsync(cancellationToken)) return true;
                }
                Stop();
            }
            catch (Win32Exception)
            {
                Stop();
            }
        }
        return false;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(BaseUri, "health"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> IsSessionCompatibleAsync(
        string? editorSessionToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(editorSessionToken)) return false;
        try
        {
            using var healthResponse = await _httpClient.GetAsync(new Uri(BaseUri, "health"), cancellationToken);
            if (!healthResponse.IsSuccessStatusCode) return false;
            await using var healthStream = await healthResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var health = await JsonDocument.ParseAsync(healthStream, cancellationToken: cancellationToken);
            if (!health.RootElement.TryGetProperty("sessionRequired", out var required)
                || required.ValueKind != JsonValueKind.True)
                return false;

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri, "mcp"));
            request.Headers.TryAddWithoutValidation("X-Rushframe-Session", editorSessionToken);
            request.Content = new StringContent(
                "{\"jsonrpc\":\"2.0\",\"id\":\"session-check\",\"method\":\"initialize\",\"params\":{}}",
                Encoding.UTF8,
                "application/json");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1500);
            }
            catch (InvalidOperationException)
            {
            }
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
    }
}
