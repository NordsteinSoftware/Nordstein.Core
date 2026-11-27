using System.Security.Cryptography;
using Nordstein.Core.Common.Conversion;

namespace Nordstein.Core.Common.Security.Internal;

/// <summary>
/// Prefixed base32 bearer secrets. See <see cref="ISecretTokenFormat"/>.
/// </summary>
/// <remarks>
/// Stateless: every buffer is local to a call, so one instance serves concurrent callers. Secrets come from
/// <see cref="RandomNumberGenerator"/>, never from the seeded <c>IRandom</c>.
/// </remarks>
internal sealed class SecretTokenFormat : ISecretTokenFormat
{
    private const int MinPrefixLength = 2;

    public string Generate(string prefix)
    {
        RequirePrefix(prefix);
        Span<byte> secret = stackalloc byte[ISecretTokenFormat.SecretBytes];
        try
        {
            RandomNumberGenerator.Fill(secret);
            return prefix + ((ReadOnlySpan<byte>)secret).ToBase32();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public string Compose(string prefix, ReadOnlySpan<byte> secret)
    {
        RequirePrefix(prefix);
        if (secret.Length != ISecretTokenFormat.SecretBytes)
        {
            throw new ArgumentException($"A token secret is {ISecretTokenFormat.SecretBytes} bytes.", nameof(secret));
        }

        return prefix + secret.ToBase32();
    }

    public bool Matches(string prefix, string? candidate)
    {
        RequirePrefix(prefix);
        if (candidate is null
            || candidate.Length != prefix.Length + ISecretTokenFormat.SecretLength
            || !candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> secret = candidate.AsSpan(prefix.Length);
        foreach (char character in secret)
        {
            if (!character.IsBase32())
            {
                return false;
            }
        }

        // 32 bytes are 256 bits in 52 five-bit characters: the last one carries one bit and four zero
        // padding bits, so only 'a' (00000) and 'q' (10000) are canonical. Any other spelling was never minted.
        return secret[^1] is 'a' or 'q';
    }

    public string Last4Of(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return token.Length <= 4 ? token : token[^4..];
    }

    private static void RequirePrefix(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (!IsValidPrefix(prefix))
        {
            throw new ArgumentException(
                $"A token prefix is {MinPrefixLength} to {ISecretTokenFormat.MaxPrefixLength} characters: a lowercase "
                + "ASCII letter, then lowercase ASCII letters, digits and underscores, ending with an underscore.",
                nameof(prefix));
        }
    }

    private static bool IsValidPrefix(string prefix)
    {
        if (prefix.Length is < MinPrefixLength or > ISecretTokenFormat.MaxPrefixLength
            || !char.IsAsciiLetterLower(prefix[0])
            || prefix[^1] != '_')
        {
            return false;
        }

        foreach (char character in prefix)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }
}
