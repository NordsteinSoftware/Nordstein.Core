namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// Thrown when an AEAD stream ends before the chunk carrying the final-block flag was reached: the
/// ciphertext was truncated.
/// </summary>
/// <remarks>
/// This is distinct from <see cref="AeadAuthenticationFailedException"/> precisely because the
/// final-block flag lets the codec tell "authentic but incomplete" from "corrupt": a stream whose
/// last present chunk authenticates as a non-final chunk is missing everything after it, so the
/// bytes already produced must not be treated as a complete message.
/// </remarks>
public sealed class CiphertextTruncatedException : AeadStreamException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CiphertextTruncatedException"/> class.
    /// </summary>
    public CiphertextTruncatedException()
        : base("The AEAD ciphertext was truncated: it ended before a chunk carrying the "
               + "final-block flag.")
    {
    }
}
