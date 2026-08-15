using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Conversion;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests;

/// <summary>
/// Closes the remaining <see cref="ITypeConverter"/> branches that <see cref="TypeConverterTests"/>
/// leaves untouched: the numeric <see cref="JsonElement"/> arms beyond int/double/decimal, and the
/// fixed English format provider that keeps conversions independent of the ambient culture.
/// </summary>
[TestClass]
public sealed class TypeConverterCoverageTests : BaseTest<Module>
{
    private ITypeConverter Converter => GetServices().GetRequiredService<ITypeConverter>();

    private static JsonElement Json(string raw)
        => JsonDocument.Parse(raw).RootElement;

    [TestMethod]
    public void ChangeType_JsonElementToSByte_Converts()
        => Converter.ChangeType(Json("-128"), typeof(sbyte)).Should().Be((sbyte)-128);

    [TestMethod]
    public void ChangeType_JsonElementToByte_Converts()
        => Converter.ChangeType(Json("255"), typeof(byte)).Should().Be((byte)255);

    [TestMethod]
    public void ChangeType_JsonElementToInt16_Converts()
        => Converter.ChangeType(Json("-32768"), typeof(short)).Should().Be((short)-32768);

    [TestMethod]
    public void ChangeType_JsonElementToUInt16_Converts()
        => Converter.ChangeType(Json("65535"), typeof(ushort)).Should().Be((ushort)65535);

    [TestMethod]
    public void ChangeType_JsonElementToUInt32_Converts()
        => Converter.ChangeType(Json("4294967295"), typeof(uint)).Should().Be(4294967295U);

    [TestMethod]
    public void ChangeType_JsonElementToInt64_Converts()
        => Converter.ChangeType(Json("-9223372036854775808"), typeof(long))
            .Should().Be(-9223372036854775808L);

    [TestMethod]
    public void ChangeType_JsonElementToUInt64_Converts()
        => Converter.ChangeType(Json("18446744073709551615"), typeof(ulong))
            .Should().Be(18446744073709551615UL);

    [TestMethod]
    public void ChangeType_JsonElementToSingle_Converts()
        => Converter.ChangeType(Json("1.5"), typeof(float)).Should().Be(1.5f);

    [TestMethod]
    public void ChangeType_JsonElementToNullableInt_Converts()
        => Converter.ChangeType(Json("5"), typeof(int?)).Should().Be(5);

    [TestMethod]
    public void ChangeType_StringToDouble_UsesEnglishFormatRegardlessOfCurrentCulture()
    {
        // The converter parses with a fixed "en" provider, so a dot always means the decimal point
        // even when the current culture (here German) would read it as a group separator.
        ITypeConverter converter = Converter;
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");

            object? result = converter.ChangeType("3.5", typeof(double));

            result.Should().Be(3.5);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
