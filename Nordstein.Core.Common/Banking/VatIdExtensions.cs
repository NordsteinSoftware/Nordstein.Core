namespace Nordstein.Core.Common.Banking;

/// <summary>
/// Validates and normalizes value-added-tax identification numbers by their country prefix, the shape that country
/// uses and, where the country publishes one, its check digit.
/// </summary>
/// <remarks>
/// <para>
/// Every member is pure, culture-invariant and thread-safe. Countries with a rule here: AT, BE, CH (the UID with or
/// without its MWST, TVA or IVA suffix), DE, DK, FI, FR, IT, LU, NL, PL, PT and SE. Any other prefix is checked by the
/// shape every VAT id shares: two ASCII letters, then 6 to 14 ASCII letters or digits holding at least one digit.
/// </para>
/// <para>
/// The check proves the number is well-formed, not that it is registered: it does not consult VIES or any registry.
/// </para>
/// </remarks>
public static class VatIdExtensions
{
    // The fewest and most characters after the two-letter prefix for a country without a rule here.
    private const int MinGenericBodyLength = 6;
    private const int MaxGenericBodyLength = 14;

    // The longest input that can still normalize: a Swiss UID with its suffix is 16; separators come on top, so the
    // bound only refuses input no printed VAT id reaches.
    private const int MaxCompactLength = 32;

    private static readonly string[] SwissSuffixes = ["MWST", "TVA", "IVA"];

    /// <summary>
    /// Normalizes <paramref name="value"/> to the compact VAT id and validates it.
    /// </summary>
    /// <param name="value">The VAT id as printed or typed, with or without separators; may be null.</param>
    /// <param name="normalized">
    /// On <see langword="true"/>: the VAT id with every whitespace character, '-', '.' and ',' removed, ASCII letters
    /// uppercased and a Swiss UID's language suffix (MWST, TVA, IVA) dropped, so <c>CHE-123.456.788 TVA</c> and
    /// <c>CHE123456788MWST</c> both become <c>CHE123456788</c>. On <see langword="false"/>: <see cref="string.Empty"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> only when the compact value starts with two ASCII letters, holds only ASCII letters and
    /// digits, matches the shape of its country and, for a country with a check digit rule, passes it. Any other
    /// character (an underscore, a non-ASCII letter, a dash other than '-') makes it <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Never throws. A French number whose two-character key holds a letter has no check over its SIREN and is
    /// accepted by its shape; a Dutch number passes either the 11-test or, as a sole proprietor's number since 2020,
    /// ISO 7064 MOD 97-10 over <c>NL</c> and the number.
    /// </remarks>
    public static bool TryNormalizeVatId(this string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null)
        {
            return false;
        }

