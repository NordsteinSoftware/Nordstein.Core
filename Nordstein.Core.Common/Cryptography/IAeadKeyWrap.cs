namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// Wraps and unwraps a data key under a key-encryption key (KEK) using AES-256-GCM with
/// caller-supplied associated data.
/// </summary>
/// <remarks>
/// <para>
/// AES-GCM is used rather than the standard AES key-wrap (RFC 3394 / 5649) specifically because it
/// binds associated data: passing a slot- or context-identifying value as <c>associatedData</c>
/// means a wrapped key cannot be moved to a different slot and still unwrap. A fresh nonce is
/// generated from the system CSPRNG for every wrap.
/// </para>
/// <para>Implementations are thread-safe and may be shared as singletons.</para>
/// </remarks>
public interface IAeadKeyWrap
{
    /// <summary>
    /// Wraps <paramref name="dataKey"/> under <paramref name="kek"/>, binding
    /// <paramref name="associatedData"/> into the authentication tag.
    /// </summary>
    /// <param name="kek">The 32-byte (256-bit) key-encryption key.</param>
    /// <param name="dataKey">The data key to protect. Any length; never stored in the clear.</param>
    /// <param name="associatedData">
    /// Context that binds the wrapped key to where it belongs (an empty span is allowed). The same
    /// bytes must be supplied to <see cref="Unwrap"/>.
    /// </param>
    /// <returns>The nonce and ciphertext-plus-tag needed to reverse the wrap.</returns>
    /// <exception cref="ArgumentException"><paramref name="kek"/> is not 32 bytes long.</exception>
    WrappedKey Wrap(ReadOnlySpan<byte> kek, ReadOnlySpan<byte> dataKey, ReadOnlySpan<byte> associatedData);

    /// <summary>
    /// Reverses <see cref="Wrap"/>, returning the original data key.
    /// </summary>
    /// <param name="kek">The same key-encryption key used to wrap.</param>
    /// <param name="wrapped">The nonce and ciphertext produced by <see cref="Wrap"/>.</param>
    /// <param name="associatedData">The same associated data used to wrap.</param>
    /// <returns>The unwrapped data key.</returns>
    /// <exception cref="ArgumentException"><paramref name="kek"/> is not 32 bytes long.</exception>
    /// <exception cref="AeadAuthenticationFailedException">
    /// The KEK, associated data, nonce or ciphertext do not match — including an attempt to unwrap
    /// under a different slot's associated data.
    /// </exception>
    byte[] Unwrap(ReadOnlySpan<byte> kek, WrappedKey wrapped, ReadOnlySpan<byte> associatedData);
}
