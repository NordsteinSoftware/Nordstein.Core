using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AwesomeAssertions;
using Nordstein.Core.Testing.Certificates;
using Nordstein.Core.Testing.Time;

namespace Nordstein.Core.Testing.Tests.Certificates;

/// <summary>
/// <see cref="ThrowawayCertificateAuthority"/>: an in-memory CA whose short-lived server
/// certificates chain to it, carry the requested names and complete a real TLS handshake.
/// </summary>
[TestClass]
public sealed class ThrowawayCertificateAuthorityTests
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    public required TestContext TestContext { get; init; }

    private CancellationToken CancellationToken => TestContext.CancellationToken;

    [TestMethod]
    public void Authority_IsASelfSignedCaValidForOneDayAroundNow()
    {
        var clock = new ManualClock(new DateTimeOffset(2031, 5, 6, 7, 0, 0, TimeSpan.Zero));
        using var authority = new ThrowawayCertificateAuthority(clock);

        X509Certificate2 ca = authority.Certificate;

        ca.Subject.Should().Be("CN=Throwaway Test CA");
        ca.Issuer.Should().Be(ca.Subject);
        ca.HasPrivateKey.Should().BeFalse("the authority's key never leaves it");
        ca.NotBefore.ToUniversalTime().Should().Be(new DateTime(2031, 5, 6, 6, 0, 0, DateTimeKind.Utc));
        ca.NotAfter.ToUniversalTime().Should().Be(new DateTime(2031, 5, 7, 7, 0, 0, DateTimeKind.Utc));
        X509BasicConstraintsExtension constraints = ca.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        constraints.CertificateAuthority.Should().BeTrue();
        constraints.HasPathLengthConstraint.Should().BeTrue();
        constraints.PathLengthConstraint.Should().Be(0);
        constraints.Critical.Should().BeTrue();
        ca.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages
            .Should().Be(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign);
    }

    [TestMethod]
    public void Authority_WithACommonName_UsesIt()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System, "Integration Suite CA");

        authority.Certificate.Subject.Should().Be("CN=Integration Suite CA");
    }

    [TestMethod]
    public void CertificatePem_IsTheAuthorityCertificate()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);

        using X509Certificate2 loaded = X509Certificate2.CreateFromPem(authority.CertificatePem);

        loaded.Thumbprint.Should().Be(authority.Certificate.Thumbprint);
        authority.CertificatePem.Should().StartWith("-----BEGIN CERTIFICATE-----").And.EndWith("-----END CERTIFICATE-----\n");
    }

    [TestMethod]
    public void IssueServerCertificate_CarriesTheNamesAndChainsToTheAuthority()
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        using var authority = new ThrowawayCertificateAuthority(clock);

        using IssuedCertificate issued = authority.IssueServerCertificate("service.example.test", "127.0.0.1", "::1");

        X509Certificate2 server = issued.Certificate;
        server.Subject.Should().Be("CN=service.example.test");
        server.Issuer.Should().Be(authority.Certificate.Subject);
        server.HasPrivateKey.Should().BeTrue();
        X509SubjectAlternativeNameExtension names = server.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        names.EnumerateDnsNames().Should().Equal("service.example.test");
        names.EnumerateIPAddresses().Should().Equal(IPAddress.Loopback, IPAddress.IPv6Loopback);
        server.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority.Should().BeFalse();
        server.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages.Should().Be(X509KeyUsageFlags.DigitalSignature);
        server.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages
            .Cast<Oid>().Select(oid => oid.Value).Should().Equal(ServerAuthentication);
        server.Extensions.OfType<X509AuthorityKeyIdentifierExtension>().Single().KeyIdentifier?.ToArray()
            .Should().Equal(authority.Certificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single().SubjectKeyIdentifierBytes.ToArray());

        BuildChain(authority, server, clock.GetUtcNow()).Should().BeTrue();
    }

    [TestMethod]
    public void IssueServerCertificate_IsValidFromAnHourBeforeNowAndNeverOutlivesTheAuthority()
    {
        var start = new DateTimeOffset(2031, 5, 6, 7, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(start);
        using var authority = new ThrowawayCertificateAuthority(clock);
        clock.Advance(TimeSpan.FromHours(20));

        using IssuedCertificate issued = authority.IssueServerCertificate("late.example.test");

        issued.Certificate.NotBefore.ToUniversalTime().Should().Be(start.AddHours(19).UtcDateTime);
        issued.Certificate.NotAfter.Should().Be(authority.Certificate.NotAfter, "a leaf never outlives its issuer");
    }

    [TestMethod]
    public void IssueServerCertificate_WhenTheClockWentBack_NeverStartsBeforeTheAuthority()
    {
        var start = new DateTimeOffset(2031, 5, 6, 7, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(start);
        using var authority = new ThrowawayCertificateAuthority(clock);
        clock.SetUtcNow(start.AddMinutes(-30));

        using IssuedCertificate issued = authority.IssueServerCertificate("skewed.example.test");

        issued.Certificate.NotBefore.Should().Be(authority.Certificate.NotBefore);
        issued.Certificate.NotAfter.ToUniversalTime().Should().Be(start.AddMinutes(-30).AddDays(1).UtcDateTime);
    }

    [TestMethod]
    public void IssueServerCertificate_AfterTheAuthorityExpired_Throws()
    {
        var clock = new ManualClock(new DateTimeOffset(2031, 5, 6, 7, 0, 0, TimeSpan.Zero));
        using var authority = new ThrowawayCertificateAuthority(clock);
        clock.Advance(TimeSpan.FromDays(1));

        Action act = () => authority.IssueServerCertificate("expired.example.test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*authority expired*");
    }

    [TestMethod]
    public void IssueServerCertificate_WhenTheClockWentBackBeforeTheAuthority_Throws()
    {
        var start = new DateTimeOffset(2031, 5, 6, 7, 0, 0, TimeSpan.Zero);
        var clock = new ManualClock(start);
        using var authority = new ThrowawayCertificateAuthority(clock);
        clock.SetUtcNow(start.AddHours(-2));

        Action act = () => authority.IssueServerCertificate("early.example.test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not valid before*");
    }

    [TestMethod]
    public void IssueServerCertificate_PemsLoadAndBelongTogether()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);
        using IssuedCertificate issued = authority.IssueServerCertificate("pem.example.test");

        using X509Certificate2 fromPem = X509Certificate2.CreateFromPem(issued.CertificatePem, issued.PrivateKeyPem);

        fromPem.Thumbprint.Should().Be(issued.Certificate.Thumbprint);
        issued.PrivateKeyPem.Should().StartWith("-----BEGIN PRIVATE KEY-----");
        issued.ChainPem.Should().Be(issued.CertificatePem + authority.CertificatePem);
        using ECDsa? key = fromPem.GetECDsaPrivateKey();
        using ECDsa? publicKey = issued.Certificate.GetECDsaPublicKey();
        Assert.IsNotNull(key);
        Assert.IsNotNull(publicKey);
        byte[] data = [1, 2, 3];
        byte[] signature = key.SignData(data, HashAlgorithmName.SHA256);
        publicKey.VerifyData(data, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }

    [TestMethod]
    public void IssueServerCertificate_GivesEveryCertificateItsOwnPositiveSerial()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);

        string[] serials = Enumerable.Range(0, 20)
            .Select(_ =>
            {
                using IssuedCertificate issued = authority.IssueServerCertificate("serial.example.test");
                byte[] serial = issued.Certificate.GetSerialNumber();
                (serial[^1] & 0x80).Should().Be(0, "a serial number is a positive integer (little-endian, sign bit clear)");
                return issued.Certificate.SerialNumber;
            })
            .ToArray();

        serials.Should().OnlyHaveUniqueItems();
    }

    [TestMethod]
    public void IssueServerCertificate_FromAnotherAuthority_DoesNotChainToThisOne()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);
        using var stranger = new ThrowawayCertificateAuthority(TimeProvider.System);
        using IssuedCertificate foreign = stranger.IssueServerCertificate("foreign.example.test");

        BuildChain(authority, foreign.Certificate, DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    public void IssueServerCertificate_WithABlankName_Throws(string name)
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);

        Action act = () => authority.IssueServerCertificate("ok.example.test", name);

        act.Should().Throw<ArgumentException>().WithParameterName("names");
    }

    [TestMethod]
    public void IssueServerCertificate_WithoutNames_Throws()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);

        Action act = () => authority.IssueServerCertificate();

        act.Should().Throw<ArgumentException>().WithParameterName("names");
    }

    [TestMethod]
    public void NullArguments_Throw()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);

