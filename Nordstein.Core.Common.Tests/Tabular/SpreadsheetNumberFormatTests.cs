using System.Globalization;
using AwesomeAssertions;
using Nordstein.Core.Common.Tabular;

namespace Nordstein.Core.Common.Tests.Tabular;

/// <summary>
/// Spreadsheet built-in number formats (ids 0–49) rendered in a culture, the spreadsheet grouping
/// of a culture, and locale tags in format codes.
/// </summary>
[TestClass]
public sealed class SpreadsheetNumberFormatTests
{
    private const double Number = 1234.5;

    // 31 December 2026, 18:30:15.
    private const double DateTimeSerial = 46387.771006944444;

    private static readonly CultureInfo DeAt = CultureInfo.GetCultureInfo("de-AT").ToSpreadsheetCulture();
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US").ToSpreadsheetCulture();

    [TestMethod]
    [DataRow(0, Number, "1234,5", "1234.5")]
    [DataRow(1, Number, "1235", "1235")]
    [DataRow(2, Number, "1234,50", "1234.50")]
    [DataRow(3, Number, "1.235", "1,235")]
    [DataRow(4, Number, "1.234,50", "1,234.50")]
    [DataRow(5, Number, "€ 1.235", "$1,235")]
    [DataRow(6, -Number, "-€ 1.235", "-$1,235")]
    [DataRow(7, Number, "€ 1.234,50", "$1,234.50")]
    [DataRow(8, -Number, "-€ 1.234,50", "-$1,234.50")]
    [DataRow(9, 0.125, "13%", "13%")]
    [DataRow(10, 0.125, "12,50%", "12.50%")]
    [DataRow(11, Number, "1,23E+03", "1.23E+03")]
    [DataRow(12, Number, "1234 1/2", "1234 1/2")]
    [DataRow(13, 0.3333, "1/3", "1/3")]
    [DataRow(14, DateTimeSerial, "31.12.2026", "12/31/2026")]
    [DataRow(15, DateTimeSerial, "31-Dez.-26", "31-Dec-26")]
    [DataRow(16, DateTimeSerial, "31-Dez.", "31-Dec")]
    [DataRow(17, DateTimeSerial, "Dez-26", "Dec-26")]
    [DataRow(18, DateTimeSerial, "6:30 PM", "6:30 PM")]
    [DataRow(19, DateTimeSerial, "6:30:15 PM", "6:30:15 PM")]
    [DataRow(20, DateTimeSerial, "18:30", "18:30")]
    [DataRow(21, DateTimeSerial, "18:30:15", "18:30:15")]
    [DataRow(22, DateTimeSerial, "31.12.2026 18:30", "12/31/2026 18:30")]
    [DataRow(23, Number, "1234,5", "1234.5")]
    [DataRow(24, Number, "1234,5", "1234.5")]
    [DataRow(25, Number, "1234,5", "1234.5")]
    [DataRow(26, Number, "1234,5", "1234.5")]
    [DataRow(27, Number, "1234,5", "1234.5")]
    [DataRow(28, Number, "1234,5", "1234.5")]
    [DataRow(29, Number, "1234,5", "1234.5")]
    [DataRow(30, Number, "1234,5", "1234.5")]
    [DataRow(31, Number, "1234,5", "1234.5")]
    [DataRow(32, Number, "1234,5", "1234.5")]
    [DataRow(33, Number, "1234,5", "1234.5")]
    [DataRow(34, Number, "1234,5", "1234.5")]
    [DataRow(35, Number, "1234,5", "1234.5")]
    [DataRow(36, Number, "1234,5", "1234.5")]
    [DataRow(37, -Number, "(1.235)", "(1,235)")]
    [DataRow(38, Number, "1.235", "1,235")]
    [DataRow(39, -Number, "(1.234,50)", "(1,234.50)")]
    [DataRow(40, Number, "1.234,50", "1,234.50")]
    [DataRow(41, -Number, "-1.235", "-1,235")]
    [DataRow(42, Number, "€ 1.235", "$1,235")]
    [DataRow(43, Number, "1.234,50", "1,234.50")]
    [DataRow(44, Number, "€ 1.234,50", "$1,234.50")]
    [DataRow(45, 0.0215277777777778, "31:00", "31:00")]
    [DataRow(46, 1.5104166666666667, "36:15:00", "36:15:00")]
    [DataRow(47, 0.0215283564814815, "31:00,1", "31:00.1")]
    [DataRow(48, Number, "1,2E+3", "1.2E+3")]
    [DataRow(49, Number, "1234,5", "1234.5")]
    public void SpreadsheetNumberFormat_BuiltInIds_FormatInCulture(int id, double value, string deAt, string enUs)
    {
        value.ToSpreadsheetText(id, DeAt).Should().Be(deAt);
        value.ToSpreadsheetText(id, EnUs).Should().Be(enUs);
    }

