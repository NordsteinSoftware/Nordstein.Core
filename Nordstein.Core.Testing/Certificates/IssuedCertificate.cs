using System.Security.Cryptography.X509Certificates;

namespace Nordstein.Core.Testing.Certificates;

/// <summary>
/// A certificate a <see cref="ThrowawayCertificateAuthority"/> issued, with its private key, in the
/// two forms test servers take: an <see cref="X509Certificate2"/> for a .NET TLS server and PEM
/// text for a server configured with files (a container, a proxy).
/// </summary>
/// <remarks>
/// Owns <see cref="Certificate"/>: dispose this object, not the certificate. The PEM strings stay
/// readable after disposal. Immutable apart from disposal; safe to read from any thread.
/// </remarks>
public sealed class IssuedCertificate : IDisposable
{
    internal IssuedCertificate(X509Certificate2 certificate, string certificatePem, string privateKeyPem, string issuerPem)
    {
        Certificate = certificate;
        CertificatePem = certificatePem;
        PrivateKeyPem = privateKeyPem;
        ChainPem = certificatePem + issuerPem;
    }

    /// <summary>The certificate, with its private key, ready for <c>SslServerAuthenticationOptions.ServerCertificate</c>.</summary>
    /// <remarks>
    /// The key is ephemeral (never persisted). On Windows, SChannel cannot serve TLS with an
    /// ephemeral key; there, load <see cref="CertificatePem"/> and <see cref="PrivateKeyPem"/>
    /// through a PKCS#12 export instead.
    /// </remarks>
    public X509Certificate2 Certificate { get; }

    /// <summary>The certificate alone, as PEM ending in a newline.</summary>
    public string CertificatePem { get; }

    /// <summary>The certificate's private key, as unencrypted PKCS#8 PEM ending in a newline.</summary>
    public string PrivateKeyPem { get; }

    /// <summary>The certificate followed by the issuing authority's certificate, as PEM: the chain file a server presents.</summary>
    public string ChainPem { get; }

    /// <summary>Disposes <see cref="Certificate"/>; idempotent.</summary>
    public void Dispose() => Certificate.Dispose();
}
