namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// Framing parameters for a chunked AEAD stream. The defaults are safe; override them only with a
/// documented reason.
/// </summary>
/// <remarks>
/// The same parameters must be supplied to decrypt a message that were used to encrypt it — the
/// chunk size is part of the wire framing, not stored in the ciphertext.
/// </remarks>
public sealed record ChunkParameters
{
    /// <summary>
    /// The default plaintext chunk size, 64 KiB.
    /// </summary>
    public const int DefaultChunkSize = 64 * 1024;

    private readonly int chunkSize = DefaultChunkSize;

    /// <summary>
    /// Gets the number of plaintext bytes per chunk. Must be greater than zero and is fixed for a
    /// whole message. Defaults to <see cref="DefaultChunkSize"/>.
    /// </summary>
    public int ChunkSize
    {
        get => chunkSize;
        init
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Chunk size must be greater than zero.");
            }

            chunkSize = value;
        }
    }

    /// <summary>
    /// Gets the shared instance carrying the default framing parameters.
    /// </summary>
    public static ChunkParameters Default { get; } = new();
}
