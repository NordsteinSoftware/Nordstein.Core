using System.Security.Cryptography;

namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// Base type for the failures an <see cref="IAeadStreamCodec"/> raises while authenticating or
/// decrypting a chunked AEAD stream.
/// </summary>
/// <remarks>
/// Derives from <see cref="CryptographicException"/> so callers already catching cryptographic
/// failures observe these too. Prefer catching the concrete subtypes when the distinction between
/// "authentication failed" and "the stream was truncated" matters.
/// </remarks>
public abstract class AeadStreamException : CryptographicException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AeadStreamException"/> class with a
    /// human-readable message.
    /// </summary>
    /// <param name="message">A description of the failure. Never contains plaintext or key material.</param>
    protected AeadStreamException(string message)
        : base(message)
    {
    }
}
