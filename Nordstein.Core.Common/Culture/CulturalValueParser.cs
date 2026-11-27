using System.Globalization;

namespace Nordstein.Core.Common.Culture;

/// <summary>
/// Parses a number or a date exactly as a text wrote it, under a format the caller declares (the
/// decimal and grouping separators, the date order), and refuses anything that format does not
/// explain. It never guesses: a value written with the other culture's separators, a malformed
/// grouping or an impossible date is refused, so a successful parse confirms the declared format.
/// </summary>
/// <remarks>
/// <para>
/// Numbers: an optional sign (<c>-</c>, <c>+</c> or U+2212 minus), ASCII digits, the grouping
/// separator only between groups (the first group of 1 to 3 digits, every later one of exactly 3),
/// and at most one decimal separator followed by at least one digit. A grouping separator that is a
/// Unicode space separator (category <c>Zs</c>, such as a space, a no-break space or a thin space)
/// accepts any space separator in its place. One currency mark may stand before or after the
/// number, with or without whitespace: a currency symbol (category <c>Sc</c>, such as <c>€</c> or
/// <c>$</c>) or an ISO 4217 style code of three uppercase ASCII letters (<c>EUR</c>). The sign may
/// stand before the currency mark or directly before the digits. Surrounding whitespace is ignored.
/// Exponents, parentheses, trailing signs, words and ranges are refused.
/// </para>
/// <para>
/// Dates: an ISO 8601 calendar date <c>yyyy-MM-dd</c> is accepted under every order. Otherwise the
/// date is three ASCII-digit parts in the declared order, joined by one separator used twice
/// (<c>.</c>, <c>/</c> or <c>-</c>), each separator optionally followed by whitespace. Day and month
/// have 1 or 2 digits; the year has 4 digits, or 2 in the day-first and month-first orders, read
/// with the fixed pivot 2049 (<c>00</c>–<c>49</c> are 2000–2049, <c>50</c>–<c>99</c> are 1950–1999,
/// never the machine's calendar setting). Month names, weekdays and times are refused.
/// </para>
/// <para>
/// Pure and thread-safe: no state, no culture lookup, the same result on every machine.
/// </para>
/// </remarks>
public static class CulturalValueParser
{
    private const int GroupDigits = 3;
    private const int MaxFirstGroupDigits = 3;
    private const int TwoDigitYearPivot = 49;

    /// <summary>
    /// Parses <paramref name="raw"/> as a number written with the given separators, ignoring a
    /// currency mark.
    /// </summary>
    /// <param name="raw">The text as written; <see langword="null"/>, empty or whitespace is refused.</param>
    /// <param name="decimalSeparator">One character, not a letter, digit, whitespace, sign or currency symbol.</param>
    /// <param name="groupingSeparator">
    /// One character, not a letter, digit, sign or currency symbol, other than
    /// <paramref name="decimalSeparator"/>; whitespace is allowed.
    /// </param>
    /// <param name="value">The parsed value, or zero when the method returns <see langword="false"/>.</param>
    /// <returns>Whether the whole text is one number in the declared format.</returns>
    /// <exception cref="ArgumentNullException">A separator is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A separator is not exactly one allowed character, or both are the same.</exception>
    public static bool TryParseDecimal(string? raw, string decimalSeparator, string groupingSeparator, out decimal value)
        => TryParseDecimal(raw, decimalSeparator, groupingSeparator, out value, out _);

