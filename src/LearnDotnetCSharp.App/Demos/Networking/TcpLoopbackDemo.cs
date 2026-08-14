using System.Net;
using System.Net.Sockets;
using System.Text;
using LearnDotnetCSharp.Infrastructure;

namespace LearnDotnetCSharp.Demos.Networking;

public sealed class TcpLoopbackDemo : IDemo
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public DemoMetadata Metadata { get; } = new(
        "networking.tcp-loopback",
        "networking",
        "TCP 回环请求/响应",
        "在 127.0.0.1 的临时端口上建立一次 TCP 会话，交换 UTF-8 帧并在 finally 中释放监听器。",
        [16],
        ["TcpListener", "TcpClient", "NetworkStream", "Loopback"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;

        var serverTask = RunServerAsync(listener, timeout.Token);
        var clientTask = RunClientAsync(endpoint, timeout.Token);

        try
        {
            await Task.WhenAll(serverTask, clientTask).ConfigureAwait(false);
            var server = await serverTask.ConfigureAwait(false);
            var response = await clientTask.ConfigureAwait(false);

            if (server.Request != "PING|42" ||
                response != "ACK|42" ||
                !server.RemoteWasLoopback ||
                !IPAddress.IsLoopback(endpoint.Address))
            {
                throw new InvalidOperationException("TCP 回环请求/响应不变量不成立。");
            }

            context.WriteProperty("Listener", endpoint);
            context.WriteProperty("Bound to loopback", IPAddress.IsLoopback(endpoint.Address));
            context.WriteProperty("Server received", server.Request);
            context.WriteProperty("Client received", response);
            context.WriteProperty("Peer was loopback", server.RemoteWasLoopback);
        }
        finally
        {
            timeout.Cancel();
            listener.Stop();
        }
    }

    private static async Task<ServerObservation> RunServerAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        var remoteEndpoint = (IPEndPoint?)client.Client.RemoteEndPoint;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Utf8WithoutBom,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1_024,
            leaveOpen: true);
        await using var writer = new StreamWriter(
            stream,
            Utf8WithoutBom,
            bufferSize: 1_024,
            leaveOpen: true)
        {
            NewLine = "\n",
        };

        var request = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                      ?? throw new EndOfStreamException("客户端在发送请求前关闭了连接。");
        var response = request == "PING|42" ? "ACK|42" : "NACK";
        await writer.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        return new ServerObservation(
            request,
            remoteEndpoint is not null && IPAddress.IsLoopback(remoteEndpoint.Address));
    }

    private static async Task<string> RunClientAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork)
        {
            NoDelay = true,
        };
        await client.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken).ConfigureAwait(false);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Utf8WithoutBom,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1_024,
            leaveOpen: true);
        await using var writer = new StreamWriter(
            stream,
            Utf8WithoutBom,
            bufferSize: 1_024,
            leaveOpen: true)
        {
            NewLine = "\n",
        };

        await writer.WriteLineAsync("PING|42".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)
               ?? throw new EndOfStreamException("服务端在发送响应前关闭了连接。");
    }

    private sealed record ServerObservation(string Request, bool RemoteWasLoopback);
}
