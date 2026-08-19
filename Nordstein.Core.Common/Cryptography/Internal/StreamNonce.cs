using System.Buffers.Binary;

namespace Nordstein.Core.Common.Cryptography.Internal;

/// <summary>
/// Builds the 12-byte per-chunk nonce for the STREAM construction: the high 64 bits are zero and
/// the low 32 bits hold a big-endian word whose top bit is the final-chunk flag and whose remaining
/// 31 bits are the chunk index. Because the message subkey is unique per message, this counter-plus-
/// flag scheme (never a random nonce) keeps every nonce distinct within a message.
/// </summary>
internal static class StreamNonce
{
    /// <summary>
    /// The largest chunk index that fits in the 31 bits left after the final-flag bit
    /// (<c>2^31 - 1</c>). Encrypting or decrypting beyond this is refused.
    /// </summary>
    internal const uint MaxChunkIndex = int.MaxValue;

    private const uint FinalFlag = 0x8000_0000u;

    /// <summary>
    /// Writes the nonce for chunk <paramref name="index"/> into <paramref name="nonce"/> (which must
    /// be <see cref="AesGcmBox.NonceSize"/> bytes), setting the final-chunk flag when
    /// <paramref name="isFinal"/> is <c>true</c>.
    /// </summary>
    internal static void Write(Span<byte> nonce, uint index, bool isFinal)
    {
        nonce.Clear();
        uint word = index | (isFinal ? FinalFlag : 0u);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.Slice(8, 4), word);
    }
}
