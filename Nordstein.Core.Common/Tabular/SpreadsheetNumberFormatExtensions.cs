using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Nordstein.Core.Common.Tabular;

/// <summary>
/// Renders a spreadsheet cell's number the way a spreadsheet application shows it under one of the
/// built-in number formats (ECMA-376 Part 1, §18.8.30, ids 0–49), and reads the locale a format
/// code names. Pure functions; thread-safe.
/// </summary>
public static class SpreadsheetNumberFormatExtensions
{
    // ECMA-376's last built-in id; custom formats start at 164.
    private const int LastBuiltInId = 49;

    // The serial range a date can take: 1 Jan 1900 (serial 1, 1900 system) to 31 Dec 9999.
    private const double MaxDateSerial = 2_958_465.999_999;

    // The 1904 date system's serial 0 (1 Jan 1904) is the 1900 system's serial 1462.
    private const double Date1904Offset = 1_462;

    private const double SecondsPerDay = 86_400;

    // The spreadsheet's grouping differs from .NET's (CLDR) in these cultures: Excel on a de-AT
    // system groups with '.', CLDR with a no-break space.
    private const string DeAt = "de-AT";
    private const string DeAtGroupSeparator = ".";

    /// <summary>
    /// Formats <paramref name="value"/> as a spreadsheet shows it under the built-in number format
    /// <paramref name="builtInFormatId"/>.
    /// </summary>
    /// <param name="value">The cell's stored number (a date or time as its serial day number).</param>
    /// <param name="builtInFormatId">
    /// The cell's number-format id. Ids 0–49 are the built-in formats: General, integer, decimals,
    /// thousands separators, currency and accounting (in the culture's currency), percent,
    /// scientific, engineering (48), fractions (12, 13), dates (14–17, the culture's short date for
    /// 14 and 22), times (18–21), date-time (22) and elapsed time (45–47). Ids the standard leaves
    /// locale-specific (23–36), text (49) and any id above 49 (a custom format code) render as
    /// General.
    /// </param>
    /// <param name="culture">
    /// The culture for separators, currency, month names and the short date pattern; pass the result
    /// of <see cref="ToSpreadsheetCulture"/> for a spreadsheet's grouping.
    /// </param>
    /// <param name="date1904">
    /// <see langword="true"/> for a workbook in the 1904 date system (serial 0 is 1 January 1904);
    /// else the 1900 system, whose serial 60 is the nonexistent 29 February 1900 and renders as
    /// 28 February 1900.
    /// </param>
    /// <returns>
    /// The displayed text. General shows up to 10 decimals with no grouping, and switches to
    /// scientific notation from 10¹¹ or below 10⁻⁹. Times round to the nearest second (format 47 to
    /// the nearest tenth). A date or time format whose value lies outside 1 January 1900 (1904) to
    /// 31 December 9999 renders as General. A negative value under format 37–40 shows in
    /// parentheses; under a currency format (5–8, 42, 44) it follows the culture's negative
    /// currency pattern. Colors in format codes are ignored.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="builtInFormatId"/> is negative.</exception>
    public static string ToSpreadsheetText(this double value, int builtInFormatId, CultureInfo culture, bool date1904 = false)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentOutOfRangeException.ThrowIfNegative(builtInFormatId);

