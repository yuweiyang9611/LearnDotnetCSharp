using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LearnDotnetCSharp.Demos.Networking;

public sealed class HttpStreamingResilienceDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "networking.http-streaming-resilience",
        "networking",
        "HTTP 流式读取、幂等重试与取消",
        "用可控分段 HttpContent 验证 ResponseHeadersRead/JSON 流消费，并以本地 Kestrel 验证幂等 GET 重试和 RequestAborted。",
        [15, 16],
        ["ResponseHeadersRead", "streaming JSON", "idempotency", "retry", "503", "cancellation", "RequestAborted"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var state = new ServerState();
        using var streamingCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var streamingPipe = new Pipe();
        var streamingProducer = ProduceStreamingJsonAsync(
            streamingPipe.Writer,
            state,
            streamingCancellation.Token);
        await using var app = CreateServer(state);
        await app.StartAsync(timeout.Token).ConfigureAwait(false);

        try
        {
            var address = GetBoundAddress(app);
            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                UseProxy = false,
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = address,
                Timeout = Timeout.InfiniteTimeSpan,
            };
            using var streamingHandler = new StreamingResponseHandler(streamingPipe.Reader.AsStream());
            using var streamingClient = new HttpClient(streamingHandler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            var streamedItems = await ReadStreamAsync(
                    streamingClient,
                    state,
                    context,
                    timeout.Token)
                .ConfigureAwait(false);
            using var retryResponse = await SendIdempotentGetWithRetryAsync(
                    client,
                    "flaky",
                    maximumAttempts: 3,
                    timeout.Token)
                .ConfigureAwait(false);
            retryResponse.EnsureSuccessStatusCode();
            var retryPayload = await retryResponse.Content
                .ReadFromJsonAsync<RetryPayload>(cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            var clientCancelled = false;
            using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
            {
                requestCancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
                try
                {
                    await client.GetAsync("slow", requestCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
                {
                    clientCancelled = true;
                }
            }

            await state.RequestAborted.Task.WaitAsync(TimeSpan.FromSeconds(2), timeout.Token).ConfigureAwait(false);

            context.WriteProperty("streamed IDs", string.Join(", ", streamedItems.Select(item => item.Id)));
            context.WriteProperty("headers before completion", state.HeadersObservedBeforeCompletion);
            context.WriteProperty("retry attempts", state.FlakyAttempts);
            context.WriteProperty("retry payload", retryPayload?.Message ?? "<null>");
            context.WriteProperty("client cancellation", clientCancelled);
            context.WriteProperty("server RequestAborted", state.RequestAborted.Task.IsCompletedSuccessfully);
            context.WriteLine("  重试只覆盖本例的幂等 GET 和显式瞬时状态码；POST 等操作需要幂等键或业务级去重。");

            DemoAssert.SequenceEqual([1, 2, 3], streamedItems.Select(item => item.Id), "流式 JSON 应按顺序读取三个元素");
            DemoAssert.True(state.HeadersObservedBeforeCompletion, "ResponseHeadersRead 应在响应体完成前返回控制权");
            DemoAssert.True(state.FlakyAttempts == 2 && retryPayload?.Message == "recovered", "503 后的第二次幂等 GET 应成功");
            DemoAssert.True(clientCancelled && state.RequestAborted.Task.IsCompletedSuccessfully, "客户端取消应传播到服务器 RequestAborted");
        }
        finally
        {
            state.ReleaseStream.TrySetResult();
            streamingCancellation.Cancel();
            await ObserveStreamingProducerShutdownAsync(streamingProducer).ConfigureAwait(false);
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<StreamItem>> ReadStreamAsync(
        HttpClient client,
        ServerState state,
        DemoContext context,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://streaming.demo/items");
        HttpResponseMessage response;
        try
        {
            response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException(
                $"等待流式响应头超时；server flushed={state.FirstItemFlushed.Task.IsCompleted}, completed={state.StreamCompleted.Task.IsCompleted}",
                exception);
        }

        using (response)
        {
            response.EnsureSuccessStatusCode();

            await state.FirstItemFlushed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            state.HeadersObservedBeforeCompletion = !state.StreamCompleted.Task.IsCompleted;
            state.ReleaseStream.TrySetResult();

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var items = new List<StreamItem>();
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<StreamItem>(
                               responseStream,
                               cancellationToken: cancellationToken))
            {
                if (item is not null)
                {
                    items.Add(item);
                }
            }

            await state.StreamCompleted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return items;
        }
    }

    private static async Task ProduceStreamingJsonAsync(
        PipeWriter writer,
        ServerState state,
        CancellationToken cancellationToken)
    {
        const string firstChunk = "[{\"Id\":1,\"Value\":\"first\"}";
        const string remainingChunk = ",{\"Id\":2,\"Value\":\"second\"},{\"Id\":3,\"Value\":\"third\"}]";

        try
        {
            await writer.WriteAsync(Encoding.UTF8.GetBytes(firstChunk), cancellationToken).ConfigureAwait(false);
            state.FirstItemFlushed.TrySetResult();

            await state.ReleaseStream.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(Encoding.UTF8.GetBytes(remainingChunk), cancellationToken).ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
            state.StreamCompleted.TrySetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await writer.CompleteAsync().ConfigureAwait(false);
            state.StreamCompleted.TrySetCanceled(cancellationToken);
        }
    }

    private static async Task ObserveStreamingProducerShutdownAsync(Task streamingProducer)
    {
        try
        {
            await streamingProducer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The surrounding demo owns cancellation and has already stopped the listener.
        }
    }

    private static async Task<HttpResponseMessage> SendIdempotentGetWithRetryAsync(
        HttpClient client,
        string requestUri,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var transientStatus = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
            if (!transientStatus || attempt >= maximumAttempts)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static WebApplication CreateServer(ServerState state)
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(HttpStreamingResilienceDemo).Assembly.GetName().Name,
                Args = [],
            });
        builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("LEARN_DOTNET_HTTP_TRACE") == "1")
        {
            builder.Logging.AddSimpleConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
        }

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
        });

        var app = builder.Build();
        app.MapGet("/flaky", () =>
        {
            var attempt = Interlocked.Increment(ref state.FlakyAttempts);
            return attempt == 1
                ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
                : Results.Json(new RetryPayload("recovered"));
        });
        app.MapGet("/slow", async (HttpContext httpContext) =>
        {
            try
            {
                httpContext.Response.ContentType = "text/plain";
                await httpContext.Response.WriteAsync("started\n", httpContext.RequestAborted);
                await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(20), httpContext.RequestAborted);
                    await httpContext.Response.WriteAsync(".", httpContext.RequestAborted);
                    await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted);
                }
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                state.RequestAborted.TrySetResult();
            }
        });
        return app;
    }

    private static Uri GetBoundAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault();
        return address is null
            ? throw new InvalidOperationException("Kestrel 启动后没有报告监听地址。")
            : new Uri(address, UriKind.Absolute);
    }

    private sealed class ServerState
    {
        public TaskCompletionSource FirstItemFlushed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStream { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StreamCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource RequestAborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int FlakyAttempts;

        public bool HeadersObservedBeforeCompletion { get; set; }
    }

    private sealed record StreamItem(int Id, string Value);

    private sealed record RetryPayload(string Message);

    private sealed class StreamingResponseHandler(Stream responseStream) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(responseStream),
                RequestMessage = request,
            };
            response.Content.Headers.ContentType = new("application/json");
            return Task.FromResult(response);
        }
    }
}
