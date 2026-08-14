using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

public sealed class Http2And3LoopbackDemo : IDemo
{
    public DemoMetadata Metadata { get; } = new(
        "networking.http2-http3",
        "networking",
        "本地 TLS 上的 HTTP/2 与条件式 HTTP/3",
        "启动真实 Kestrel 端点并以 RequestVersionExact 验证协商结果；HTTP/3 依赖操作系统 QUIC/MsQuic，缺失时明确跳过。",
        [16],
        ["Kestrel", "TLS", "ALPN", "HTTP/2", "HTTP/3", "QUIC", "RequestVersionExact"]);

    public async ValueTask RunAsync(DemoContext context, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var certificateKey = RSA.Create(2048);
        using var certificate = CreateLoopbackCertificate(certificateKey);

        var http2 = await ExerciseProtocolAsync(
                HttpProtocols.Http2,
                HttpVersion.Version20,
                certificate,
                timeout.Token)
            .ConfigureAwait(false);

        context.WriteProperty("HTTP/2 response version", http2.ResponseVersion);
        context.WriteProperty("HTTP/2 server protocol", http2.ServerProtocol);
        DemoAssert.True(
            http2.ResponseVersion == HttpVersion.Version20 && http2.ServerProtocol == "HTTP/2",
            "精确版本策略必须完成真实 HTTP/2 协商");

        if (!IsQuicSupported())
        {
            context.WriteProperty("HTTP/3", "SKIPPED：当前操作系统、运行时或 MsQuic 不支持 QUIC");
            return;
        }

        var http3 = await ExerciseProtocolAsync(
                HttpProtocols.Http3,
                HttpVersion.Version30,
                certificate,
                timeout.Token)
            .ConfigureAwait(false);

        context.WriteProperty("HTTP/3 response version", http3.ResponseVersion);
        context.WriteProperty("HTTP/3 server protocol", http3.ServerProtocol);
        DemoAssert.True(
            http3.ResponseVersion == HttpVersion.Version30 && http3.ServerProtocol == "HTTP/3",
            "平台声明支持 QUIC 时，精确版本策略必须完成真实 HTTP/3 协商");
    }

    private static async Task<ProtocolResult> ExerciseProtocolAsync(
        HttpProtocols protocol,
        Version requestedVersion,
        X509Certificate2 certificate,
        CancellationToken cancellationToken)
    {
        await using var app = CreateServer(protocol, certificate);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var address = GetBoundAddress(app);
            var certificateHash = SHA256.HashData(certificate.RawData);
            using var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5),
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, peerCertificate, _, _) =>
                        peerCertificate is not null &&
                        CryptographicOperations.FixedTimeEquals(
                            certificateHash,
                            SHA256.HashData(peerCertificate.GetRawCertData())),
                },
                UseProxy = false,
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(8),
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "protocol"))
            {
                Version = requestedVersion,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            };
            using var response = await SendWithProtocolDiagnosticsAsync(
                    client,
                    request,
                    requestedVersion,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var serverProtocol = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ProtocolResult(response.Version, serverProtocol);
        }
        finally
        {
            await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task<HttpResponseMessage> SendWithProtocolDiagnosticsAsync(
        HttpClient client,
        HttpRequestMessage request,
        Version requestedVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"HTTP/{requestedVersion} 本地协商失败：{exception.InnerException?.Message ?? exception.Message}",
                exception);
        }
    }

    private static WebApplication CreateServer(HttpProtocols protocol, X509Certificate2 certificate)
    {
        var builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Http2And3LoopbackDemo).Assembly.GetName().Name,
                Args = [],
            });
        builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("LEARN_DOTNET_HTTP_TRACE") == "1")
        {
            builder.Logging.AddSimpleConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
        }

        if (protocol == HttpProtocols.Http3)
        {
            builder.WebHost.UseQuic();
        }

        builder.WebHost.ConfigureKestrel(
            options =>
            {
                options.Listen(
                    IPAddress.Loopback,
                    port: 0,
                    listenOptions =>
                    {
                        listenOptions.Protocols = protocol;
                        listenOptions.UseHttps(certificate);
                    });
            });

        var app = builder.Build();
        app.MapGet("/protocol", (HttpContext httpContext) => Results.Text(httpContext.Request.Protocol));
        return app;
    }

    private static Uri GetBoundAddress(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?.SingleOrDefault();
        return address is null
            ? throw new InvalidOperationException("Kestrel 启动后没有报告监听地址。")
            : new Uri(address, UriKind.Absolute);
    }

    private static bool IsQuicSupported()
    {
        var quicConnectionType = Type.GetType(
            "System.Net.Quic.QuicConnection, System.Net.Quic",
            throwOnError: false);
        var property = quicConnectionType?.GetProperty(
            "IsSupported",
            BindingFlags.Public | BindingFlags.Static);
        return property?.GetValue(null) is true;
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
        return X509CertificateLoader.LoadPkcs12(
            pfx,
            password,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    private sealed record ProtocolResult(Version ResponseVersion, string ServerProtocol);
}