        return builtInFormatId switch
        {
            1 => value.ToString("0", culture),
            2 => value.ToString("0.00", culture),
            3 => value.ToString("#,##0", culture),
            4 => value.ToString("#,##0.00", culture),
            // The standard currency format rounds half to even; a spreadsheet rounds half away from zero.
            5 or 6 or 42 => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("C0", culture),
            7 or 8 or 44 => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("C2", culture),
            9 => value.ToString("0%", culture),
            10 => value.ToString("0.00%", culture),
            11 => value.ToString("0.00E+00", culture),
            12 => Fraction(value, 9, culture),
            13 => Fraction(value, 99, culture),
            >= 14 and <= 22 or 45 or 47 => DateTime(value, builtInFormatId, culture, date1904),
            37 or 38 => value.ToString("#,##0;(#,##0)", culture),
            39 or 40 => value.ToString("#,##0.00;(#,##0.00)", culture),
            41 => value.ToString("#,##0", culture),
            43 => value.ToString("#,##0.00", culture),
            46 => Elapsed(value, culture),
            48 => Engineering(value, culture),
            _ => General(value, culture),
        };
    }

    /// <summary>
    /// Returns <paramref name="culture"/> with the separators a spreadsheet application uses in it.
    /// </summary>
    /// <param name="culture">The culture.</param>
    /// <returns>
    /// For <c>de-AT</c>, a read-only copy whose number, currency and percent group separator is
    /// <c>.</c> (a spreadsheet shows <c>1.234,50</c> where .NET's CLDR data gives
    /// <c>1 234,50</c> with a no-break space). Any other culture is returned as is. The argument is
    /// never modified.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public static CultureInfo ToSpreadsheetCulture(this CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (!string.Equals(culture.Name, DeAt, StringComparison.OrdinalIgnoreCase))
        {
            return culture;
        }

        var copy = (CultureInfo)culture.Clone();
        copy.NumberFormat.NumberGroupSeparator = DeAtGroupSeparator;
        copy.NumberFormat.CurrencyGroupSeparator = DeAtGroupSeparator;
        copy.NumberFormat.PercentGroupSeparator = DeAtGroupSeparator;
        return CultureInfo.ReadOnly(copy);
    }

    /// <summary>
    /// Reads the culture the first usable locale tag in a number-format code names:
    /// <c>[$-407]</c> or <c>[$€-C07]</c> (a hexadecimal LCID, only its low 16 bits used) or
    /// <c>[$-de-AT]</c> (a culture name).
    /// </summary>
    /// <param name="formatCode">The format code, as a workbook's styles part stores it.</param>
    /// <param name="culture">The culture of the first tag .NET knows; <see langword="null"/> when none.</param>
    /// <returns>
    /// <see langword="true"/> when a tag names a culture. System tags (<c>[$-F800]</c>,
    /// <c>[$-F400]</c>), LCID 0, unknown names and an unclosed bracket are skipped.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="formatCode"/> is null.</exception>
    public static bool TryParseSpreadsheetLocale(this string formatCode, [NotNullWhen(true)] out CultureInfo? culture)
    {
        ArgumentNullException.ThrowIfNull(formatCode);
        int from = 0;
        while ((from = formatCode.IndexOf("[$", from, StringComparison.Ordinal)) >= 0)
        {
            int close = formatCode.IndexOf(']', from);
            if (close < 0)
            {
                break;
            }

            string tag = formatCode[(from + 2)..close];
            from = close + 1;
            int dash = tag.IndexOf('-', StringComparison.Ordinal);
            if (dash >= 0 && CultureOfLocale(tag[(dash + 1)..]) is { } found)
            {
                culture = found;
                return true;
            }
        }

        culture = null;
        return false;
    }

    private static CultureInfo? CultureOfLocale(string locale)
    {
        if (locale.Length is > 0 and <= 8
            && int.TryParse(locale, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int lcid))
        {
            int language = lcid & 0xFFFF;
            // 0xF400 and 0xF800 are the system time and long-date formats, not a culture.
            if (language is not (0 or 0xF400 or 0xF800) && CultureOfLcid(language) is { } byLcid)
            {
                return byLcid;
            }
        }

        try
        {
            return locale.Length == 0 ? null : CultureInfo.GetCultureInfo(locale, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static CultureInfo? CultureOfLcid(int lcid)
    {
        try
        {
            return CultureInfo.GetCultureInfo(lcid);
        }
        catch (Exception exception) when (exception is CultureNotFoundException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string General(double value, CultureInfo culture)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value.ToString(culture);
        }

        double magnitude = Math.Abs(value);
        if (magnitude >= 1e11 || (magnitude > 0 && magnitude < 1e-9))
        {
            return value.ToString("0.#####E+00", culture);
        }

        // The decimal conversion keeps 15 significant digits, so binary noise (0.1 + 0.2) is gone.
        return ((decimal)value).ToString("0.##########", culture);
    }

    private static string Fraction(double value, int maxDenominator, CultureInfo culture)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return General(value, culture);
        }

        double magnitude = Math.Abs(value);
        double whole = Math.Floor(magnitude);
        double rest = magnitude - whole;
        int numerator = 0;
        int denominator = 1;
        double error = rest;
        for (int candidate = 1; candidate <= maxDenominator; candidate++)
        {
            int top = (int)Math.Round(rest * candidate, MidpointRounding.AwayFromZero);
            double candidateError = Math.Abs(rest - ((double)top / candidate));
            if (candidateError < error)
            {
                numerator = top;
                denominator = candidate;
                error = candidateError;
            }
        }

        if (numerator == denominator)
        {
            whole++;
            numerator = 0;
        }

        string sign = value < 0 && (whole > 0 || numerator > 0) ? culture.NumberFormat.NegativeSign : string.Empty;
        string wholeText = whole.ToString("0", culture);
        string fraction = string.Create(culture, $"{numerator}/{denominator}");
        return numerator == 0 ? sign + wholeText
            : whole == 0 ? sign + fraction
            : sign + wholeText + " " + fraction;
    }

    private static string Engineering(double value, CultureInfo culture)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return General(value, culture);
        }

        if (value == 0)
        {
            return 0.0.ToString("0.0", culture) + "E+0";
        }

        int exponent = (int)Math.Floor(Math.Log10(Math.Abs(value)) / 3) * 3;
        double mantissa = Math.Round(value / Math.Pow(10, exponent), 1, MidpointRounding.AwayFromZero);
        if (Math.Abs(mantissa) >= 1000)
        {
            mantissa /= 1000;
            exponent += 3;
        }

        return mantissa.ToString("0.0", culture)
            + "E" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
    }

    private static string Elapsed(double value, CultureInfo culture)
    {
        if (!(value >= 0 && value <= MaxDateSerial))
        {
            return General(value, culture);
        }

        long seconds = (long)Math.Round(value * SecondsPerDay, MidpointRounding.AwayFromZero);
        return string.Create(culture, $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }

    private static string DateTime(double value, int id, CultureInfo culture, bool date1904)
    {
        double serial = date1904 ? value + Date1904Offset : value;
        if (!(serial >= 0 && serial <= MaxDateSerial))
        {
            return General(value, culture);
        }

        // OLE automation dates agree with the 1900 system from serial 61 (1 March 1900); below 60,
        // the 1900 system counts the nonexistent 29 February 1900, so they lag one day.
        double tenths = id == 47 ? 10 : 1;
        double rounded = Math.Round(serial * SecondsPerDay * tenths, MidpointRounding.AwayFromZero) / (SecondsPerDay * tenths);
        double automation = rounded < 60 ? rounded + 1 : rounded;
        System.DateTime moment = System.DateTime.FromOADate(automation);
        string shortDate = culture.DateTimeFormat.ShortDatePattern;
        string pattern = id switch
        {
            14 => shortDate,
            15 => "d-MMM-yy",
            16 => "d-MMM",
            17 => "MMM-yy",
            18 => "h:mm tt",
            19 => "h:mm:ss tt",
            20 => "H:mm",
            21 => "H:mm:ss",
            22 => shortDate + " H:mm",
            45 => "mm:ss",
            _ => "mm:ss'" + culture.NumberFormat.NumberDecimalSeparator + "'f",
        };
        return moment.ToString(pattern, culture);
    }
}
