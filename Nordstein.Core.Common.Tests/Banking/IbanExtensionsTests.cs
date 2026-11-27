using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using Nordstein.Core.Common.Banking;

namespace Nordstein.Core.Common.Tests.Banking;

[TestClass]
public sealed class IbanExtensionsTests
{
    [TestMethod]
    [DataRow("de89 3704 0044 0532 0130 00", "DE89370400440532013000")]
    [DataRow("DE89370400440532013000", "DE89370400440532013000")]
    [DataRow("  GB82 WEST 1234 5698 7654 32\t", "GB82WEST12345698765432")]
    [DataRow("NO93 8601\n1117947", "NO9386011117947")]
    public void TryNormalizeIban_ValidWithSpaces_Normalized(string value, string expected)
    {
        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [TestMethod]
    public void TryNormalizeIban_CheckDigits00_False()
    {
        var valid = "AT00 1234 0000 5678 9012".TryNormalizeIban(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("97", "00")]
    [DataRow("98", "01")]
    [DataRow("02", "99")]
    public void TryNormalizeIban_CheckDigitsOutside02To98WithRemainder1_False(string computed, string replacement)
    {
        // A BBAN whose valid check digits are `computed`; `replacement` differs by 97, so it keeps the remainder 1.
        var valid = Enumerable.Range(0, 10_000)
            .Select(n => WithCheckDigits("XK", n.ToString("D12", CultureInfo.InvariantCulture)))
            .First(iban => iban[2..4] == computed);
        var value = valid[..2] + replacement + valid[4..];

        var accepted = value.TryNormalizeIban(out var normalized);

        valid.TryNormalizeIban(out _).Should().BeTrue();
        accepted.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("DE88370400440532013000")]
    [DataRow("DE89370400440532013001")]
    [DataRow("GB82WEST12345698765433")]
    public void TryNormalizeIban_WrongChecksum_False(string value)
    {
        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("\t\n")]
    public void TryNormalizeIban_NullOrBlank_False(string? value)
    {
        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(14)]
    [DataRow(35)]
    public void TryNormalizeIban_TooShortOrTooLong_False(int length)
    {
        var value = WithCheckDigits("XK", new string('1', length - 4));

        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow(15)]
    [DataRow(34)]
    public void TryNormalizeIban_LengthBounds_Accepted(int length)
    {
        var value = WithCheckDigits("XK", new string('1', length - 4));

        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be(value);
    }

    [TestMethod]
    [DataRow("DE89-3704-0044-0532-0130-00")]
    [DataRow("DE89/370400440532013000")]
    [DataRow("DE89.370400440532013000")]
    [DataRow("DE89370400440532013ä00")]
    [DataRow("DE8937040044053201300\u0000")]
    public void TryNormalizeIban_NonAlphanumeric_False(string value)
    {
        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("1E89370400440532013000")]
    [DataRow("D189370400440532013000")]
    [DataRow("DEX9370400440532013000")]
    [DataRow("DE8X370400440532013000")]
    public void TryNormalizeIban_CountryOrCheckDigitsMalformed_False(string value)
    {
        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("DE89370400440532013000٠")]
    [DataRow("DE８９370400440532013000")]
    public void TryNormalizeIban_NonAsciiDigitOrLetter_False(string value)
    {
        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    public void TryNormalizeIban_NoOverflowOn34Characters()
    {
        // Every BBAN character a 'Z' expands to "35": the largest numeric form an IBAN can take.
        var value = WithCheckDigits("ZZ", new string('Z', 30));

        var valid = value.TryNormalizeIban(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be(value);
        normalized.Should().HaveLength(34);
    }

    [TestMethod]
    public void TryNormalizeIban_LowercaseLetters_Uppercased()
    {
        var valid = "gb82west12345698765432".TryNormalizeIban(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be("GB82WEST12345698765432");
    }

    [TestMethod]
    public void TryNormalizeIban_UnderTurkishCulture_SameResult()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            // A culture-sensitive uppercase would turn the lowercase 'i' into the dotted 'İ' here.
            var iban = WithCheckDigits("XK", "IIII1111111111");

            var valid = iban.ToLowerInvariant().TryNormalizeIban(out var normalized);

            valid.Should().BeTrue();
            normalized.Should().Be(iban);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// Builds an IBAN from a country code and an uppercase BBAN with the ISO 7064 MOD 97-10 check digits that make
    /// it valid, computed independently of the code under test (arbitrary precision).
    /// </summary>
    private static string WithCheckDigits(string country, string bban)
    {
        var digits = string.Concat((bban + country + "00").Select(c => char.IsDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture)));
        var check = 98 - (int)(BigInteger.Parse(digits, CultureInfo.InvariantCulture) % 97);
        return country + check.ToString("00", CultureInfo.InvariantCulture) + bban;
    }
}
