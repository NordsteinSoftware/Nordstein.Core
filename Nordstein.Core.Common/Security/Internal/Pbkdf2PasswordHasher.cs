using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// PBKDF2-HMAC-SHA256 password hashing in PHC string form. See <see cref="IPasswordHasher"/>.
/// </summary>
/// <remarks>
/// Stateless: every buffer is local to a call, so one instance serves concurrent callers. Salts come
/// from <see cref="RandomNumberGenerator"/>, never from the seeded <c>IRandom</c>.
/// </remarks>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>
    /// The current iteration count: OWASP's recommendation for PBKDF2-HMAC-SHA256.
    /// </summary>
    internal const int Iterations = 600_000;

    /// <summary>
    /// The current salt length in bytes.
    /// </summary>
    internal const int SaltSize = 16;

    /// <summary>
    /// The current output length in bytes: one SHA-256 block, so no iteration runs twice.
    /// </summary>
    internal const int HashSize = 32;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] passwordBytes = EncodeUtf8(password)
            ?? throw new ArgumentException(
                "The password contains an unpaired surrogate and so has no UTF-8 encoding.",
                nameof(password));

        try
        {
            Span<byte> salt = stackalloc byte[SaltSize];
            Span<byte> hash = stackalloc byte[HashSize];
            RandomNumberGenerator.Fill(salt);
            Rfc2898DeriveBytes.Pbkdf2(passwordBytes, salt, hash, Iterations, HashAlgorithmName.SHA256);
            return Pbkdf2Phc.Format(Iterations, salt, hash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    public PasswordVerification Verify(string password, string encodedHash)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(encodedHash);

        Pbkdf2PhcParts stored = Pbkdf2Phc.Parse(encodedHash);

        byte[]? passwordBytes = EncodeUtf8(password);
        if (passwordBytes is null)
        {
            // Hash refuses such a password, so no hash of it exists to match.
            return PasswordVerification.Failed;
        }

        byte[] derived = new byte[stored.Hash.Length];
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(
                passwordBytes, stored.Salt.Span, derived, stored.Iterations, HashAlgorithmName.SHA256);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }

        if (!CryptographicOperations.FixedTimeEquals(derived, stored.Hash.Span))
        {
            return PasswordVerification.Failed;
        }

        bool isCurrent = stored.Iterations >= Iterations
            && stored.Salt.Length == SaltSize
            && stored.Hash.Length == HashSize;

        return isCurrent ? PasswordVerification.Succeeded : PasswordVerification.SucceededRehashNeeded;
    }

    /// <summary>
    /// Strict UTF-8: <see langword="null"/> for a string with an unpaired surrogate. The lenient
    /// <see cref="Encoding.UTF8"/> would map every unpaired surrogate to U+FFFD, so distinct
    /// passwords would hash alike.
    /// </summary>
    private static byte[]? EncodeUtf8(string password)
    {
        // GetByteCount is exact for well-formed input; for ill-formed input the conversion stops
        // with InvalidData before it could overrun.
        byte[] bytes = new byte[Encoding.UTF8.GetByteCount(password)];
        if (Utf8.FromUtf16(password, bytes, out _, out _, replaceInvalidSequences: false) == OperationStatus.Done)
        {
            return bytes;
        }

        CryptographicOperations.ZeroMemory(bytes);
        return null;
    }
}
