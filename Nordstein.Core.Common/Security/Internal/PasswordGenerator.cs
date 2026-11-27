using System.Security.Cryptography;
using Nordstein.Core.Common.Validation;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// CSPRNG base32 passwords. See <see cref="IPasswordGenerator"/>.
/// </summary>
/// <remarks>
/// Stateless apart from the length fixed at construction, so one instance serves concurrent callers. The
/// characters come from <see cref="RandomNumberGenerator.GetString"/>, which draws each one uniformly, never
/// from the seeded <c>IRandom</c>.
/// </remarks>
internal sealed class PasswordGenerator : IPasswordGenerator
{
    // The lowercase RFC 4648 base32 alphabet, as Conversion's ToBase32 writes it.
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    private readonly int length;

    public PasswordGenerator(PasswordPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        // A valid minimum never exceeds the maximum, so neither does the length.
        length = Math.Max(IPasswordGenerator.DefaultLength, options.MinLength);
    }

    public string Generate() => RandomNumberGenerator.GetString(Alphabet, length);
}
