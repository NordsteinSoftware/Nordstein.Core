using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Nordstein.Core.Testing.Certificates;

/// <summary>
/// A certificate authority that exists only in memory, for one test process: it issues short-lived
/// server certificates for the names a test connects to, so a test can run a real TLS server and a
/// client that trusts exactly this authority. No key is ever written anywhere; nothing is checked in.
/// </summary>
/// <remarks>
/// <para>
/// Keys are ECDSA P-256, signatures SHA-256. The authority is valid from one hour before its
/// creation to one day after, may sign only end-entity certificates (path length zero), and keeps
/// its private key to itself: <see cref="Certificate"/> carries the public part only. A client
/// trusts it through a custom root (<c>X509ChainPolicy.CustomTrustStore</c> with
/// <c>X509ChainTrustMode.CustomRootTrust</c>) or, for a process outside .NET, the PEM in
/// <see cref="CertificatePem"/>.
/// </para>
/// <para>
/// "Now" is read from the <see cref="TimeProvider"/> given at construction, at construction and at
/// each issue; pass <see cref="TimeProvider.System"/> when a real TLS stack validates the
/// certificates against the machine's clock.
/// </para>
/// <para>
/// <b>Thread-safety.</b> Issuing is safe from any thread, concurrently (one issue signs at a time).
/// <see cref="Dispose"/> disposes the authority's key; issuing afterwards throws
/// <see cref="ObjectDisposedException"/>. Certificates already issued stay valid and owned by
/// their <see cref="IssuedCertificate"/>.
/// </para>
/// </remarks>
public sealed class ThrowawayCertificateAuthority : IDisposable
{
    private static readonly TimeSpan Backdating = TimeSpan.FromHours(1);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    private readonly Lock gate = new();
    private readonly TimeProvider timeProvider;
    private readonly ECDsa key;
    private readonly X509Certificate2 signer;
    private bool disposed;

    /// <summary>
    /// Creates an authority named <c>CN=Throwaway Test CA</c>.
    /// </summary>
    /// <param name="timeProvider">The source of "now" for every validity period.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is null.</exception>
    public ThrowawayCertificateAuthority(TimeProvider timeProvider)
        : this(timeProvider, "Throwaway Test CA")
    {
    }

    /// <summary>
    /// Creates an authority named <c>CN=</c><paramref name="commonName"/>.
    /// </summary>
    /// <param name="timeProvider">The source of "now" for every validity period.</param>
    /// <param name="commonName">The authority's common name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> or <paramref name="commonName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="commonName"/> is empty or whitespace.</exception>
    public ThrowawayCertificateAuthority(TimeProvider timeProvider, string commonName)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(commonName);
        this.timeProvider = timeProvider;

        key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName("CN=" + commonName), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        DateTimeOffset now = timeProvider.GetUtcNow();
        signer = request.CreateSelfSigned(now - Backdating, now + Lifetime);
        Certificate = X509CertificateLoader.LoadCertificate(signer.RawData);
        CertificatePem = signer.ExportCertificatePem() + "\n";
    }

    /// <summary>
    /// The authority's certificate without its private key: the trust anchor a client adds. Owned
    /// by the authority; do not dispose it.
    /// </summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The authority's certificate as PEM ending in a newline: the content of a CA bundle file.</summary>
    public string CertificatePem { get; }

    /// <summary>
    /// Issues a TLS server certificate for <paramref name="names"/>: valid from one hour before now
    /// to one day after (clamped to the authority's own validity), with every name as a subject
    /// alternative name and the first as the common name.
    /// </summary>
    /// <param name="names">DNS names or IP address literals (<c>localhost</c>, <c>127.0.0.1</c>, <c>::1</c>, <c>service.example.test</c>); at least one.</param>
    /// <returns>The certificate with its private key; the caller disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="names"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="names"/> is empty or holds a null, empty or whitespace name.</exception>
    /// <exception cref="InvalidOperationException">Now is outside the authority's validity: it expired, or the clock was set back before it.</exception>
    /// <exception cref="ObjectDisposedException">The authority was disposed.</exception>
    public IssuedCertificate IssueServerCertificate(params string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (names.Length == 0 || names.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A server certificate needs at least one name, and no name may be blank.", nameof(names));
        }

        var alternativeNames = new SubjectAlternativeNameBuilder();
        foreach (string name in names)
        {
            if (IPAddress.TryParse(name, out IPAddress? address))
            {
                alternativeNames.AddIpAddress(address);
            }
            else
            {
                alternativeNames.AddDnsName(name);
            }
        }

        using ECDsa serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(new X500DistinguishedName("CN=" + names[0]), serverKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(alternativeNames.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication")], critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // A positive, minimally encoded DER integer: the first byte is 0x40 to 0x7F.
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] = (byte)((serial[0] & 0x3f) | 0x40);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                signer, includeKeyIdentifier: true, includeIssuerAndSerial: false));

            DateTimeOffset now = timeProvider.GetUtcNow();
            DateTimeOffset authorityFrom = new(signer.NotBefore.ToUniversalTime());
            DateTimeOffset authorityUntil = new(signer.NotAfter.ToUniversalTime());
            if (now >= authorityUntil)
            {
                throw new InvalidOperationException(
                    $"The throwaway certificate authority expired at {authorityUntil:O}; create a new one.");
            }

            if (now < authorityFrom)
            {
                throw new InvalidOperationException(
                    $"The throwaway certificate authority is not valid before {authorityFrom:O}; the clock was set back past it.");
            }

            DateTimeOffset notBefore = Max(now - Backdating, authorityFrom);
            DateTimeOffset notAfter = Min(now + Lifetime, authorityUntil);
            using X509Certificate2 issued = request.Create(signer, notBefore, notAfter, serial);
            X509Certificate2 withKey = issued.CopyWithPrivateKey(serverKey);
            return new IssuedCertificate(
                withKey,
                withKey.ExportCertificatePem() + "\n",
                serverKey.ExportPkcs8PrivateKeyPem() + "\n",
                CertificatePem);
        }
    }

    /// <summary>
    /// Disposes the authority's key and certificates; idempotent. Certificates already issued are
    /// not affected.
    /// </summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Certificate.Dispose();
            signer.Dispose();
            key.Dispose();
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left > right ? left : right;

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}
