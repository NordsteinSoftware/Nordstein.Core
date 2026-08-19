namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// Encrypts and decrypts arbitrarily large streams as a sequence of authenticated chunks
/// (AES-256-GCM in a STREAM-style construction), so neither direction needs to buffer the whole
/// payload in memory.
/// </summary>
/// <remarks>
/// <para>
/// Each message derives a fresh per-message subkey with HKDF-SHA256 from the caller's
/// <c>key</c>, <c>salt</c> and <c>info</c>, then encrypts the plaintext in fixed-size chunks under
/// that subkey. Because the subkey is unique per message (a random <c>salt</c>), the per-chunk
/// nonce is a counter plus a final-block flag rather than a random value — reordering, duplicating
/// or truncating chunks all become authentication failures.
/// </para>
/// <para>
/// This codec owns no on-disk header: any framing a caller needs (a magic marker, a format
/// version, the salt itself, product identifiers) is written and read by the caller and passed in
/// as <c>associatedData</c>, which is bound into every chunk's tag. The codec therefore knows
/// nothing about the caller's format — it authenticates whatever opaque bytes it is given.
/// </para>
/// <para>Implementations are thread-safe and may be shared as singletons.</para>
/// </remarks>
public interface IAeadStreamCodec
{
    /// <summary>
    /// The salt length, in bytes, that <see cref="NewSalt"/> produces and that
    /// <see cref="EncryptAsync"/> expects (16 bytes / 128 bits).
    /// </summary>
    int SaltSizeInBytes { get; }

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> into <paramref name="destination"/> as a sequence of
    /// AES-256-GCM chunks under a per-message subkey derived (HKDF-SHA256) from
    /// <paramref name="key"/>, <paramref name="salt"/> and <paramref name="info"/>. Every chunk
    /// additionally binds <paramref name="associatedData"/> into its authentication tag.
    /// </summary>
    /// <param name="plaintext">The stream to read plaintext from. Read to its end; not disposed.</param>
    /// <param name="destination">The stream the chunk records are written to. Not disposed.</param>
    /// <param name="key">The root key material for HKDF. Any length; never stored.</param>
    /// <param name="salt">
    /// A per-message salt of length <see cref="SaltSizeInBytes"/>, from a cryptographic RNG (for
    /// example <see cref="NewSalt"/>). Must be produced before the caller seals it into its own
    /// header, so it is a required input rather than generated here.
    /// </param>
    /// <param name="info">Context bytes mixed into HKDF (an empty span is allowed).</param>
    /// <param name="associatedData">
    /// Opaque associated data bound into every chunk's tag (an empty span is allowed). The same
    /// bytes must be supplied to decrypt.
    /// </param>
    /// <param name="parameters">Chunk framing; <see cref="ChunkParameters.Default"/> when <c>null</c>.</param>
    /// <param name="cancellationToken">Observed between chunks.</param>
    /// <returns>A task that completes when the whole plaintext has been encrypted and written.</returns>
    /// <exception cref="ArgumentException"><paramref name="salt"/> is not <see cref="SaltSizeInBytes"/> long.</exception>
    /// <exception cref="ChunkCountExceededException">The payload exceeds the per-message chunk limit.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    Task EncryptAsync(
        Stream plaintext,
        Stream destination,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> salt,
        ReadOnlyMemory<byte> info,
        ReadOnlyMemory<byte> associatedData,
        ChunkParameters? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a forward-only, read-only stream that authenticates and decrypts the chunk records
    /// in <paramref name="ciphertext"/> on demand, in O(chunk-size) memory.
    /// </summary>
    /// <param name="ciphertext">The stream of chunk records to read. Not disposed by the returned stream.</param>
    /// <param name="key">The same root key used to encrypt.</param>
    /// <param name="salt">The same salt used to encrypt (read back from the caller's own framing).</param>
    /// <param name="info">The same info used to encrypt.</param>
    /// <param name="associatedData">The same associated data used to encrypt.</param>
    /// <param name="parameters">The same chunk framing used to encrypt.</param>
    /// <returns>
    /// A read-only stream. Reading past a tampered, reordered or wrong-key chunk throws
    /// <see cref="AeadAuthenticationFailedException"/>; reaching the end of input before the
    /// final-flagged chunk throws <see cref="CiphertextTruncatedException"/>. Disposing the
    /// returned stream does not dispose <paramref name="ciphertext"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="salt"/> is not <see cref="SaltSizeInBytes"/> long.</exception>
    Stream OpenRead(
        Stream ciphertext,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> salt,
        ReadOnlyMemory<byte> info,
        ReadOnlyMemory<byte> associatedData,
        ChunkParameters? parameters = null);

    /// <summary>
    /// Produces a cryptographically random salt of length <see cref="SaltSizeInBytes"/> for
    /// <see cref="EncryptAsync"/>, from the system CSPRNG (never a seeded generator).
    /// </summary>
    /// <returns>A new salt.</returns>
    byte[] NewSalt();
}
