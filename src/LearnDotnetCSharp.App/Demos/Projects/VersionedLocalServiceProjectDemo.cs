using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LearnDotnetCSharp.Capstones.LocalService;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LearnDotnetCSharp.Demos.Projects;

public sealed class VersionedLocalServiceProjectDemo : IDemo
{
    private const int CurrentProtocolVersion = 2;
    private const string TransientServiceFault = "local-service.transient-503";
    private static readonly byte[] SharedSecret =
        Encoding.UTF8.GetBytes("learn-dotnet-local-protocol-demo-key");

    public DemoMetadata Metadata { get; } = new(
        "project.versioned-local-service",
        "projects",
        "综合项目：可持久化、可重启的版本化协议服务",
        "以仅绑定回环地址的 Kestrel 服务组合 HMAC、固定时间比较、SQLite 持久化幂等、提交后 503、服务重启重放与 NDJSON 流式响应。",
        [14, 15, 16, 17, 21],
        ["Kestrel", "protocol versioning", "HMAC", "SQLite", "idempotency key", "commit ambiguity", "restart replay", "NDJSON", "JsonExtensionData"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"learn-dotnet-service-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(stateDirectory, "idempotency.db");
        var faultPlan = FaultPlan.FailOn(TransientServiceFault, invocation: 1);
        await using var store = new SqliteIdempotencyStore(databasePath);
        await store.InitializeAsync(timeout.Token).ConfigureAwait(false);
        var state = new LocalServiceState(faultPlan, store, pauseFirstResponse: true);
        await using var app = CreateServer(state);
        await app.StartAsync(timeout.Token).ConfigureAwait(false);
        WebApplication? restartedApp = null;
        SqliteIdempotencyStore? restartedStore = null;
        LocalServiceState? restartedState = null;

        try
        {
            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
                UseProxy = false,
            };
            using var client = new HttpClient(handler)
            {
                BaseAddress = GetBoundAddress(app),
                Timeout = Timeout.InfiniteTimeSpan,
            };

            using var extensionDocument = JsonDocument.Parse("3");
            var request = new LocalServiceRequest
            {
                CorrelationId = "correlation-42",
                ProtocolVersion = CurrentProtocolVersion,
                Operation = "sum",
                Values = [4, 7, 11],
                ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["priority"] = extensionDocument.RootElement.Clone(),
                },
            };
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(
                request,
                LocalServiceProjectJsonContext.Default.LocalServiceRequest);
            const string idempotencyKey = "job-42";

            using (var badSignatureResponse = await SendOnceAsync(
                       client,
                       requestBytes,
                       request.ProtocolVersion,
                       request.CorrelationId,
                       idempotencyKey,
                       validSignature: false,
                       timeout.Token).ConfigureAwait(false))
            {
                DemoAssert.Equal(HttpStatusCode.Unauthorized, badSignatureResponse.StatusCode, "错误签名必须在业务处理前被拒绝");
            }

            var unsupported = new LocalServiceRequest
            {
                CorrelationId = request.CorrelationId,
                ProtocolVersion = 99,
                Operation = request.Operation,
                Values = request.Values,
                ExtensionData = request.ExtensionData,
            };
            var unsupportedBytes = JsonSerializer.SerializeToUtf8Bytes(
                unsupported,
                LocalServiceProjectJsonContext.Default.LocalServiceRequest);
            using (var versionResponse = await SendOnceAsync(
                       client,
                       unsupportedBytes,
                       unsupported.ProtocolVersion,
                       unsupported.CorrelationId,
                       "unsupported-version",
                       validSignature: true,
                       timeout.Token).ConfigureAwait(false))
            {
                DemoAssert.Equal(HttpStatusCode.UpgradeRequired, versionResponse.StatusCode, "不支持的协议版本应返回 426");
            }

            var successfulResponseTask = SendWithRetryAsync(
                client,
                requestBytes,
                request.ProtocolVersion,
                request.CorrelationId,
                idempotencyKey,
                maximumAttempts: 2,
                timeout.Token);
            await state.FirstEventFlushed.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
            state.FirstEventFlushedBeforeCompletion = !state.StreamCompleted.Task.IsCompleted;
            state.ReleaseRemainingEvents.TrySetResult();
            using var successfulResponse = await successfulResponseTask.ConfigureAwait(false);
            successfulResponse.EnsureSuccessStatusCode();
            var streamedEvents = await ReadEventsAsync(
                successfulResponse,
                timeout.Token).ConfigureAwait(false);

            using var replayResponse = await SendOnceAsync(
                client,
                requestBytes,
                request.ProtocolVersion,
                request.CorrelationId,
                idempotencyKey,
                validSignature: true,
                timeout.Token).ConfigureAwait(false);
            replayResponse.EnsureSuccessStatusCode();
            var replayEvents = await ReadEventsAsync(
                replayResponse,
                timeout.Token).ConfigureAwait(false);

            var conflictRequest = new LocalServiceRequest
            {
                CorrelationId = request.CorrelationId,
                ProtocolVersion = request.ProtocolVersion,
                Operation = request.Operation,
                Values = [100],
                ExtensionData = request.ExtensionData,
            };
            var conflictBytes = JsonSerializer.SerializeToUtf8Bytes(
                conflictRequest,
                LocalServiceProjectJsonContext.Default.LocalServiceRequest);
            using (var conflictResponse = await SendOnceAsync(
                       client,
                       conflictBytes,
                       conflictRequest.ProtocolVersion,
                       conflictRequest.CorrelationId,
                       idempotencyKey,
                       validSignature: true,
                       timeout.Token).ConfigureAwait(false))
            {
                DemoAssert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode, "相同幂等键不得绑定不同请求正文");
            }