    /// <summary>
    /// Parses <paramref name="raw"/> as a number written with the given separators and reports the
    /// currency mark it carried.
    /// </summary>
    /// <param name="raw">The text as written; <see langword="null"/>, empty or whitespace is refused.</param>
    /// <param name="decimalSeparator">One character, not a letter, digit, whitespace, sign or currency symbol.</param>
    /// <param name="groupingSeparator">
    /// One character, not a letter, digit, sign or currency symbol, other than
    /// <paramref name="decimalSeparator"/>; whitespace is allowed.
    /// </param>
    /// <param name="value">The parsed value, or zero when the method returns <see langword="false"/>.</param>
    /// <param name="currency">
    /// The currency mark exactly as written (a symbol such as <c>€</c> or a code such as <c>EUR</c>),
    /// or <see langword="null"/> when the text has none or the method returns <see langword="false"/>.
    /// </param>
    /// <returns>Whether the whole text is one number in the declared format.</returns>
    /// <exception cref="ArgumentNullException">A separator is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A separator is not exactly one allowed character, or both are the same.</exception>
    public static bool TryParseDecimal(
        string? raw, string decimalSeparator, string groupingSeparator, out decimal value, out string? currency)
    {
        char decimalMark = Separator(decimalSeparator, nameof(decimalSeparator), allowWhiteSpace: false);
        char groupingMark = Separator(groupingSeparator, nameof(groupingSeparator), allowWhiteSpace: true);
        if (decimalMark == groupingMark)
        {
            throw new ArgumentException("The grouping separator equals the decimal separator.", nameof(groupingSeparator));
        }

        value = 0m;
        currency = null;
        string text = raw?.Trim() ?? string.Empty;
        int position = 0;

        bool negative = false;
        bool signed = TrySign(text, ref position, ref negative);
        string? leading = TryCurrency(text, ref position);
        if (leading is not null)
        {
            SkipWhiteSpace(text, ref position);
            if (!signed)
            {
                signed = TrySign(text, ref position, ref negative);
            }
        }

        if (!TryNumber(text, ref position, decimalMark, groupingMark, out string digits))
        {
            return false;
        }

        SkipWhiteSpace(text, ref position);
        string? trailing = TryCurrency(text, ref position);
        if (position != text.Length || (leading is not null && trailing is not null))
        {
            return false;
        }

        if (!decimal.TryParse(
                (negative ? "-" : string.Empty) + digits,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out decimal parsed))
        {
            return false;
        }

        value = parsed;
        currency = leading ?? trailing;
        return true;
    }

    /// <summary>
    /// Parses <paramref name="raw"/> as a numeric date in the declared order, or as an ISO 8601
    /// calendar date (<c>yyyy-MM-dd</c>).
    /// </summary>
    /// <param name="raw">The text as written; <see langword="null"/>, empty or whitespace is refused.</param>
    /// <param name="order">The order the text writes day, month and year in.</param>
    /// <param name="value">The parsed date, or the default when the method returns <see langword="false"/>.</param>
    /// <returns>Whether the whole text is one existing date in the declared format.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="order"/> is not a defined <see cref="DateOrder"/>.</exception>
    public static bool TryParseDate(string? raw, DateOrder order, out DateOnly value)
    {
        if (!Enum.IsDefined(order))
        {
            throw new ArgumentOutOfRangeException(nameof(order), order, "Not a date order.");
        }

        value = default;
        string text = raw?.Trim() ?? string.Empty;
        if (IsIsoDate(text))
        {
            return TryDate(Number(text, 0, 4), Number(text, 5, 2), Number(text, 8, 2), out value);
        }

        if (!TrySplitDate(text, out string first, out string second, out string third))
        {
            return false;
        }

        (string year, string month, string day) = order switch
        {
            DateOrder.Dmy => (third, second, first),
            DateOrder.Mdy => (third, first, second),
            _ => (first, second, third),
        };
        bool twoDigitYear = order != DateOrder.Ymd && year.Length == 2;
        if (!(year.Length == 4 || twoDigitYear) || month.Length > 2 || day.Length > 2)
        {
            return false;
        }

        int years = Number(year, 0, year.Length);
        if (twoDigitYear)
        {
            years += years <= TwoDigitYearPivot ? 2000 : 1900;
        }

        return TryDate(years, Number(month, 0, month.Length), Number(day, 0, day.Length), out value);
    }

    private static char Separator(string separator, string parameter, bool allowWhiteSpace)
    {
        ArgumentNullException.ThrowIfNull(separator, parameter);
        if (separator.Length != 1)
        {
            throw new ArgumentException("A separator is exactly one character.", parameter);
        }

        char mark = separator[0];
        if (char.IsLetterOrDigit(mark) || IsSign(mark) || char.GetUnicodeCategory(mark) == UnicodeCategory.CurrencySymbol
            || (!allowWhiteSpace && char.IsWhiteSpace(mark)))
        {
            throw new ArgumentException("A separator is not a letter, digit, sign, currency symbol or (for decimals) whitespace.", parameter);
        }

        return mark;
    }

    private static bool IsSign(char character) => character is '-' or '+' or '\u2212';

    private static bool TrySign(string text, ref int position, ref bool negative)
    {
        if (position < text.Length && IsSign(text[position]))
        {
            negative = text[position] != '+';
            position++;
            return true;
        }

        return false;
    }

