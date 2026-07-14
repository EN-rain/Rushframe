using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace Rushframe.Desktop.Services;

public sealed class LocalAgentBridgeService : IDisposable
{
    private const long MaxRequestBytes = 1_048_576;
    private readonly HttpListener _listener = new();
    private readonly Func<string, JsonElement?, CancellationToken, Task<object>> _handler;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    public int Port { get; }
    public Uri BaseUri => new($"http://127.0.0.1:{Port}/");
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string SessionToken { get; }

    public LocalAgentBridgeService(
        Func<string, JsonElement?, CancellationToken, Task<object>> handler,
        int port = 7320)
    {
        _handler = handler;
        Port = port;
        SessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        _listener.Prefixes.Add(BaseUri.ToString());
    }

    public void Start()
    {
        if (_listener.IsListening) return;
        _cts = new CancellationTokenSource();
        _listener.Start();
        _loopTask = Task.Run(() => ListenAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        if (_listener.IsListening)
            _listener.Stop();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts?.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested || !_listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (!IPAddress.IsLoopback(context.Request.RemoteEndPoint?.Address ?? IPAddress.None))
            {
                await WriteJsonAsync(context.Response, new { ok = false, error = "loopback only" }, HttpStatusCode.Forbidden, cancellationToken);
                return;
            }

            var path = context.Request.Url?.AbsolutePath.Trim('/').ToLowerInvariant() ?? "";
            if (path is not ("" or "health") && !IsAuthorized(context.Request))
            {
                await WriteJsonAsync(context.Response, new { ok = false, error = "invalid agent session" }, HttpStatusCode.Unauthorized, cancellationToken);
                return;
            }

            if (context.Request.ContentLength64 > MaxRequestBytes)
            {
                await WriteJsonAsync(context.Response, new { ok = false, error = "request too large" }, HttpStatusCode.RequestEntityTooLarge, cancellationToken);
                return;
            }

            JsonElement? payload = null;
            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var document = JsonDocument.Parse(body);
                    payload = document.RootElement.Clone();
                }
            }

            var result = await _handler(path, payload, cancellationToken);
            await WriteJsonAsync(context.Response, result, HttpStatusCode.OK, cancellationToken);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context.Response, new { ok = false, error = ex.Message }, HttpStatusCode.BadRequest, cancellationToken);
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var supplied = request.Headers["X-Rushframe-Session"];
        if (string.IsNullOrWhiteSpace(supplied))
        {
            var authorization = request.Headers["Authorization"];
            if (authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                supplied = authorization[7..].Trim();
        }
        if (string.IsNullOrWhiteSpace(supplied)) return false;

        var expectedBytes = Encoding.UTF8.GetBytes(SessionToken);
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        return expectedBytes.Length == suppliedBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }

    private static async Task WriteJsonAsync(
        HttpListenerResponse response,
        object value,
        HttpStatusCode status,
        CancellationToken cancellationToken)
    {
        response.StatusCode = (int)status;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["Cache-Control"] = "no-store";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
        response.OutputStream.Close();
    }
}
