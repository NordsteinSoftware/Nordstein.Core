namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// Thrown when encrypting or decrypting a message would exceed the per-message chunk-count limit.
/// </summary>
/// <remarks>
/// The chunk counter is bounded so that the nonce, which encodes the counter and the final-block
/// flag, can never wrap and repeat under one message subkey. A payload large enough to hit the
/// limit must be split across separate messages (each with its own salt and subkey). With the
/// default chunk size the limit is far beyond any practical payload.
/// </remarks>
public sealed class ChunkCountExceededException : AeadStreamException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkCountExceededException"/> class.
    /// </summary>
    public ChunkCountExceededException()
        : base("The AEAD message exceeds the maximum number of chunks; split the payload across "
               + "separate messages.")
    {
    }
}