    // A currency symbol (one character of category Sc) or three uppercase ASCII letters not followed
    // by another letter.
    private static string? TryCurrency(string text, ref int position)
    {
        if (position < text.Length && char.GetUnicodeCategory(text[position]) == UnicodeCategory.CurrencySymbol)
        {
            return text[position++].ToString();
        }

        int end = position + 3;
        if (end <= text.Length
            && char.IsAsciiLetterUpper(text[position]) && char.IsAsciiLetterUpper(text[position + 1]) && char.IsAsciiLetterUpper(text[position + 2])
            && (end == text.Length || !char.IsLetter(text[end])))
        {
            string code = text[position..end];
            position = end;
            return code;
        }

        return null;
    }

    private static void SkipWhiteSpace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }

    // Reads digits, grouped and with an optional fraction, and returns them in invariant form
    // ("1234.50"). A grouping mark counts only before exactly three digits; anything after it stays
    // unread, so the caller refuses the text.
    private static bool TryNumber(string text, ref int position, char decimalMark, char groupingMark, out string digits)
    {
        digits = string.Empty;
        int start = position;
        int index = position;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        int firstGroup = index - start;
        if (firstGroup == 0)
        {
            return false;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        builder.Append(text, start, firstGroup);
        bool grouped = false;
        while (index < text.Length && IsGrouping(text[index], groupingMark) && IsGroup(text, index + 1))
        {
            builder.Append(text, index + 1, GroupDigits);
            index += 1 + GroupDigits;
            grouped = true;
        }

        if (grouped && firstGroup > MaxFirstGroupDigits)
        {
            return false;
        }

        if (index < text.Length && text[index] == decimalMark)
        {
            int fractionStart = index + 1;
            int fractionEnd = fractionStart;
            while (fractionEnd < text.Length && char.IsAsciiDigit(text[fractionEnd]))
            {
                fractionEnd++;
            }

            if (fractionEnd == fractionStart)
            {
                return false;
            }

            builder.Append('.').Append(text, fractionStart, fractionEnd - fractionStart);
            index = fractionEnd;
        }

        position = index;
        digits = builder.ToString();
        return true;
    }

    private static bool IsGrouping(char character, char groupingMark)
        => character == groupingMark
           || (char.GetUnicodeCategory(groupingMark) == UnicodeCategory.SpaceSeparator
               && char.GetUnicodeCategory(character) == UnicodeCategory.SpaceSeparator);

    // Exactly three digits at start, not followed by a fourth.
    private static bool IsGroup(string text, int start)
        => start + GroupDigits <= text.Length
           && char.IsAsciiDigit(text[start]) && char.IsAsciiDigit(text[start + 1]) && char.IsAsciiDigit(text[start + 2])
           && (start + GroupDigits == text.Length || !char.IsAsciiDigit(text[start + GroupDigits]));

    private static bool IsIsoDate(string text)
        => text.Length == 10 && text[4] == '-' && text[7] == '-'
           && AllDigits(text, 0, 4) && AllDigits(text, 5, 2) && AllDigits(text, 8, 2);

    // Three runs of ASCII digits joined by the same separator (., / or -), each separator optionally
    // followed by whitespace; nothing else.
    private static bool TrySplitDate(string text, out string first, out string second, out string third)
    {
        first = second = third = string.Empty;
        string[] parts = new string[3];
        char separator = '\0';
        int position = 0;
        for (int part = 0; part < parts.Length; part++)
        {
            int start = position;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
            }

            if (position == start)
            {
                return false;
            }

            parts[part] = text[start..position];
            if (part == parts.Length - 1)
            {
                break;
            }

            if (position == text.Length || text[position] is not ('.' or '/' or '-') || (separator != '\0' && text[position] != separator))
            {
                return false;
            }

            separator = text[position++];
            SkipWhiteSpace(text, ref position);
        }

        if (position != text.Length)
        {
            return false;
        }

        (first, second, third) = (parts[0], parts[1], parts[2]);
        return true;
    }

    private static bool AllDigits(string text, int start, int length)
    {
        for (int index = start; index < start + length; index++)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    // At most four ASCII digits, so the value never overflows.
    private static int Number(string text, int start, int length)
    {
        int number = 0;
        for (int index = start; index < start + length; index++)
        {
            number = (number * 10) + (text[index] - '0');
        }

        return number;
    }

    private static bool TryDate(int year, int month, int day, out DateOnly value)
    {
        value = default;
        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        value = new DateOnly(year, month, day);
        return true;
    }
}
