using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Rushframe.Desktop.Services;

namespace Rushframe.Desktop.Tests;

public sealed class AgentBridgeSecurityTests
{
    [Fact]
    public async Task chunked_request_is_bounded_by_actual_streamed_bytes()
    {
        var port = ReservePort();
        using var service = new LocalAgentBridgeService(
            (_, _, _) => Task.FromResult<object>(new { ok = true }),
            port);
        service.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(service.BaseUri, "edit"));
        request.Headers.TryAddWithoutValidation("X-Rushframe-Session", service.SessionToken);
        request.Headers.TransferEncodingChunked = true;
        request.Content = new StreamContent(new MemoryStream(new byte[1_048_577]));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task concurrent_requests_are_capped_and_complete_after_release()
    {
        var port = ReservePort();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximum = 0;
        using var service = new LocalAgentBridgeService(async (_, _, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximum, current);
            try
            {
                await release.Task.WaitAsync(cancellationToken);
                return new { ok = true };
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }, port);
        service.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        var requests = Enumerable.Range(0, 12)
            .Select(_ => SendAuthorizedAsync(client, service, "timeline", "{}"))
            .ToArray();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref maximum) < 8 && DateTime.UtcNow < deadline)
            await Task.Delay(20);

        Assert.Equal(8, Volatile.Read(ref maximum));
        release.TrySetResult();
        var responses = await Task.WhenAll(requests);
        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task stop_cancels_and_drains_inflight_requests()
    {
        var port = ReservePort();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new LocalAgentBridgeService(async (_, _, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new { ok = true };
        }, port);
        service.Start();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var request = SendAuthorizedAsync(client, service, "timeline", "{}");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var clock = Stopwatch.StartNew();
        service.Stop();
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Stop took {clock.Elapsed}.");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var response = await request;
            response.EnsureSuccessStatusCode();
        });
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpClient client,
        LocalAgentBridgeService service,
        string path,
        string body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(service.BaseUri, path));
        request.Headers.TryAddWithoutValidation("X-Rushframe-Session", service.SessionToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        while (true)
        {
            var observed = Volatile.Read(ref maximum);
            if (value <= observed) return;
            if (Interlocked.CompareExchange(ref maximum, value, observed) == observed) return;
        }
    }
}
