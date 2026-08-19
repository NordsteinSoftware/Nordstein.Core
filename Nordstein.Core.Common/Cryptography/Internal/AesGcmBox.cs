using System.Security.Cryptography;

namespace Nordstein.Core.Common.Cryptography.Internal;

/// <summary>
/// Thin, single-shot AES-256-GCM helper shared by the stream codec and the key-wrap. Each call
/// constructs and disposes its own <see cref="AesGcm"/>, so the helper is stateless and thread-safe.
/// Records are laid out as <c>ciphertext ‖ tag(16)</c>.
/// </summary>
internal static class AesGcmBox
{
    /// <summary>The AES-GCM nonce length in bytes (fixed by the algorithm).</summary>
    internal const int NonceSize = 12;

    /// <summary>The authentication tag length in bytes (the maximum, and the only value valid on every platform).</summary>
    internal const int TagSize = 16;

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> into <paramref name="destination"/>, which must be
    /// exactly <c>plaintext.Length + <see cref="TagSize"/></c> bytes long, writing the ciphertext
    /// followed by the tag.
    /// </summary>
    internal static void Seal(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        using var aes = new AesGcm(key, TagSize);
        Span<byte> ciphertext = destination[..plaintext.Length];
        Span<byte> tag = destination.Slice(plaintext.Length, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
    }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns <c>ciphertext ‖ tag</c> as a new array.
    /// </summary>
    internal static byte[] Seal(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData)
    {
        byte[] output = new byte[plaintext.Length + TagSize];
        Seal(key, nonce, plaintext, associatedData, output);
        return output;
    }

    /// <summary>
    /// Attempts to authenticate and decrypt <paramref name="record"/> (<c>ciphertext ‖ tag</c>)
    /// into <paramref name="destination"/> (which must be at least <c>record.Length - <see cref="TagSize"/></c>
    /// bytes). Returns <c>true</c> on success, <c>false</c> if the tag does not verify.
    /// </summary>
    internal static bool TryOpen(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> record,
        ReadOnlySpan<byte> associatedData,
        Span<byte> destination)
    {
        int plaintextLength = record.Length - TagSize;
        ReadOnlySpan<byte> ciphertext = record[..plaintextLength];
        ReadOnlySpan<byte> tag = record.Slice(plaintextLength, TagSize);

        using var aes = new AesGcm(key, TagSize);
        try
        {
            aes.Decrypt(nonce, ciphertext, tag, destination[..plaintextLength], associatedData);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
    }
}
