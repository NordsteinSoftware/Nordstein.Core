using AwesomeAssertions;
using Nordstein.Core.Common.Banking;

namespace Nordstein.Core.Common.Tests.Banking;

[TestClass]
public sealed class VatIdExtensionsTests
{
    // Published VAT ids of real companies and the examples of the national rules; each passes its country's checksum.
    [TestMethod]
    [DataRow("ATU13585627", "ATU13585627")]
    [DataRow("atu 135 856 27", "ATU13585627")]
    [DataRow("DE 136,695 976", "DE136695976")]
    [DataRow("DE143454214", "DE143454214")]
    [DataRow("DE129274202", "DE129274202")]
    [DataRow("BE 0428.759.497", "BE0428759497")]
    [DataRow("BE0417497106", "BE0417497106")]
    [DataRow("NL004495445B01", "NL004495445B01")]
    [DataRow("FR 40 303 265 045", "FR40303265045")]
    [DataRow("IT 00743110157", "IT00743110157")]
    [DataRow("DK 13 58 56 28", "DK13585628")]
    [DataRow("FI 20774740", "FI20774740")]
    [DataRow("LU 150 274 42", "LU15027442")]
    [DataRow("PL 856-734-62-15", "PL8567346215")]
    [DataRow("PT 501 964 843", "PT501964843")]
    [DataRow("SE 123456789701", "SE123456789701")]
    [DataRow("CHE-107.787.577", "CHE107787577")]
    [DataRow("CHE-107.787.577 MWST", "CHE107787577")]
    [DataRow("CHE-116.281.710 TVA", "CHE116281710")]
    [DataRow("che116281710iva", "CHE116281710")]
    public void TryNormalizeVatId_ValidChecksum_Normalized(string value, string expected)
    {
        var valid = value.TryNormalizeVatId(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    // One digit off each valid id above: the checksum refuses it.
    [TestMethod]
    [DataRow("ATU13585628")]
    [DataRow("DE136695977")]
    [DataRow("BE0428759498")]
    [DataRow("NL004495446B01")]
    [DataRow("FR41303265045")]
    [DataRow("IT00743110158")]
    [DataRow("DK13585629")]
    [DataRow("FI20774741")]
    [DataRow("LU15027443")]
    [DataRow("PL8567346216")]
    [DataRow("PT501964844")]
    [DataRow("SE123456789801")]
    [DataRow("CHE107787578")]
    public void TryNormalizeVatId_WrongChecksum_False(string value)
    {
        var valid = value.TryNormalizeVatId(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    [DataRow("ATU1358562")] // seven digits
    [DataRow("AT13585627")] // no U
    [DataRow("DE13669597")] // eight digits
    [DataRow("BE2428759497")] // ten digits starting with 2
    [DataRow("NL004495445C01")] // no B
    [DataRow("SE123456789702")] // suffix other than 01
    [DataRow("CHE107787577XYZ")] // unknown suffix
    [DataRow("CHE10778757")] // eight digits
    [DataRow("PL856734621")] // nine digits
    public void TryNormalizeVatId_WrongShapeForCountry_False(string value)
    {
        var valid = value.TryNormalizeVatId(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }

    [TestMethod]
    public void TryNormalizeVatId_DanishRemainderOtherThanZero_False()
    {
        // The weighted sum of 13585627 is 1 less than a multiple of 11.
        var valid = "DK13585627".TryNormalizeVatId(out _);

        valid.Should().BeFalse();
    }

    [TestMethod]
    public void TryNormalizeVatId_PolishRemainder10_False()
    {
        // The weighted sum of 100000016 is 6·1 + 6·1 + 7·6 = 54, and 54 mod 11 = 10: no check digit can match.
        var valid = "PL1000000160".TryNormalizeVatId(out _);

        valid.Should().BeFalse();
    }

    [TestMethod]
    public void TryNormalizeVatId_DutchSoleProprietorMod97_True()
    {
        // Since 2020 a sole proprietor's id passes ISO 7064 MOD 97-10 over "NL" and the number instead of the 11-test.
        var valid = "NL000099998B57".TryNormalizeVatId(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be("NL000099998B57");
    }

    [TestMethod]
    public void TryNormalizeVatId_FrenchAlphabeticKey_ShapeOnly()
    {
        // A French key with letters (a new-style number) has no published checksum over the SIREN; the shape decides.
        var valid = "FR K7 399859412".TryNormalizeVatId(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be("FRK7399859412");
    }

    [TestMethod]
    [DataRow("ESB58378431", "ESB58378431")] // a country without a checksum rule here: shape only
    [DataRow("EL 094 014 201", "EL094014201")]
    [DataRow("GB 123 4567 89", "GB123456789")]
    public void TryNormalizeVatId_CountryWithoutRule_ShapeOnly(string value, string expected)
    {
        var valid = value.TryNormalizeVatId(out var normalized);

        valid.Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("n/a")]
    [DataRow("—")]
    [DataRow("ESABCDEFGH")] // no digit
    [DataRow("1234567890")] // no country prefix
    [DataRow("E1234567890")] // one letter
    [DataRow("ES1234")] // too short
    [DataRow("ES1234567890123456")] // too long
    [DataRow("ES 1234_5678")] // a character outside letters, digits and separators
    [DataRow("ÄT12345678")] // a non-ASCII letter
    public void TryNormalizeVatId_NotAVatId_False(string? value)
    {
        var valid = value.TryNormalizeVatId(out var normalized);

        valid.Should().BeFalse();
        normalized.Should().BeEmpty();
    }
}
