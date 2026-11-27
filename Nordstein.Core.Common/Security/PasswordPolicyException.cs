using System.Globalization;

namespace Nordstein.Core.Common.Security;

/// <summary>
/// A chosen password broke <see cref="IPasswordPolicy"/>. The message is fixed per <see cref="Rule"/>, names
/// the configured minimum for <see cref="PasswordRule.TooShort"/>, and never holds the password or an
/// identifying value, so it may be shown to the person who chose the password and logged.
/// </summary>
public sealed class PasswordPolicyException : Exception
{
    /// <summary>
    /// A refusal under <paramref name="rule"/> by a policy whose minimum is <paramref name="minimumLength"/>
    /// code points.
    /// </summary>
    /// <param name="rule">The broken rule.</param>
    /// <param name="minimumLength">The refusing policy's <see cref="PasswordPolicyOptions.MinLength"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rule"/> is not a defined rule, or <paramref name="minimumLength"/> is outside 0 to
    /// <see cref="PasswordPolicyOptions.MaxLength"/>.
    /// </exception>
    public PasswordPolicyException(PasswordRule rule, int minimumLength)
        : base(MessageFor(rule, minimumLength))
    {
        Rule = rule;
        MinimumLength = minimumLength;
    }

    /// <summary>
    /// The broken rule.
    /// </summary>
    public PasswordRule Rule { get; }

    /// <summary>
    /// The shortest password the refusing policy accepts, in code points.
    /// </summary>
    public int MinimumLength { get; }

    private static string MessageFor(PasswordRule rule, int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumLength, PasswordPolicyOptions.MaxLength);
        return rule switch
        {
            PasswordRule.TooShort => string.Create(
                CultureInfo.InvariantCulture,
                $"The password must have at least {minimumLength} {(minimumLength == 1 ? "character" : "characters")}."),
            PasswordRule.TooLong => string.Create(
                CultureInfo.InvariantCulture,
                $"The password must have at most {PasswordPolicyOptions.MaxLength} characters."),
            PasswordRule.MatchesIdentity => "The password must not be a value that identifies the account, such as its user name.",
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown password rule."),
        };
    }
}
