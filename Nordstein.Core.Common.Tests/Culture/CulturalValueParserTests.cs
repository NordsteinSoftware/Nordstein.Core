using AwesomeAssertions;
using Nordstein.Core.Common.Culture;

namespace Nordstein.Core.Common.Tests.Culture;

/// <summary>
/// The culture-aware parser of numbers and dates as a document writes them: separators and date
/// order come from the caller, currency marks and surrounding whitespace are tolerated, and anything
/// the declared format does not explain is refused rather than guessed.
/// </summary>
[TestClass]
public sealed class CulturalValueParserTests
{
    [TestMethod]
    [DataRow("1.234,50", "1234.50")]
    [DataRow("1234,50", "1234.50")]
    [DataRow("0,5", "0.5")]
    [DataRow("12", "12")]
    [DataRow("1.234.567,89", "1234567.89")]
    [DataRow("-1.234,50", "-1234.50")]
    [DataRow("+7,00", "7.00")]
    [DataRow("\u22127,25", "-7.25")]
    [DataRow("  42,10  ", "42.10")]
    public void CulturalValueParser_DeAtDecimal_Parses(string raw, string expected)
    {
        CulturalValueParser.TryParseDecimal(raw, ",", ".", out decimal value).Should().BeTrue();

        value.Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("1,234.50", "1234.50")]
    [DataRow("1234.5", "1234.5")]
    [DataRow("1,234,567", "1234567")]
    [DataRow("0.99", "0.99")]
    public void CulturalValueParser_EnUsDecimal_Parses(string raw, string expected)
    {
        CulturalValueParser.TryParseDecimal(raw, ".", ",", out decimal value).Should().BeTrue();

        value.Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow("1.234,50", ".", ",")]
    [DataRow("1,5", ".", ",")]
    [DataRow("1.234", ",", "'")]
    [DataRow("12.34.56", ",", ".")]
    [DataRow("1.2345,00", ",", ".")]
    [DataRow("1234.567,00", ",", ".")]
    [DataRow(".5", ".", ",")]
    [DataRow("5.", ".", ",")]
    [DataRow("1,234.5.6", ".", ",")]
    [DataRow("1,,234", ".", ",")]
    [DataRow(",234", ".", ",")]
    public void CulturalValueParser_GroupingOrSeparatorMismatch_Refuses(string raw, string decimalSeparator, string groupingSeparator)
    {
        CulturalValueParser.TryParseDecimal(raw, decimalSeparator, groupingSeparator, out decimal value).Should().BeFalse();
        value.Should().Be(0m);
    }

    [TestMethod]
    [DataRow("1 234,50", " ")]
    [DataRow("1\u00A0234,50", " ")]
    [DataRow("1\u202F234,50", "\u00A0")]
    [DataRow("1\u2009234,50", "\u202F")]
    public void CulturalValueParser_SpaceGrouping_AcceptsEverySpaceSeparator(string raw, string groupingSeparator)
    {
        CulturalValueParser.TryParseDecimal(raw, ",", groupingSeparator, out decimal value).Should().BeTrue();

        value.Should().Be(1234.50m);
    }

    [TestMethod]
    public void CulturalValueParser_ApostropheGrouping_Parses()
    {
        CulturalValueParser.TryParseDecimal("1'234'567.05", ".", "'", out decimal value).Should().BeTrue();

        value.Should().Be(1234567.05m);
    }

    [TestMethod]
    [DataRow("€ 1.234,50", "€")]
    [DataRow("€1.234,50", "€")]
    [DataRow("1.234,50 €", "€")]
    [DataRow("1.234,50€", "€")]
    [DataRow("EUR 1.234,50", "EUR")]
    [DataRow("1.234,50 EUR", "EUR")]
    [DataRow("CHF1.234,50", "CHF")]
    [DataRow("1.234,50\u00A0$", "$")]
    [DataRow("£ 1.234,50", "£")]
    public void CulturalValueParser_CurrencySymbol_ParsesAndReportsIt(string raw, string currency)
    {
        CulturalValueParser.TryParseDecimal(raw, ",", ".", out decimal value, out string? found).Should().BeTrue();

        value.Should().Be(1234.50m);
        found.Should().Be(currency);
    }

