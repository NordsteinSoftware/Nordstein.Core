namespace Nordstein.Core.Common.Security;

/// <summary>
/// Reversible protection for secrets stored outside the process.
/// </summary>
public interface ISecretProtector
{
    /// <summary>
    /// Protects <paramref name="plaintext"/> as an opaque string.
    /// </summary>
    string Protect(string plaintext);

    /// <summary>
    /// Recovers a value produced by <see cref="Protect"/>.
    /// </summary>
    string Unprotect(string ciphertext);
}
