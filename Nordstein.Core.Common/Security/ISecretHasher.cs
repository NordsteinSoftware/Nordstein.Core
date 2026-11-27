namespace Nordstein.Core.Common.Security;

/// <summary>
/// Deterministic one-way hashing for high-entropy, verify-only secrets.
/// </summary>
/// <remarks>
/// This seam is not suitable for passwords or other human-chosen values; implementations intended
/// for those values must use a salted, deliberately slow password-hashing algorithm. The algorithm
/// and encoding must remain stable once hashes are persisted; changing either invalidates existing
/// equality lookups unless the consumer provides an explicit migration path.
/// <para>
/// <see cref="Module"/> registers a default as a singleton: the upper-case hex SHA-256 of the UTF-8
/// bytes of the value (<see cref="Sha256.HexHash"/>), 64 characters that
/// <see cref="Convert.FromHexString(string)"/> turns into 32 bytes. A product that needs another
/// algorithm or encoding registers its own implementation, either before the module (the default
/// then stays unused) or after it (the last registration wins).
/// </para>
/// </remarks>
public interface ISecretHasher
{
    /// <summary>
    /// Returns a deterministic, stable one-way hash of <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The secret to hash.</param>
    /// <returns>The hash, encoded as the implementation documents.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/> (Core's default).</exception>
    string Hash(string value);
}