    [TestMethod]
    [DataRow("-€ 5,00")]
    [DataRow("€ -5,00")]
    [DataRow("-5,00 €")]
    public void CulturalValueParser_SignAroundCurrency_Negative(string raw)
    {
        CulturalValueParser.TryParseDecimal(raw, ",", ".", out decimal value, out string? currency).Should().BeTrue();

        value.Should().Be(-5.00m);
        currency.Should().Be("€");
    }

    [TestMethod]
    public void CulturalValueParser_NoCurrency_ReportsNull()
    {
        CulturalValueParser.TryParseDecimal("17,5", ",", ".", out decimal value, out string? currency).Should().BeTrue();

        value.Should().Be(17.5m);
        currency.Should().BeNull();
    }

    [TestMethod]
    [DataRow("€ 5,00 €")]
    [DataRow("EUR 5,00 USD")]
    [DataRow("eur 5,00")]
    [DataRow("EURO 5,00")]
    [DataRow("5,00 kg")]
    [DataRow("ca. 5,00")]
    [DataRow("5,00 - 6,00")]
    [DataRow("5 ,00")]
    [DataRow("- 5,00")]
    [DataRow("--5,00")]
    [DataRow("5,00-")]
    [DataRow("(5,00)")]
    [DataRow("€")]
    [DataRow("-")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\u0661\u0662")]
    [DataRow("1e3")]
    public void CulturalValueParser_UnexplainedText_Refuses(string raw)
    {
        CulturalValueParser.TryParseDecimal(raw, ",", ".", out decimal value, out string? currency).Should().BeFalse();

        value.Should().Be(0m);
        currency.Should().BeNull();
    }

    [TestMethod]
    public void CulturalValueParser_NullDecimal_Refuses()
        => CulturalValueParser.TryParseDecimal(null, ",", ".", out _).Should().BeFalse();

    [TestMethod]
    public void CulturalValueParser_DecimalOverflow_Refuses()
        => CulturalValueParser.TryParseDecimal("99.999.999.999.999.999.999.999.999.999,00", ",", ".", out _).Should().BeFalse();

    [TestMethod]
    public void CulturalValueParser_ManyFractionDigits_RoundsLikeDecimal()
    {
        CulturalValueParser.TryParseDecimal("0,1234567890123456789012345678901", ",", ".", out decimal value).Should().BeTrue();

        value.Should().Be(0.1234567890123456789012345679m);
    }

    [TestMethod]
    public void CulturalValueParser_NullDecimalSeparator_Throws()
    {
        Action act = () => CulturalValueParser.TryParseDecimal("1", NullString(), ".", out _);

        act.Should().Throw<ArgumentNullException>().WithParameterName("decimalSeparator");
    }

    [TestMethod]
    public void CulturalValueParser_NullGroupingSeparator_Throws()
    {
        Action act = () => CulturalValueParser.TryParseDecimal("1", ",", NullString(), out _);

        act.Should().Throw<ArgumentNullException>().WithParameterName("groupingSeparator");
    }

    [TestMethod]
    [DataRow("", ".", "decimalSeparator")]
    [DataRow(",,", ".", "decimalSeparator")]
    [DataRow("a", ".", "decimalSeparator")]
    [DataRow("5", ".", "decimalSeparator")]
    [DataRow(" ", ".", "decimalSeparator")]
    [DataRow("-", ".", "decimalSeparator")]
    [DataRow(",", "", "groupingSeparator")]
    [DataRow(",", "x", "groupingSeparator")]
    [DataRow(",", "+", "groupingSeparator")]
    [DataRow(",", ",", "groupingSeparator")]
    [DataRow("\u20AC", ".", "decimalSeparator")]
    [DataRow(",", "\u20AC", "groupingSeparator")]
    public void CulturalValueParser_InvalidSeparator_Throws(string decimalSeparator, string groupingSeparator, string parameter)
    {
        Action act = () => CulturalValueParser.TryParseDecimal("1", decimalSeparator, groupingSeparator, out _);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(parameter);
    }

    [TestMethod]
    [DataRow("31.01.2027", DateOrder.Dmy)]
    [DataRow("31.1.2027", DateOrder.Dmy)]
    [DataRow("31. 1. 2027", DateOrder.Dmy)]
    [DataRow("31/01/2027", DateOrder.Dmy)]
    [DataRow("31-01-2027", DateOrder.Dmy)]
    [DataRow("01/31/2027", DateOrder.Mdy)]
    [DataRow("1/31/2027", DateOrder.Mdy)]
    [DataRow("2027/01/31", DateOrder.Ymd)]
    [DataRow("2027.01.31", DateOrder.Ymd)]
    [DataRow("2027-1-31", DateOrder.Ymd)]
    [DataRow("  31.01.2027 ", DateOrder.Dmy)]
    [DataRow("31.01.27", DateOrder.Dmy)]
    public void CulturalValueParser_DateInDeclaredOrder_Parses(string raw, DateOrder order)
    {
        CulturalValueParser.TryParseDate(raw, order, out DateOnly value).Should().BeTrue();

        value.Should().Be(new DateOnly(2027, 1, 31));
    }

    [TestMethod]
    [DataRow(DateOrder.Dmy)]
    [DataRow(DateOrder.Mdy)]
    [DataRow(DateOrder.Ymd)]
    public void CulturalValueParser_IsoDate_ParsesInEveryOrder(DateOrder order)
    {
        CulturalValueParser.TryParseDate("2027-01-31", order, out DateOnly value).Should().BeTrue();

        value.Should().Be(new DateOnly(2027, 1, 31));
    }

    [TestMethod]
    [DataRow("01.02.2027", DateOrder.Dmy, 2027, 2, 1)]
    [DataRow("01.02.2027", DateOrder.Mdy, 2027, 1, 2)]
    [DataRow("31.12.49", DateOrder.Dmy, 2049, 12, 31)]
    [DataRow("01.01.50", DateOrder.Dmy, 1950, 1, 1)]
    [DataRow("29.02.2028", DateOrder.Dmy, 2028, 2, 29)]
    public void CulturalValueParser_DateOrderDecides(string raw, DateOrder order, int year, int month, int day)
    {
        CulturalValueParser.TryParseDate(raw, order, out DateOnly value).Should().BeTrue();

        value.Should().Be(new DateOnly(year, month, day));
    }

    [TestMethod]
    [DataRow("31.01.2027", DateOrder.Mdy)]
    [DataRow("2027/01/31", DateOrder.Dmy)]
    [DataRow("31.01/2027", DateOrder.Dmy)]
    [DataRow("29.02.2027", DateOrder.Dmy)]
    [DataRow("00.01.2027", DateOrder.Dmy)]
    [DataRow("31.13.2027", DateOrder.Dmy)]
    [DataRow("31.01.20270", DateOrder.Dmy)]
    [DataRow("31.01.202", DateOrder.Dmy)]
    [DataRow("031.01.2027", DateOrder.Dmy)]
    [DataRow("31 01 2027", DateOrder.Dmy)]
    [DataRow("31.01.2027.", DateOrder.Dmy)]
    [DataRow("31. Jänner 2027", DateOrder.Dmy)]
    [DataRow("Jan 31, 2027", DateOrder.Mdy)]
    [DataRow("2027-01-31T10:00", DateOrder.Ymd)]
    [DataRow("31.01.", DateOrder.Dmy)]
    [DataRow("0000-01-01", DateOrder.Ymd)]
    [DataRow("27.01.31", DateOrder.Ymd)]
    [DataRow("\u0663\u0661.01.2027", DateOrder.Dmy)]
    [DataRow("", DateOrder.Dmy)]
    [DataRow("   ", DateOrder.Dmy)]
    public void CulturalValueParser_DateNotInDeclaredFormat_Refuses(string raw, DateOrder order)
    {
        CulturalValueParser.TryParseDate(raw, order, out DateOnly value).Should().BeFalse();

        value.Should().Be(default);
    }

    [TestMethod]
    public void CulturalValueParser_NullDate_Refuses()
        => CulturalValueParser.TryParseDate(null, DateOrder.Dmy, out _).Should().BeFalse();

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    public void CulturalValueParser_UndefinedDateOrder_Throws(int order)
    {
        Action act = () => CulturalValueParser.TryParseDate("2027-01-31", (DateOrder)order, out _);

        act.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("order");
    }

    // A null the compiler cannot see, so a null-argument test needs no suppression operator.
    private static string NullString() => (new string[1])[0];
}
