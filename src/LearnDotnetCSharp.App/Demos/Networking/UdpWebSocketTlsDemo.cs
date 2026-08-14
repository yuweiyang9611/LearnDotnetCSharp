using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LearnDotnetCSharp.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LearnDotnetCSharp.Demos.Networking;

public sealed class UdpWebSocketTlsDemo : IDemo
{
    private static readonly SslApplicationProtocol LearningProtocol = new("learn-dotnet");

    public DemoMetadata Metadata { get; } = new(
        "networking.udp-websocket-tls",
        "networking",
        "DNS、UDP、WebSocket 与 SslStream/TLS",
        "只使用回环地址运行 DNS 解析、UDP 数据报、Kestrel WebSocket，以及带证书校验和 ALPN 的 TLS 字节流。",
        [16],
        ["DNS", "UDP", "WebSocket", "Kestrel", "SslStream", "TLS", "ALPN", "X.509"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var addresses = await Dns.GetHostAddressesAsync("localhost", timeout.Token).ConfigureAwait(false);
        var udp = await ExerciseUdpEchoAsync(timeout.Token).ConfigureAwait(false);
        var webSocket = await ExerciseWebSocketAsync(timeout.Token).ConfigureAwait(false);

        using var certificateKey = RSA.Create(2048);
        using var certificate = CreateLoopbackCertificate(certificateKey);
        var tls = await ExerciseTlsAsync(certificate, timeout.Token).ConfigureAwait(false);

        context.WriteProperty(
            "DNS localhost",
            string.Join(", ", addresses.Distinct().Select(address => address.ToString())));
        context.WriteProperty("UDP endpoint", udp.ServerEndpoint);
        context.WriteProperty("UDP echo", udp.Response);
        context.WriteProperty("WebSocket endpoint", webSocket.Endpoint);
        context.WriteProperty("WebSocket response", webSocket.Response);
        context.WriteProperty("TLS protocol", tls.Protocol);
        context.WriteProperty("TLS ALPN", tls.ApplicationProtocol);
        context.WriteProperty("TLS response", tls.Response);
        context.WriteLine("  UDP 保留消息边界；WebSocket 在 HTTP/1.1 Upgrade 后传输帧；SslStream 则在 TCP 上提供经身份验证和加密的字节流。三者不能互换抽象。 ");

        DemoAssert.True(addresses.Length > 0, "localhost 应至少解析为一个回环地址");
        DemoAssert.True(udp.Response == "ECHO:datagram", "UDP 服务器应把单个数据报回显给发送端");
        DemoAssert.True(webSocket.Response == "ACK:frame", "WebSocket 端点应处理一个文本帧");
        DemoAssert.True(
            tls.Response == "PONG" && tls.ApplicationProtocol == LearningProtocol.ToString(),
            "TLS 双方应通过证书指纹校验、协商 ALPN 并交换加密负载");
    }

    private static async Task<UdpResult> ExerciseUdpEchoAsync(CancellationToken cancellationToken)
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var client = new UdpClient(AddressFamily.InterNetwork);
        var endpoint = (IPEndPoint?)server.Client.LocalEndPoint
            ?? throw new InvalidOperationException("UDP 服务器没有报告本地端点。");

        var serverTask = Task.Run(
            async () =>
            {
                var received = await server.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var request = Encoding.UTF8.GetString(received.Buffer);
                var response = Encoding.UTF8.GetBytes($"ECHO:{request}");
                await server.SendAsync(response, received.RemoteEndPoint, cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

        await client
            .SendAsync(Encoding.UTF8.GetBytes("datagram"), endpoint, cancellationToken)
            .ConfigureAwait(false);
        var echoed = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        await serverTask.ConfigureAwait(false);

        return new UdpResult(endpoint, Encoding.UTF8.GetString(echoed.Buffer));
    }

    private static async Task<WebSocketResult> ExerciseWebSocketAsync(CancellationToken cancellationToken)
    {
        await using var app = CreateWebSocketServer();
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var httpAddress = GetBoundAddress(app);
            var webSocketAddress = new UriBuilder(httpAddress)
            {
                Scheme = "ws",
                Path = "/ws",
            }.Uri;

            using var client = new ClientWebSocket();
            await client.ConnectAsync(webSocketAddress, cancellationToken).ConfigureAwait(false);
            await client
                .SendAsync(
                    Encoding.UTF8.GetBytes("frame"),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken)
                .ConfigureAwait(false);

            var buffer = new byte[128];
            var received = await client.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            var response = Encoding.UTF8.GetString(buffer.AsSpan(0, received.Count));
            await client
                .CloseAsync(WebSocketCloseStatus.NormalClosure, "demo complete", cancellationToken)
                .ConfigureAwait(false);
            return new WebSocketResult(webSocketAddress, response);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static WebApplication CreateWebSocketServer()
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(UdpWebSocketTlsDemo).Assembly.GetName().Name,
                Args = [],
            });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        app.UseWebSockets();
        app.MapGet(
            "/ws",
            async (HttpContext httpContext) =>
            {
                if (!httpContext.WebSockets.IsWebSocketRequest)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                using var socket = await httpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                var buffer = new byte[128];
                var received = await socket.ReceiveAsync(buffer, httpContext.RequestAborted).ConfigureAwait(false);
                var request = Encoding.UTF8.GetString(buffer.AsSpan(0, received.Count));
                await socket
                    .SendAsync(
                        Encoding.UTF8.GetBytes($"ACK:{request}"),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        httpContext.RequestAborted)
                    .ConfigureAwait(false);
                await socket
                    .CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "server complete",
                        httpContext.RequestAborted)
                    .ConfigureAwait(false);
            });
        return app;
    }

