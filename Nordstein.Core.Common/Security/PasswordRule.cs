namespace Nordstein.Core.Common.Security;

/// <summary>
/// The rule of <see cref="IPasswordPolicy"/> a refused password broke. The numbers are stable: a caller may
/// store or transmit them.
/// </summary>
public enum PasswordRule
{
    /// <summary>
    /// Fewer code points than <see cref="PasswordPolicyOptions.MinLength"/> after NFKC normalization.
    /// </summary>
    TooShort = 1,

    /// <summary>
    /// More code points than <see cref="PasswordPolicyOptions.MaxLength"/> after NFKC normalization.
    /// </summary>
    TooLong = 2,

    /// <summary>
    /// Equal, after NFKC normalization and invariant lower-casing, to a value that identifies the account
    /// (its user name, for example).
    /// </summary>
    MatchesIdentity = 3,
}
