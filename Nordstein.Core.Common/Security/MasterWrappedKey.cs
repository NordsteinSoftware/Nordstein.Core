using Nordstein.Core.Common.Cryptography;

namespace Nordstein.Core.Common.Security;

/// <summary>
/// A value wrapped by <see cref="IMasterKeyRing.Wrap"/>: the version of the master key that wrapped it and the
/// AES-GCM nonce and ciphertext with tag. The caller stores the three fields however it likes; none is secret
/// on its own.
/// </summary>
/// <param name="KeyVersion">The master-key version the value was wrapped under.</param>
/// <param name="Wrapped">The nonce and the ciphertext with its 16-byte tag.</param>
/// <remarks>Record equality compares <paramref name="Wrapped"/> by reference, not by its bytes.</remarks>
public sealed record MasterWrappedKey(int KeyVersion, WrappedKey Wrapped);
