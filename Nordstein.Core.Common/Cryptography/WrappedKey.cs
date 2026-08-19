namespace Nordstein.Core.Common.Cryptography;

/// <summary>
/// A data key wrapped (encrypted) under a key-encryption key by <see cref="IAeadKeyWrap"/>: the
/// per-wrap nonce and the AES-GCM output (ciphertext with its authentication tag appended).
/// </summary>
/// <remarks>
/// The codec imposes no on-disk layout — the caller decides how to store the two fields. Both are
/// required to reverse the wrap; neither is secret on its own.
/// </remarks>
/// <param name="Nonce">The per-wrap nonce that was generated when the key was wrapped.</param>
/// <param name="Ciphertext">The wrapped key bytes: AES-GCM ciphertext with the 16-byte tag appended.</param>
public sealed record WrappedKey(ReadOnlyMemory<byte> Nonce, ReadOnlyMemory<byte> Ciphertext);