    [TestMethod]
    [DataRow(0, 0.30000000000000004, "0.3")]
    [DataRow(0, -0.0, "0")]
    [DataRow(0, 123456789012.0, "1.23457E+11")]
    [DataRow(0, 0.0000000001, "1E-10")]
    [DataRow(0, 99999999999.0, "99999999999")]
    [DataRow(0, double.NaN, "NaN")]
    [DataRow(12, -2.25, "-2 1/4")]
    [DataRow(12, 3.0, "3")]
    [DataRow(12, 0.0, "0")]
    [DataRow(12, 0.97, "1")]
    [DataRow(13, 0.01, "1/99")]
    [DataRow(46, 0.25, "6:00:00")]
    [DataRow(48, 0.0, "0.0E+0")]
    [DataRow(48, 0.00012, "120.0E-6")]
    [DataRow(48, 999.96, "1.0E+3")]
    [DataRow(48, -12345.0, "-12.3E+3")]
    [DataRow(21, 0.99999999, "0:00:00")]
    public void SpreadsheetNumberFormat_EdgeValues_FormatLikeTheSpreadsheet(int id, double value, string expected)
    {
        value.ToSpreadsheetText(id, CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [TestMethod]
    [DataRow(1.0, "01/01/1900")]
    [DataRow(59.0, "02/28/1900")]
    [DataRow(60.0, "02/28/1900")]
    [DataRow(61.0, "03/01/1900")]
    [DataRow(46387.0, "12/31/2026")]
    public void SpreadsheetNumberFormat_Serial1900_MapsTheLeapYearBug(double serial, string expected)
    {
        serial.ToSpreadsheetText(14, CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [TestMethod]
    public void SpreadsheetNumberFormat_Date1904_ShiftsTheEpoch()
    {
        44925.0.ToSpreadsheetText(14, CultureInfo.InvariantCulture, date1904: true).Should().Be("12/31/2026");
        0.0.ToSpreadsheetText(14, CultureInfo.InvariantCulture, date1904: true).Should().Be("01/01/1904");
    }

    [TestMethod]
    [DataRow(-1.0)]
    [DataRow(3_000_000.0)]
    [DataRow(double.PositiveInfinity)]
    public void SpreadsheetNumberFormat_SerialOutOfDateRange_FallsBackToGeneral(double serial)
    {
        serial.ToSpreadsheetText(14, CultureInfo.InvariantCulture)
            .Should().Be(serial.ToSpreadsheetText(0, CultureInfo.InvariantCulture));
    }

    [TestMethod]
    [DataRow(164)]
    [DataRow(50)]
    [DataRow(int.MaxValue)]
    public void SpreadsheetNumberFormat_CustomCode_FallsBackToGeneral(int id)
    {
        Number.ToSpreadsheetText(id, DeAt).Should().Be("1234,5");
    }

    [TestMethod]
    public void SpreadsheetNumberFormat_NegativeIdOrNullCulture_Throws()
    {
        Action negative = () => Number.ToSpreadsheetText(-1, DeAt);
        Action nullCulture = () => Number.ToSpreadsheetText(0, null!);

        negative.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("builtInFormatId");
        nullCulture.Should().Throw<ArgumentNullException>().WithParameterName("culture");
    }

    [TestMethod]
    public void SpreadsheetNumberFormat_DeAt_GroupsWithDot()
    {
        CultureInfo raw = CultureInfo.GetCultureInfo("de-AT");

        CultureInfo spreadsheet = raw.ToSpreadsheetCulture();

        spreadsheet.Name.Should().Be("de-AT");
        spreadsheet.IsReadOnly.Should().BeTrue();
        spreadsheet.NumberFormat.NumberGroupSeparator.Should().Be(".");
        spreadsheet.NumberFormat.CurrencyGroupSeparator.Should().Be(".");
        spreadsheet.NumberFormat.PercentGroupSeparator.Should().Be(".");
        raw.NumberFormat.NumberGroupSeparator.Should().NotBe(".", "the cached culture is never changed");
        Number.ToSpreadsheetText(4, spreadsheet).Should().Be("1.234,50");
    }

    [TestMethod]
    public void SpreadsheetNumberFormat_OtherCultures_KeepTheirGrouping()
    {
        CultureInfo enUs = CultureInfo.GetCultureInfo("en-US");

        enUs.ToSpreadsheetCulture().Should().BeSameAs(enUs);
        ((Action)(() => ((CultureInfo)null!).ToSpreadsheetCulture())).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    [DataRow("[$-407]dd.mm.yyyy", "de-DE")]
    [DataRow("[$-409]m/d/yyyy", "en-US")]
    [DataRow("[$-C07]#,##0.00", "de-AT")]
    [DataRow("[$€-C07] #,##0.00", "de-AT")]
    [DataRow("[$-de-AT]dd.mm.yyyy", "de-AT")]
    [DataRow("[$-en-GB]dd/mm/yyyy", "en-GB")]
    [DataRow("[$-1010409]d/m/yyyy", "en-US")]
    [DataRow("[Red][$-409]0.00", "en-US")]
    [DataRow("[$-F800]dddd, mmmm dd, yyyy[$-409]", "en-US")]
    public void SpreadsheetNumberFormat_LocaleTag_ParsesNameAndLcid(string formatCode, string expected)
    {
        formatCode.TryParseSpreadsheetLocale(out CultureInfo? culture).Should().BeTrue();

        culture?.Name.Should().Be(expected);
    }

    [TestMethod]
    [DataRow("#,##0.00")]
    [DataRow("[$-F800]dddd")]
    [DataRow("[$-0]0")]
    [DataRow("[$€]0")]
    [DataRow("[$-xx-NOPE]0")]
    [DataRow("[$-407")]
    [DataRow("")]
    public void SpreadsheetNumberFormat_NoUsableLocaleTag_ReturnsFalse(string formatCode)
    {
        formatCode.TryParseSpreadsheetLocale(out CultureInfo? culture).Should().BeFalse();

        culture.Should().BeNull();
    }

    [TestMethod]
    public void SpreadsheetNumberFormat_NullFormatCode_Throws()
    {
        Action act = () => ((string)null!).TryParseSpreadsheetLocale(out _);

        act.Should().Throw<ArgumentNullException>();
    }
}