        Span<char> buffer = stackalloc char[MaxCompactLength];
        var length = 0;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch is '-' or '.' or ',')
            {
                continue;
            }

            if (length == MaxCompactLength || !char.IsAsciiLetterOrDigit(ch))
            {
                return false;
            }

            buffer[length++] = char.IsAsciiLetterLower(ch) ? (char)(ch - ('a' - 'A')) : ch;
        }

        if (length < 2 || !char.IsAsciiLetterUpper(buffer[0]) || !char.IsAsciiLetterUpper(buffer[1]))
        {
            return false;
        }

        ReadOnlySpan<char> compact = buffer[..length];
        var country = compact[..2];
        var body = compact[2..];
        if (country is "CH")
        {
            body = WithoutSwissSuffix(body);
        }

        var valid = country switch
        {
            "AT" => IsAustrian(body),
            "BE" => IsBelgian(body),
            "CH" => IsSwiss(body),
            "DE" => IsGerman(body),
            "DK" => IsDanish(body),
            "FI" => IsFinnish(body),
            "FR" => IsFrench(body),
            "IT" => IsItalian(body),
            "LU" => IsLuxembourgish(body),
            "NL" => IsDutch(body),
            "PL" => IsPolish(body),
            "PT" => IsPortuguese(body),
            "SE" => IsSwedish(body),
            _ => IsGeneric(body),
        };
        if (!valid)
        {
            return false;
        }

        normalized = string.Concat(country, body);
        return true;
    }

    private static ReadOnlySpan<char> WithoutSwissSuffix(ReadOnlySpan<char> body)
    {
        foreach (var suffix in SwissSuffixes)
        {
            if (body.EndsWith(suffix, StringComparison.Ordinal))
            {
                return body[..^suffix.Length];
            }
        }

        return body;
    }

    private static bool IsGeneric(ReadOnlySpan<char> body)
        => body.Length is >= MinGenericBodyLength and <= MaxGenericBodyLength && body.ContainsAnyInRange('0', '9');

    // ATU + 8 digits; the 7 digits weighted 1, 2, 1, 2, … with each product's digit sum, plus 4, decide the last.
    private static bool IsAustrian(ReadOnlySpan<char> body)
    {
        if (body.Length != 9 || body[0] != 'U' || !AllDigits(body[1..]))
        {
            return false;
        }

        var digits = body[1..];
        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            var digit = Digit(digits[i]);
            sum += i % 2 == 0 ? digit : DigitSum(2 * digit);
        }

        return (10 - ((sum + 4) % 10)) % 10 == Digit(digits[7]);
    }

    // 10 digits starting with 0 or 1; the last two are 97 minus the first eight mod 97.
    private static bool IsBelgian(ReadOnlySpan<char> body)
    {
        if (body.Length != 10 || !AllDigits(body) || body[0] is not ('0' or '1'))
        {
            return false;
        }

        return 97 - (Number(body[..8]) % 97) == Number(body[8..]);
    }

    // CHE + 9 digits; the 8 digits weighted 5, 4, 3, 2, 7, 6, 5, 4 decide the last (MOD 11, a remainder of 10 is no UID).
    private static bool IsSwiss(ReadOnlySpan<char> body)
    {
        if (body.Length != 10 || body[0] != 'E' || !AllDigits(body[1..]))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [5, 4, 3, 2, 7, 6, 5, 4];
        var check = 11 - (WeightedSum(body[1..9], weights) % 11);
        check = check == 11 ? 0 : check;
        return check != 10 && check == Digit(body[9]);
    }

    // 9 digits; ISO 7064 MOD 11,10 over the first eight decides the last.
    private static bool IsGerman(ReadOnlySpan<char> body)
    {
        if (body.Length != 9 || !AllDigits(body))
        {
            return false;
        }

        var product = 10;
        for (var i = 0; i < 8; i++)
        {
            var sum = (Digit(body[i]) + product) % 10;
            product = 2 * (sum == 0 ? 10 : sum) % 11;
        }

        var check = 11 - product;
        return (check == 10 ? 0 : check) == Digit(body[8]);
    }

    // 8 digits weighted 2, 7, 6, 5, 4, 3, 2, 1; the sum is a multiple of 11.
    private static bool IsDanish(ReadOnlySpan<char> body)
    {
        ReadOnlySpan<int> weights = [2, 7, 6, 5, 4, 3, 2, 1];
        return body.Length == 8 && AllDigits(body) && WeightedSum(body, weights) % 11 == 0;
    }

    // 8 digits; the 7 weighted 7, 9, 10, 5, 8, 4, 2 decide the last (MOD 11, a remainder of 1 is no number).
    private static bool IsFinnish(ReadOnlySpan<char> body)
    {
        if (body.Length != 8 || !AllDigits(body))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [7, 9, 10, 5, 8, 4, 2];
        var remainder = WeightedSum(body[..7], weights) % 11;
        return remainder != 1 && (remainder == 0 ? 0 : 11 - remainder) == Digit(body[7]);
    }

    // A two-character key and the 9-digit SIREN; a numeric key is (12 + 3 × (SIREN mod 97)) mod 97.
    private static bool IsFrench(ReadOnlySpan<char> body)
    {
        if (body.Length != 11 || !AllDigits(body[2..]))
        {
            return false;
        }

        var key = body[..2];
        if (!AllDigits(key))
        {
            return true;
        }

        return (12 + (3 * (Number(body[2..]) % 97))) % 97 == Number(key);
    }

    // 11 digits; the Luhn sum of the first ten decides the last.
    private static bool IsItalian(ReadOnlySpan<char> body)
    {
        if (body.Length != 11 || !AllDigits(body))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var digit = Digit(body[i]);
            sum += i % 2 == 0 ? digit : DigitSum(2 * digit);
        }

        return (10 - (sum % 10)) % 10 == Digit(body[10]);
    }

    // 8 digits; the last two are the first six mod 89.
    private static bool IsLuxembourgish(ReadOnlySpan<char> body)
        => body.Length == 8 && AllDigits(body) && Number(body[..6]) % 89 == Number(body[6..]);

    // 9 digits, 'B', 2 digits. The 11-test over the 9 digits, or MOD 97-10 over "NL" and the whole body.
    private static bool IsDutch(ReadOnlySpan<char> body)
    {
        if (body.Length != 12 || !AllDigits(body[..9]) || body[9] != 'B' || !AllDigits(body[10..]))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [9, 8, 7, 6, 5, 4, 3, 2];
        var remainder = WeightedSum(body[..8], weights) % 11;
        if (remainder != 10 && remainder == Digit(body[8]))
        {
            return true;
        }

        // MOD 97-10 reads N = 23, L = 21, then the body with B = 11.
        var mod97 = Remainder97("NL", 0);
        return Remainder97(body, mod97) == 1;
    }

    // 10 digits; the 9 weighted 6, 5, 7, 2, 3, 4, 5, 6, 7 decide the last (MOD 11, a remainder of 10 is no number).
    private static bool IsPolish(ReadOnlySpan<char> body)
    {
        if (body.Length != 10 || !AllDigits(body))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [6, 5, 7, 2, 3, 4, 5, 6, 7];
        var remainder = WeightedSum(body[..9], weights) % 11;
        return remainder != 10 && remainder == Digit(body[9]);
    }

    // 9 digits; the 8 weighted 9 down to 2 decide the last (11 minus the remainder, 10 and 11 as 0).
    private static bool IsPortuguese(ReadOnlySpan<char> body)
    {
        if (body.Length != 9 || !AllDigits(body))
        {
            return false;
        }

        ReadOnlySpan<int> weights = [9, 8, 7, 6, 5, 4, 3, 2];
        var check = 11 - (WeightedSum(body[..8], weights) % 11);
        return (check >= 10 ? 0 : check) == Digit(body[8]);
    }

    // 12 digits ending in 01; the first ten pass the Luhn check.
    private static bool IsSwedish(ReadOnlySpan<char> body)
    {
        if (body.Length != 12 || !AllDigits(body) || body[10..] is not "01")
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var digit = Digit(body[i]);
            sum += i % 2 == 0 ? DigitSum(2 * digit) : digit;
        }

        return sum % 10 == 0;
    }

    private static bool AllDigits(ReadOnlySpan<char> text) => !text.ContainsAnyExceptInRange('0', '9');

    private static int Digit(char ch) => ch - '0';

    private static int DigitSum(int number) => (number / 10) + (number % 10);

    // At most 10 digits are read, so the value stays far below long.MaxValue.
    private static long Number(ReadOnlySpan<char> digits)
    {
        long number = 0;
        foreach (var ch in digits)
        {
            number = (number * 10) + Digit(ch);
        }

        return number;
    }

    private static int WeightedSum(ReadOnlySpan<char> digits, ReadOnlySpan<int> weights)
    {
        var sum = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            sum += Digit(digits[i]) * weights[i];
        }

        return sum;
    }

    // Continues a MOD 97 remainder over digits (one decimal digit each) and uppercase letters (A = 10 … Z = 35).
    private static int Remainder97(ReadOnlySpan<char> text, int remainder)
    {
        foreach (var ch in text)
        {
            remainder = char.IsAsciiDigit(ch)
                ? ((remainder * 10) + Digit(ch)) % 97
                : ((remainder * 100) + (ch - 'A' + 10)) % 97;
        }

        return remainder;
    }
}
