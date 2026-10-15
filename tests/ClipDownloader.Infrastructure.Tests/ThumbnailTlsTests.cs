using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ClipDownloader.Application.Models;
using ClipDownloader.Infrastructure.Thumbnails;
using Xunit;

namespace ClipDownloader.Infrastructure.Tests;

public sealed class ThumbnailTlsTests
{
    [Fact]
    public async Task RealSocketKeepsOriginalTlsHostAndRejectsUntrustedCertificate()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=images.example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("images.example.com"); request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int localPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        string? sni = null;
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            using var tls = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificateSelectionCallback = (_, host) => { sni = host; return cert; },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, timeout.Token);
                byte[] one = new byte[1]; _ = await tls.ReadAsync(one, timeout.Token);
            }
            catch (Exception e) when (e is AuthenticationException or IOException) { }
        }, timeout.Token);
        IPAddress? pinned = null;
        // Only this test connector maps an approved test IP to its local TLS endpoint.
        var connection = new ThumbnailConnection((_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            async (ip, port, token) =>
            {
                pinned = ip; Assert.Equal(443, port);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(IPAddress.Loopback, localPort, token); return new NetworkStream(socket, true); }
                catch { socket.Dispose(); throw; }
            });
        using var service = new HttpThumbnailContentService(HttpThumbnailContentService.CreateHandler(connection));
        var result = await service.GetAsync(new("https://images.example.com/a"), timeout.Token);
        await server;
        Assert.Equal(ThumbnailFailure.HttpFailure, result.Failure);
        Assert.Equal("images.example.com", sni); Assert.Equal(IPAddress.Parse("8.8.8.8"), pinned);
    }
}
