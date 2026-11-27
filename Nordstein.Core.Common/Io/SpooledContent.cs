using System.Security.Cryptography;

namespace Nordstein.Core.Common.Io;

/// <summary>
/// What <see cref="IPrivateFileSpool.SpoolAsync"/> wrote: the content's SHA-256 and its length.
/// </summary>
public sealed class SpooledContent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpooledContent"/> class.
    /// </summary>
    /// <param name="sha256">The SHA-256 of the content, 32 bytes. The array is copied.</param>
    /// <param name="length">The content length in bytes.</param>
    /// <exception cref="ArgumentException"><paramref name="sha256"/> is not 32 bytes long.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative.</exception>
    public SpooledContent(ReadOnlySpan<byte> sha256, long length)
    {
        if (sha256.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException($"A SHA-256 is {SHA256.HashSizeInBytes} bytes long.", nameof(sha256));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Sha256 = sha256.ToArray();
        Length = length;
    }

    /// <summary>
    /// Gets the SHA-256 of the content (32 bytes).
    /// </summary>
    public ReadOnlyMemory<byte> Sha256 { get; }

    /// <summary>
    /// Gets the content length in bytes.
    /// </summary>
    public long Length { get; }
}
