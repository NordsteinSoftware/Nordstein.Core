namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// Core's default <see cref="ISecretHasher"/>: the upper-case hex SHA-256 of the UTF-8 bytes
/// (<see cref="Sha256.HexHash"/>).
/// </summary>
/// <remarks>
/// Key-independent and unsalted, so it is sound only for high-entropy, verify-only secrets (at least
/// 128 random bits, such as a CSPRNG-minted token), never for passwords. Stateless: one instance
/// serves concurrent callers.
/// </remarks>
internal sealed class Sha256SecretHasher : ISecretHasher
{
    /// <summary>
    /// Returns the 64-character upper-case hex SHA-256 of the UTF-8 bytes of <paramref name="value"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public string Hash(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return Sha256.HexHash(value);
    }
}
