namespace Nordstein.Core.Common.Security;

/// <summary>
/// Salted, deliberately slow one-way hashing for passwords and other human-chosen secrets, with a
/// signal that tells the caller when a stored hash should be replaced.
/// </summary>
/// <remarks>
/// <para>
/// A hash is a self-describing ASCII string in the PHC string format: it names its algorithm and
/// carries its parameters and salt, so a hash keeps verifying after the current parameters change,
/// and <see cref="Verify"/> reports <see cref="PasswordVerification.SucceededRehashNeeded"/> for it.
/// The current parameters are PBKDF2-HMAC-SHA256 with 600,000 iterations, a fresh 16-byte salt from
/// the system CSPRNG and a 32-byte output:
/// <c>$pbkdf2-sha256$i=600000$&lt;salt&gt;$&lt;hash&gt;</c>, salt and output in standard base64
/// without padding.
/// </para>
/// <para>
/// The password is hashed as the UTF-8 bytes of the string exactly as given: no trimming, no case
/// folding, no Unicode normalization, no length limit and no truncation. A caller that wants
/// normalization applies the same one before both <see cref="Hash"/> and <see cref="Verify"/>. The
/// hasher enforces no password policy; the empty string is hashed like any other value.
/// </para>
/// <para>
/// Both methods are CPU-bound and synchronous by design — hundreds of thousands of HMAC rounds per
/// call at the current parameters — and cannot be cancelled once started. An endpoint that calls
/// them on behalf of an unauthenticated client must rate-limit that client and bound the size of
/// the password it accepts.
/// </para>
/// <para>
/// Implementations are stateless and thread-safe; one instance may be shared as a singleton. An
/// encoded hash cannot be reversed, but it enables an offline guessing attack: store it like a
/// credential and never log it.
/// </para>
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes <paramref name="password"/> under the current parameters with a fresh random salt.
    /// </summary>
    /// <param name="password">The password. The empty string is allowed.</param>
    /// <returns>
    /// A PHC string for <paramref name="password"/>. Two calls with the same password return different
    /// strings, because each draws its own salt.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="password"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="password"/> contains an unpaired surrogate and so has no UTF-8 encoding. The
    /// message does not contain the password.
    /// </exception>
    string Hash(string password);

    /// <summary>
    /// Checks <paramref name="password"/> against <paramref name="encodedHash"/>, a value returned by
    /// <see cref="Hash"/>. The comparison takes the same time however much of the hash matches.
    /// </summary>
    /// <param name="password">The candidate password.</param>
    /// <param name="encodedHash">The stored PHC string.</param>
    /// <returns>
    /// <see cref="PasswordVerification.Failed"/> when the password does not match, and without
    /// running the derivation when it contains an unpaired surrogate, since <see cref="Hash"/> never
    /// produces a hash for such a password.
    /// <see cref="PasswordVerification.Succeeded"/> when it matches and the hash uses the current
    /// parameters. <see cref="PasswordVerification.SucceededRehashNeeded"/> when it matches but the hash
    /// has fewer iterations than the current default, or a salt or output length other than the
    /// current one; a hash with more iterations than the default is not downgraded.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="password"/> or <paramref name="encodedHash"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="FormatException">
    /// <paramref name="encodedHash"/> is not a hash this hasher accepts. A PBKDF2 hash is accepted with
    /// exactly the segments <c>$pbkdf2-sha256$i=&lt;n&gt;$&lt;salt&gt;$&lt;hash&gt;</c> and nothing
    /// around them; <c>n</c> a decimal from 1 to 10,000,000 without sign or leading zero; the salt
    /// 8 to 64 bytes and the output 16 to 64 bytes, each in canonical standard base64 without
    /// padding. The bounds keep one stored value from costing minutes of CPU per check, and a short
    /// output from matching a wrong password by chance. The stored value is checked before the
    /// password, so this is thrown whatever the password is. The message contains neither argument.
    /// </exception>
    PasswordVerification Verify(string password, string encodedHash);
}
