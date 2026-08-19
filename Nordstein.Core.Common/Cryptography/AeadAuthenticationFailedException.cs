namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// Thrown when a chunk of an AEAD stream fails authentication: tampered ciphertext or tag, a
/// reordered chunk, or a wrong key, salt, info, or associated data.
/// </summary>
/// <remarks>
/// The cause is deliberately indistinguishable — every one of those conditions produces the same
/// exception, because revealing which check failed would leak information to an attacker. Callers
/// must not branch on the cause; treat the ciphertext as unusable and fail closed.
/// </remarks>
public sealed class AeadAuthenticationFailedException : AeadStreamException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AeadAuthenticationFailedException"/> class.
    /// </summary>
    public AeadAuthenticationFailedException()
        : base("AEAD authentication failed: the chunk was tampered with, reordered, or produced "
               + "with a different key, salt, info, or associated data.")
    {
    }
}
