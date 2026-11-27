using System.Text;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// The NIST SP 800-63B password policy. See <see cref="IPasswordPolicy"/>.
/// </summary>
/// <remarks>
/// Stateless apart from the validated minimum, so one instance serves concurrent callers. The BCL's
/// normalization errors name an index, never the text, but they are replaced with fixed messages anyway so the
/// contract does not depend on a framework's wording.
/// </remarks>
internal sealed class PasswordPolicy : IPasswordPolicy
{
    private readonly int minimumLength;

    public PasswordPolicy(PasswordPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        minimumLength = options.MinLength;
    }

    public string Normalize(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return NormalizeOrThrow(password, nameof(password), "The password");
    }

    public void Check(string normalized, IEnumerable<string> identities)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        ArgumentNullException.ThrowIfNull(identities);
        if (!IsNormalized(normalized))
        {
            throw new ArgumentException(
                "The password is not in Unicode normalization form KC; pass the result of Normalize.",
                nameof(normalized));
        }

        int codePoints = 0;
        foreach (Rune _ in normalized.EnumerateRunes())
        {
            codePoints++;
        }

        if (codePoints < minimumLength)
        {
            throw new PasswordPolicyException(PasswordRule.TooShort, minimumLength);
        }

        if (codePoints > PasswordPolicyOptions.MaxLength)
        {
            throw new PasswordPolicyException(PasswordRule.TooLong, minimumLength);
        }

        string folded = normalized.ToLowerInvariant();
        foreach (string? identity in identities)
        {
            if (identity is null)
            {
                throw new ArgumentException("An identifying value is null.", nameof(identities));
            }

            if (string.Equals(folded, Fold(identity), StringComparison.Ordinal))
            {
                throw new PasswordPolicyException(PasswordRule.MatchesIdentity, minimumLength);
            }
        }
    }

    private static string Fold(string identity)
        => NormalizeOrThrow(identity, "identities", "An identifying value").ToLowerInvariant();

    private static string NormalizeOrThrow(string value, string parameterName, string what)
    {
        try
        {
            return value.Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                $"{what} holds an unpaired surrogate and so has no normalized form.", parameterName, exception);
        }
    }

    private static bool IsNormalized(string value)
    {
        try
        {
            return value.IsNormalized(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            // An unpaired surrogate: no normalized form exists, so this is not one.
            return false;
        }
    }
}
