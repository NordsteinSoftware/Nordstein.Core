namespace Nordstein.Core.Common.Security;

/// <summary>
/// A password policy after NIST SP 800-63B: a length from <see cref="PasswordPolicyOptions.MinLength"/> to
/// <see cref="PasswordPolicyOptions.MaxLength"/> code points of the NFKC form, and not equal to a value that
/// identifies the account. No composition rules, no expiry, no common-password list.
/// </summary>
/// <remarks>
/// <para>
/// Normalize first, then check, hash and verify the normalized form, never the raw one: a password typed with
/// full-width digits on one device and ASCII digits on another is the same password. The hasher
/// (<see cref="IPasswordHasher"/>) does not normalize by itself.
/// </para>
/// <para>
/// No message this policy produces holds a password or an identifying value. Stateless and thread-safe;
/// <see cref="Module"/> registers one instance per container, built from the registered
/// <see cref="PasswordPolicyOptions"/>.
/// </para>
/// </remarks>
public interface IPasswordPolicy
{
    /// <summary>
    /// The form every password is checked, hashed and verified in: Unicode normalization form KC. Spaces and
    /// case are kept.
    /// </summary>
    /// <param name="password">The password as entered.</param>
    /// <returns>The NFKC form of <paramref name="password"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="password"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="password"/> holds an unpaired surrogate and so has no normalized form. The message does
    /// not hold the password.
    /// </exception>
    string Normalize(string password);

    /// <summary>
    /// Throws <see cref="PasswordPolicyException"/> for the first broken rule, in the order
    /// <see cref="PasswordRule.TooShort"/>, <see cref="PasswordRule.TooLong"/>,
    /// <see cref="PasswordRule.MatchesIdentity"/>; returns for an acceptable password.
    /// </summary>
    /// <param name="normalized">The password as <see cref="Normalize"/> returned it.</param>
    /// <param name="identities">
    /// The values that identify the account (a user name, an e-mail address, a display name). Each is compared
    /// whole with the password after NFKC normalization and <see cref="string.ToLowerInvariant()"/> on both sides;
    /// a password that only contains one is accepted. An empty collection skips the rule. The collection is
    /// enumerated once.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="normalized"/> or <paramref name="identities"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="normalized"/> is not in NFKC form (it did not come from <see cref="Normalize"/>), or
    /// <paramref name="identities"/> holds <see langword="null"/> or a value with an unpaired surrogate. The
    /// message holds neither the password nor an identifying value.
    /// </exception>
    /// <exception cref="PasswordPolicyException">The password broke a rule.</exception>
    void Check(string normalized, IEnumerable<string> identities);
}
