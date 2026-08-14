using System.Net;
using System.Net.Sockets;
using System.Text;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Networking;

public sealed class HttpClientLoopbackDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "networking.httpclient-loopback",
        "networking",
        "HttpClient 与本地 HTTP/1.1",
        "用 TcpListener 托管最小本地 HTTP 端点，并以禁用代理、带超时的 HttpClient 完成请求。",
        [16],
        ["HttpClient", "HTTP/1.1", "SocketsHttpHandler", "Loopback"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var uri = new Uri($"http://127.0.0.1:{endpoint.Port}/health?source=demo", UriKind.Absolute);

        var serverTask = ServeSingleRequestAsync(listener, timeout.Token);
        var clientTask = SendRequestAsync(uri, timeout.Token);

        try
        {
            await Task.WhenAll(serverTask, clientTask).ConfigureAwait(false);
            var requestLine = await serverTask.ConfigureAwait(false);
            var response = await clientTask.ConfigureAwait(false);

            if (requestLine != "GET /health?source=demo HTTP/1.1" ||
                response.StatusCode != HttpStatusCode.OK ||
                response.Body != "{\"status\":\"ok\",\"transport\":\"loopback\"}")
            {
                throw new InvalidOperationException("本地 HTTP 请求或响应不变量不成立。");
            }

            context.WriteProperty("Request URI", uri);
            context.WriteProperty("Request line", requestLine);
            context.WriteProperty("Status", $"{(int)response.StatusCode} {response.StatusCode}");
            context.WriteProperty("Response body", response.Body);
            context.WriteProperty("Proxy used", false);
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
        }
    }

    private static async Task<string> ServeSingleRequestAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        var requestHeaders = await ReadHeadersAsync(stream, cancellationToken).ConfigureAwait(false);
        var requestLineEnd = requestHeaders.IndexOf("\r\n", StringComparison.Ordinal);
        var requestLine = requestLineEnd >= 0 ? requestHeaders[..requestLineEnd] : requestHeaders;

        const string body = "{\"status\":\"ok\",\"transport\":\"loopback\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headerBytes = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n");

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return requestLine;
    }

    private static async Task<HttpObservation> SendRequestAsync(Uri uri, CancellationToken cancellationToken)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(1),
            UseProxy = false,
        };
        using var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(2),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        request.Headers.Add("X-Demo-Trace", "loopback-only");

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new HttpObservation(response.StatusCode, body);
    }

    private static async Task<string> ReadHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1_024];
        var length = 0;

        while (length < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(length, buffer.Length - length), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
            {
                return Encoding.ASCII.GetString(buffer, 0, length);
            }
        }

        throw new InvalidDataException("HTTP 请求头缺少终止标记或超过 8 KiB。");
    }

    private sealed record HttpObservation(HttpStatusCode StatusCode, string Body);
}