#pragma warning disable CS8625 // WHY: the guards under test are reachable only with the null the annotations forbid.
        Action provider = () => _ = new ThrowawayCertificateAuthority(null);
        Action commonName = () => _ = new ThrowawayCertificateAuthority(TimeProvider.System, null);
        Action names = () => authority.IssueServerCertificate(null);
        Action name = () => authority.IssueServerCertificate("ok.example.test", null);
#pragma warning restore CS8625

        provider.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
        commonName.Should().Throw<ArgumentNullException>().WithParameterName("commonName");
        names.Should().Throw<ArgumentNullException>().WithParameterName("names");
        name.Should().Throw<ArgumentException>().WithParameterName("names");
    }

    [TestMethod]
    public void Authority_WithABlankCommonName_Throws()
    {
        Action act = () => _ = new ThrowawayCertificateAuthority(TimeProvider.System, " ");

        act.Should().Throw<ArgumentException>().WithParameterName("commonName");
    }

    [TestMethod]
    public void IssueServerCertificate_AfterDispose_Throws()
    {
        var authority = new ThrowawayCertificateAuthority(TimeProvider.System);

        authority.Dispose();
        authority.Dispose();

        Action act = () => authority.IssueServerCertificate("gone.example.test");
        act.Should().Throw<ObjectDisposedException>();
    }

    [TestMethod]
    public void IssuedCertificate_Dispose_IsIdempotent()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);
        IssuedCertificate issued = authority.IssueServerCertificate("twice.example.test");

        issued.Dispose();
        Action again = issued.Dispose;

        again.Should().NotThrow();
    }

    [TestMethod]
    public async Task IssueServerCertificate_FromManyThreads_IssuesValidDistinctCertificates()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);

        IssuedCertificate[] issued = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => authority.IssueServerCertificate($"host{index}.example.test"))));
        try
        {
            issued.Select(certificate => certificate.Certificate.SerialNumber).Should().OnlyHaveUniqueItems();
            issued.Should().OnlyContain(certificate => BuildChain(authority, certificate.Certificate, DateTimeOffset.UtcNow));
        }
        finally
        {
            foreach (IssuedCertificate certificate in issued)
            {
                certificate.Dispose();
            }
        }
    }

    [TestMethod]
    public async Task IssuedCertificate_CompletesATlsHandshakeWithAClientThatTrustsOnlyTheAuthority()
    {
        using var authority = new ThrowawayCertificateAuthority(TimeProvider.System);
        using IssuedCertificate issued = authority.IssueServerCertificate("localhost", "127.0.0.1");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Task server = Task.Run(
            async () =>
            {
                using TcpClient accepted = await listener.AcceptTcpClientAsync(CancellationToken);
                await using var tls = new SslStream(accepted.GetStream());
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = issued.Certificate },
                    CancellationToken);
                await tls.WriteAsync(new byte[] { 42 }, CancellationToken);
            },
            CancellationToken);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, CancellationToken);
        await using var clientTls = new SslStream(client.GetStream());
        var chainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
        };
        chainPolicy.CustomTrustStore.Add(authority.Certificate);
        await clientTls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = "localhost",
                CertificateChainPolicy = chainPolicy,
                EnabledSslProtocols = SslProtocols.None,
            },
            CancellationToken);
        byte[] received = new byte[1];
        await clientTls.ReadExactlyAsync(received, CancellationToken);
        await server;

        received.Should().Equal(42);
        clientTls.IsAuthenticated.Should().BeTrue();
    }

    private static bool BuildChain(ThrowawayCertificateAuthority authority, X509Certificate2 leaf, DateTimeOffset at)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(authority.Certificate);
        chain.ChainPolicy.VerificationTime = at.UtcDateTime;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthentication));
        return chain.Build(leaf);
    }
}
