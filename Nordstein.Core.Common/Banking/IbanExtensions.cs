namespace Nordstein.Core.Common.Banking;

/// <summary>
/// Validates and normalizes International Bank Account Numbers (ISO 13616) by their structure and ISO 7064
/// MOD 97-10 check digits.
/// </summary>
/// <remarks>
/// Every member is pure, culture-invariant and thread-safe. The check covers the structure every IBAN shares; it
/// does not consult a per-country registry, so a well-formed IBAN with correct check digits but a length its
/// country does not use is accepted.
/// </remarks>
public static class IbanExtensions
{
    // The shortest IBAN in use (Norway) and the ISO 13616 maximum.
    private const int MinLength = 15;
    private const int MaxLength = 34;

    /// <summary>
    /// Normalizes <paramref name="value"/> to the electronic IBAN form and validates it.
    /// </summary>
    /// <param name="value">The IBAN as printed or typed, with or without grouping whitespace; may be null.</param>
    /// <param name="normalized">
    /// On <see langword="true"/>: the IBAN with every whitespace character (<see cref="char.IsWhiteSpace(char)"/>)
    /// removed and ASCII letters uppercased. On <see langword="false"/>: <see cref="string.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> only when the value without whitespace has 15 to 34 characters, starts with two ASCII letters and two ASCII digits from 02 to 98, continues
    /// with ASCII letters or digits only, and its ISO 7064 MOD 97-10 remainder is 1. Any other character (a hyphen, a
    /// non-ASCII letter or digit, a control character) makes it <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Never throws; pure and thread-safe; independent of the current culture. Check digits 00, 01 and 99 are refused
    /// although one of them can satisfy the remainder: the MOD 97-10 algorithm only ever produces 02 to 98.
    /// </remarks>
    public static bool TryNormalizeIban(this string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null)
        {
            return false;
        }

        Span<char> buffer = stackalloc char[MaxLength];
        var length = 0;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            if (length == MaxLength || !char.IsAsciiLetterOrDigit(ch))
            {
                return false;
            }

            buffer[length++] = char.IsAsciiLetterLower(ch) ? (char)(ch - ('a' - 'A')) : ch;
        }

        if (length < MinLength
            || !char.IsAsciiLetterUpper(buffer[0])
            || !char.IsAsciiLetterUpper(buffer[1])
            || !char.IsAsciiDigit(buffer[2])
            || !char.IsAsciiDigit(buffer[3]))
        {
            return false;
        }

        var checkDigits = ((buffer[2] - '0') * 10) + (buffer[3] - '0');
        if (checkDigits is < 2 or > 98)
        {
            return false;
        }

        // ISO 7064 MOD 97-10 reads the rearranged number: the BBAN, then the country code and check digits.
        var iban = buffer[..length];
        if (Remainder(iban[..4], Remainder(iban[4..], 0)) != 1)
        {
            return false;
        }

        normalized = new string(iban);
        return true;
    }

    /// <summary>
    /// Continues the MOD 97 remainder <paramref name="remainder"/> over <paramref name="digits"/>, each ASCII digit
    /// read as one decimal digit and each uppercase ASCII letter as two (A = 10 … Z = 35). The running remainder
    /// stays below 97, so no intermediate value exceeds 9,735 whatever the length.
    /// </summary>
    private static int Remainder(ReadOnlySpan<char> digits, int remainder)
    {
        foreach (var ch in digits)
        {
            remainder = char.IsAsciiDigit(ch)
                ? ((remainder * 10) + (ch - '0')) % 97
                : ((remainder * 100) + (ch - 'A' + 10)) % 97;
        }

        return remainder;
    }
}