    private static async Task<TlsResult> ExerciseTlsAsync(
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        try
        {
            var endpoint = (IPEndPoint)listener.LocalEndpoint;
            var serverTask = RunTlsServerAsync(listener, certificate, cancellationToken);
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken).ConfigureAwait(false);

            var expectedCertificateHash = SHA256.HashData(certificate.RawData);
            await using var tls = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, peerCertificate, _, _) =>
                    peerCertificate is not null &&
                    CryptographicOperations.FixedTimeEquals(
                        expectedCertificateHash,
                        SHA256.HashData(peerCertificate.GetRawCertData())));
            await tls
                .AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = "localhost",
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                        ApplicationProtocols = [LearningProtocol],
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            await tls.WriteAsync("PING"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
            var response = new byte[4];
            await tls.ReadExactlyAsync(response, cancellationToken).ConfigureAwait(false);
            var serverResult = await serverTask.ConfigureAwait(false);

            DemoAssert.True(
                serverResult.Protocol == tls.SslProtocol &&
                serverResult.ApplicationProtocol == tls.NegotiatedApplicationProtocol.ToString(),
                "TLS 客户端和服务器必须观察到相同的协议与 ALPN 结果");
            return new TlsResult(
                tls.SslProtocol,
                tls.NegotiatedApplicationProtocol.ToString(),
                Encoding.ASCII.GetString(response));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<TlsServerResult> RunTlsServerAsync(
        TcpListener listener,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
        await using var tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await tls
            .AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificate = certificate,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ApplicationProtocols = [LearningProtocol],
                },
                cancellationToken)
            .ConfigureAwait(false);

        var request = new byte[4];
        await tls.ReadExactlyAsync(request, cancellationToken).ConfigureAwait(false);
        DemoAssert.True(request.AsSpan().SequenceEqual("PING"u8), "TLS 服务器应收到完整的 PING 字节流");
        await tls.WriteAsync("PONG"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);
        return new TlsServerResult(tls.SslProtocol, tls.NegotiatedApplicationProtocol.ToString());
    }

    private static Uri GetBoundAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.SingleOrDefault();
        return address is null
            ? throw new InvalidOperationException("Kestrel 启动后没有报告监听地址。")
            : new Uri(address, UriKind.Absolute);
    }

    private static X509Certificate2 CreateLoopbackCertificate(RSA certificateKey)
    {
        var request = new CertificateRequest(
            "CN=localhost",
            certificateKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                critical: true));
        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName("localhost");
        subjectAlternativeName.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(subjectAlternativeName.Build());

        var now = DateTimeOffset.UtcNow;
        using var ephemeralCertificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pfx = ephemeralCertificate.Export(X509ContentType.Pfx, password);
        var storageFlags = OperatingSystem.IsWindows()
            ? X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;
        return X509CertificateLoader.LoadPkcs12(pfx, password, storageFlags);
    }

    private sealed record UdpResult(IPEndPoint ServerEndpoint, string Response);

    private sealed record WebSocketResult(Uri Endpoint, string Response);

    private sealed record TlsResult(SslProtocols Protocol, string ApplicationProtocol, string Response);

    private sealed record TlsServerResult(SslProtocols Protocol, string ApplicationProtocol);
}