            await app.StopAsync(timeout.Token).ConfigureAwait(false);
            restartedStore = new SqliteIdempotencyStore(databasePath);
            await restartedStore.InitializeAsync(timeout.Token).ConfigureAwait(false);
            restartedState = new LocalServiceState(FaultPlan.None, restartedStore, pauseFirstResponse: false);
            restartedApp = CreateServer(restartedState);
            await restartedApp.StartAsync(timeout.Token).ConfigureAwait(false);
            using var restartedClient = new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(2),
                UseProxy = false,
            })
            {
                BaseAddress = GetBoundAddress(restartedApp),
                Timeout = Timeout.InfiniteTimeSpan,
            };
            using var restartReplayResponse = await SendOnceAsync(
                restartedClient,
                requestBytes,
                request.ProtocolVersion,
                request.CorrelationId,
                idempotencyKey,
                validSignature: true,
                timeout.Token).ConfigureAwait(false);
            restartReplayResponse.EnsureSuccessStatusCode();
            var restartReplayEvents = await ReadEventsAsync(
                restartReplayResponse,
                timeout.Token).ConfigureAwait(false);

            context.WriteProperty("authentication", $"bad signatures={state.BadSignatures}, version rejects={state.VersionRejections}");
            context.WriteProperty("retry / replay", $"authenticated attempts={state.AuthenticatedAttempts}, executions={state.Executions}, durable replays={state.CacheHits}");
            context.WriteProperty(
                "fault plan",
                $"{TransientServiceFault} observed={faultPlan.GetInvocationCount(TransientServiceFault)}, injected=1");
            context.WriteProperty("streamed events", string.Join(", ", streamedEvents.Select(item => $"{item.Sequence}:{item.Kind}")));
            context.WriteProperty("first flush before completion", state.FirstEventFlushedBeforeCompletion);
            context.WriteProperty("extension fields", state.ExtensionFieldCount);
            context.WriteProperty("restart replay", $"executions={restartedState.Executions}, durable replays={restartedState.CacheHits}");

            DemoAssert.True(
                state.BadSignatures == 1 && state.VersionRejections == 1,
                "鉴权失败和版本协商失败都应被观测");
            DemoAssert.True(
                state.AuthenticatedAttempts == 4 && state.Executions == 1 && state.CacheHits == 2 && state.Conflicts == 1,
                "提交后 503、自动重试、显式重放和正文冲突不得导致重复业务执行");
            DemoAssert.True(state.FirstEventFlushedBeforeCompletion, "服务端应先 flush 第一条事件，再生成剩余事件");
            DemoAssert.Equal(1, state.ExtensionFieldCount, "JsonExtensionData 应保留一个未知字段");
            DemoAssert.SequenceEqual([1, 2], streamedEvents.Select(item => item.Sequence), "NDJSON 事件应逐行按序到达");
            DemoAssert.True(
                streamedEvents[1] is { Kind: "completed", Sum: 22, CorrelationId: "correlation-42" },
                "完成事件应携带协议关联 ID 和聚合结果");
            DemoAssert.SequenceEqual(streamedEvents, replayEvents, "同一幂等请求应返回缓存的同一逻辑响应");
            DemoAssert.SequenceEqual(streamedEvents, restartReplayEvents, "服务进程状态重建后仍应从 SQLite 逐字节重放同一逻辑响应");
            DemoAssert.True(
                restartedState.Executions == 0 && restartedState.CacheHits == 1,
                "重启后的新服务状态不得再次执行业务工厂");
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"本地协议项目超时：attempts={state.AuthenticatedAttempts}, firstFlushed={state.FirstEventFlushed.Task.IsCompleted}, " +
                $"released={state.ReleaseRemainingEvents.Task.IsCompleted}, streamCompleted={state.StreamCompleted.Task.IsCompleted}。",
                exception);
        }
        finally
        {
            state.ReleaseRemainingEvents.TrySetResult();
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
            if (restartedApp is not null)
            {
                await restartedApp.StopAsync(CancellationToken.None).ConfigureAwait(false);
                await restartedApp.DisposeAsync().ConfigureAwait(false);
            }

            if (restartedStore is not null)
            {
                await restartedStore.DisposeAsync().ConfigureAwait(false);
            }

            if (Directory.Exists(stateDirectory))
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        byte[] body,
        int protocolVersion,
        string correlationId,
        string idempotencyKey,
        int maximumAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumAttempts, 1);

        for (var attempt = 1; ; attempt++)
        {
            var response = await SendOnceAsync(
                client,
                body,
                protocolVersion,
                correlationId,
                idempotencyKey,
                validSignature: true,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || attempt >= maximumAttempts)
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(20 * attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task<HttpResponseMessage> SendOnceAsync(
        HttpClient client,
        byte[] body,
        int protocolVersion,
        string correlationId,
        string idempotencyKey,
        bool validSignature,
        CancellationToken cancellationToken)
    {
        const string path = "/api/jobs";
        var nonce = Guid.NewGuid().ToString("N");
        var signature = validSignature
            ? CreateSignature(HttpMethod.Post.Method, path, protocolVersion, correlationId, idempotencyKey, nonce, body)
            : Convert.ToBase64String(new byte[32]);
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.TryAddWithoutValidation("X-Protocol-Version", protocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        request.Headers.TryAddWithoutValidation("X-Nonce", nonce);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Headers.TryAddWithoutValidation("X-Signature", signature);
        return SendAndDisposeRequestAsync(client, request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeRequestAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            return await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<LocalServiceEvent>> ReadEventsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        var events = new List<LocalServiceEvent>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            var item = JsonSerializer.Deserialize(line, LocalServiceProjectJsonContext.Default.LocalServiceEvent)
                ?? throw new JsonException("NDJSON 事件反序列化为 null。");
            events.Add(item);
        }

        return events;
    }

    private static WebApplication CreateServer(LocalServiceState state)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(VersionedLocalServiceProjectDemo).Assembly.GetName().Name,
            Args = [],
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
            options.Listen(IPAddress.Loopback, 0, listenOptions => listenOptions.Protocols = HttpProtocols.Http1));

        var app = builder.Build();
        app.MapPost("/api/jobs", httpContext => HandleJobAsync(httpContext, state));
        return app;
    }

    private static async Task HandleJobAsync(HttpContext httpContext, LocalServiceState state)
    {
        var cancellationToken = httpContext.RequestAborted;
        var body = await ReadBoundedBodyAsync(httpContext.Request, maximumBytes: 32 * 1024, cancellationToken)
            .ConfigureAwait(false);

        var protocolText = httpContext.Request.Headers["X-Protocol-Version"].ToString();
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].ToString();
        var nonce = httpContext.Request.Headers["X-Nonce"].ToString();
        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].ToString();
        var suppliedSignature = httpContext.Request.Headers["X-Signature"].ToString();
        if (!int.TryParse(protocolText, out var protocolVersion) ||
            string.IsNullOrWhiteSpace(correlationId) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(idempotencyKey) ||
            !VerifySignature(
                suppliedSignature,
                httpContext.Request.Method,
                httpContext.Request.Path,
                protocolVersion,
                correlationId,
                idempotencyKey,
                nonce,
                body))
        {
            Interlocked.Increment(ref state.BadSignatures);
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!state.Nonces.TryAdd(nonce, 0))
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        if (protocolVersion != CurrentProtocolVersion)
        {
            Interlocked.Increment(ref state.VersionRejections);
            httpContext.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }

        LocalServiceRequest request;
        try
        {
            request = JsonSerializer.Deserialize(body, LocalServiceProjectJsonContext.Default.LocalServiceRequest)
                ?? throw new JsonException("请求正文为 JSON null。");
        }
        catch (JsonException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (request.ProtocolVersion != protocolVersion || request.CorrelationId != correlationId || request.Operation != "sum")
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        Interlocked.Increment(ref state.AuthenticatedAttempts);
        var bodyHash = Convert.ToHexString(SHA256.HashData(body));
        var resolution = await state.ResolveAsync(idempotencyKey, bodyHash, request, cancellationToken)
            .ConfigureAwait(false);
        if (resolution.Outcome == IdempotencyOutcome.Conflict)
        {
            httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        // 故意把瞬时失败放在 SQLite 提交之后：客户端无法知道服务是否已经执行业务，
        // 因此重试必须复用同一个业务幂等键，再从持久化响应中重放。
        if (state.FaultPlan.ShouldInject(TransientServiceFault))
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.ContentType = "application/x-ndjson; charset=utf-8";
        httpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
        var payload = resolution.Payload
            ?? throw new InvalidOperationException("成功的幂等解析必须包含持久化响应字节。");
        httpContext.Response.ContentLength = payload.Length;
        await httpContext.Response.StartAsync(cancellationToken).ConfigureAwait(false);

        var offset = 0;
        var index = 0;
        while (offset < payload.Length)
        {
            var newline = Array.IndexOf(payload, (byte)'\n', offset);
            if (newline < 0)
            {
                throw new InvalidOperationException("持久化 NDJSON 响应必须以换行符终止每个事件。");
            }

            var length = newline - offset + 1;
            await httpContext.Response.Body.WriteAsync(payload.AsMemory(offset, length), cancellationToken)
                .ConfigureAwait(false);
            await httpContext.Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);

            if (index == 0)
            {
                state.FirstEventFlushed.TrySetResult();
                if (state.PauseFirstResponse)
                {
                    await state.ReleaseRemainingEvents.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            offset += length;
            index++;
        }

        state.StreamCompleted.TrySetResult();
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpRequest request,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength > maximumBytes)
        {
            throw new Microsoft.AspNetCore.Http.BadHttpRequestException(
                "请求正文过大。",
                StatusCodes.Status413PayloadTooLarge);
        }

        using var destination = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new Microsoft.AspNetCore.Http.BadHttpRequestException(
                    "请求正文过大。",
                    StatusCodes.Status413PayloadTooLarge);
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return destination.ToArray();
    }

    private static string CreateSignature(
        string method,
        string path,
        int protocolVersion,
        string correlationId,
        string idempotencyKey,
        string nonce,
        ReadOnlySpan<byte> body)
    {
        var bodyHash = Convert.ToBase64String(SHA256.HashData(body));
        var canonical = string.Join(
            '\n',
            method,
            path,
            protocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            idempotencyKey,
            nonce,
            bodyHash);
        var signature = HMACSHA256.HashData(SharedSecret, Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(signature);
    }

    private static bool VerifySignature(
        string suppliedSignature,
        string method,
        string path,
        int protocolVersion,
        string correlationId,
        string idempotencyKey,
        string nonce,
        ReadOnlySpan<byte> body)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(suppliedSignature);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Convert.FromBase64String(CreateSignature(
            method,
            path,
            protocolVersion,
            correlationId,
            idempotencyKey,
            nonce,
            body));
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    private static Uri GetBoundAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault();
        return address is null
            ? throw new InvalidOperationException("Kestrel 启动后没有报告回环监听地址。")
            : new Uri(address, UriKind.Absolute);
    }

    private sealed class LocalServiceState(
        FaultPlan faultPlan,
        SqliteIdempotencyStore idempotencyStore,
        bool pauseFirstResponse)
    {
        public ConcurrentDictionary<string, byte> Nonces { get; } = new(StringComparer.Ordinal);

        public FaultPlan FaultPlan { get; } = faultPlan;

        public bool PauseFirstResponse { get; } = pauseFirstResponse;

        public TaskCompletionSource FirstEventFlushed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseRemainingEvents { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StreamCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int BadSignatures;
        public int VersionRejections;
        public int AuthenticatedAttempts;
        public int Executions;
        public int CacheHits;
        public int Conflicts;

        public int ExtensionFieldCount { get; private set; }

        public bool FirstEventFlushedBeforeCompletion { get; set; }

        public async Task<IdempotencyResult> ResolveAsync(
            string idempotencyKey,
            string bodyHash,
            LocalServiceRequest request,
            CancellationToken cancellationToken)
        {
            var result = await idempotencyStore.ExecuteAsync(
                idempotencyKey,
                bodyHash,
                _ =>
                {
                    Interlocked.Increment(ref Executions);
                    ExtensionFieldCount = request.ExtensionData?.Count ?? 0;
                    LocalServiceEvent[] events =
                    [
                        new(1, "accepted", request.CorrelationId, null),
                        new(2, "completed", request.CorrelationId, request.Values.Sum()),
                    ];
                    return ValueTask.FromResult(SerializeEvents(events));
                },
                cancellationToken).ConfigureAwait(false);

            switch (result.Outcome)
            {
                case IdempotencyOutcome.Replayed:
                    Interlocked.Increment(ref CacheHits);
                    break;
                case IdempotencyOutcome.Conflict:
                    Interlocked.Increment(ref Conflicts);
                    break;
            }

            return result;
        }

        private static byte[] SerializeEvents(IEnumerable<LocalServiceEvent> events)
        {
            var text = string.Concat(events.Select(item =>
                JsonSerializer.Serialize(item, LocalServiceProjectJsonContext.Default.LocalServiceEvent) + "\n"));
            return Encoding.UTF8.GetBytes(text);
        }
    }
}

internal sealed class LocalServiceRequest
{
    public string CorrelationId { get; set; } = string.Empty;

    public int ProtocolVersion { get; set; }

    public string Operation { get; set; } = string.Empty;

    public int[] Values { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

internal sealed record LocalServiceEvent(
    int Sequence,
    string Kind,
    string CorrelationId,
    int? Sum);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LocalServiceRequest))]
[JsonSerializable(typeof(LocalServiceEvent))]
internal sealed partial class LocalServiceProjectJsonContext : JsonSerializerContext;
