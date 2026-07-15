using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rushframe.Desktop.Services;

public sealed class LocalAgentBridgeService : IDisposable
{
    private const long MaxRequestBytes = 1_048_576;
    private const int MaxConcurrentRequests = 8;
    private readonly HttpListener _listener = new();
    private readonly Func<string, JsonElement?, CancellationToken, Task<object>> _handler;
    private readonly SemaphoreSlim _requestGate = new(MaxConcurrentRequests, MaxConcurrentRequests);
    private readonly ConcurrentDictionary<long, Task> _inflightRequests = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private long _nextRequestId;

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
        var cancellation = _cts;
        cancellation?.Cancel();
        if (_listener.IsListening)
            _listener.Stop();

        WaitForTask(_loopTask, TimeSpan.FromSeconds(2));
        var requests = _inflightRequests.Values.ToArray();
        if (requests.Length > 0)
        {
            try
            {
                Task.WhenAll(requests).Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Individual request failures have already been translated to responses.
            }
        }

        cancellation?.Dispose();
        _cts = null;
        _loopTask = null;
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        // A handler that ignores cancellation may finish after the bounded shutdown wait.
        // SemaphoreSlim owns no unmanaged resource, so leaving it undisposed avoids a late
        // Release() fault without allowing new requests after the listener is closed.
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

            try
            {
                await _requestGate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                context.Response.Abort();
                break;
            }

            var requestId = Interlocked.Increment(ref _nextRequestId);
            var task = Task.Run(() => HandleTrackedAsync(context, cancellationToken));
            _inflightRequests[requestId] = task;
            _ = task.ContinueWith(
                completedTask =>
                {
                    _inflightRequests.TryRemove(requestId, out _);
                    _ = completedTask.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private async Task HandleTrackedAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await HandleAsync(context, cancellationToken);
        }
        finally
        {
            _requestGate.Release();
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
                var body = await ReadBoundedBodyAsync(context.Request, cancellationToken);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var document = JsonDocument.Parse(body);
                    payload = document.RootElement.Clone();
                }
            }

            var result = await _handler(path, payload, cancellationToken);
            await WriteJsonAsync(context.Response, result, HttpStatusCode.OK, cancellationToken);
        }
        catch (RequestTooLargeException)
        {
            await TryWriteJsonAsync(context.Response, new { ok = false, error = "request too large" }, HttpStatusCode.RequestEntityTooLarge, cancellationToken);
        }
        catch (JsonException ex)
        {
            await TryWriteJsonAsync(context.Response, new { ok = false, error = ex.Message }, HttpStatusCode.BadRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Response.Abort();
        }
        catch (Exception ex)
        {
            await TryWriteJsonAsync(context.Response, new { ok = false, error = ex.Message }, HttpStatusCode.BadRequest, cancellationToken);
        }
    }

    private static async Task<string> ReadBoundedBodyAsync(
        HttpListenerRequest request,
        CancellationToken cancellationToken)
    {
        await using var body = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await request.InputStream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (body.Length + read > MaxRequestBytes)
                throw new RequestTooLargeException();
            await body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (body.Length == 0) return string.Empty;
        return request.ContentEncoding.GetString(body.GetBuffer(), 0, checked((int)body.Length));
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

    private static async Task TryWriteJsonAsync(
        HttpListenerResponse response,
        object value,
        HttpStatusCode status,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteJsonAsync(response, value, status, cancellationToken);
        }
        catch (HttpListenerException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
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

    private static void WaitForTask(Task? task, TimeSpan timeout)
    {
        if (task == null) return;
        try
        {
            task.Wait(timeout);
        }
        catch (AggregateException)
        {
        }
    }

    private sealed class RequestTooLargeException : Exception;
}
