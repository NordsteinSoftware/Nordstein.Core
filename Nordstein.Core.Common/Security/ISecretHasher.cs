namespace Nordstein.Core.Common.Security;

/// <summary>
/// Deterministic one-way hashing for high-entropy, verify-only secrets.
/// </summary>
/// <remarks>
/// This seam is not suitable for passwords or other human-chosen values; implementations intended
/// for those values must use a salted, deliberately slow password-hashing algorithm. The algorithm
/// and encoding must remain stable once hashes are persisted; changing either invalidates existing
/// equality lookups unless the consumer provides an explicit migration path.
/// </remarks>
public interface ISecretHasher
{
    /// <summary>
    /// Returns a deterministic, stable one-way hash of <paramref name="value"/>.
    /// </summary>
    string Hash(string value);
}
