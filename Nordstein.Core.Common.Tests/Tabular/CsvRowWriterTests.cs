using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Tabular;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Tabular;

/// <summary>
/// The RFC 4180 row writer: quoting, CRLF, encodings, and a refused field that writes nothing.
/// </summary>
[TestClass]
public sealed class CsvRowWriterTests : BaseTest<Module>
{
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)
        ?? throw new InvalidOperationException("No Windows-1252 encoding.");

    private ICsvRowWriter Writer => GetServices().GetRequiredService<ICsvRowWriter>();

    [TestMethod]
    [DataRow(new[] { "a", "b" }, "a;b\r\n")]
    [DataRow(new[] { "a;b", "c" }, "\"a;b\";c\r\n")]
    [DataRow(new[] { "say \"hi\"" }, "\"say \"\"hi\"\"\"\r\n")]
    [DataRow(new[] { "line\nbreak", "cr\rx" }, "\"line\nbreak\";\"cr\rx\"\r\n")]
    [DataRow(new[] { "a,b" }, "a,b\r\n")]
    [DataRow(new[] { "", "" }, ";\r\n")]
    [DataRow(new string[0], "\r\n")]
    public void CsvWriter_Fields_QuotedPerRfc4180(string[] fields, string expected)
    {
        using var output = new MemoryStream();

        Writer.WriteRow(output, fields, new CsvDialect(';', new UTF8Encoding(false)));

        Encoding.UTF8.GetString(output.ToArray()).Should().Be(expected);
    }

    [TestMethod]
    public void CsvWriter_Windows1252_WritesOneBytePerUmlaut()
    {
        using var output = new MemoryStream();

        Writer.WriteRow(output, ["Größe"], new CsvDialect(',', Windows1252));

        output.ToArray().Should().Equal(0x47, 0x72, 0xF6, 0xDF, 0x65, 0x0D, 0x0A);
    }

    [TestMethod]
    public void CsvWriter_Utf8WithPreamble_WritesNoBom()
    {
        using var output = new MemoryStream();

        Writer.WriteRow(output, ["ö"], new CsvDialect(',', new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)));

        output.ToArray().Should().Equal(0xC3, 0xB6, 0x0D, 0x0A);
    }

    [TestMethod]
    public void CsvWriter_TabDelimiter_Joins()
    {
        using var output = new MemoryStream();

        Writer.WriteRow(output, ["a", "b\tc"], new CsvDialect('\t', new UTF8Encoding(false)));

        Encoding.UTF8.GetString(output.ToArray()).Should().Be("a\t\"b\tc\"\r\n");
    }

    [TestMethod]
    public void CsvWriter_UnencodableField_ThrowsWithItsIndexAndWritesNothing()
    {
        using var output = new MemoryStream();
        output.Write("kept"u8);

        // Windows-1252 has no CJK character.
        Action act = () => Writer.WriteRow(output, ["ok", "漢"], new CsvDialect(';', Windows1252));

        CsvFieldEncodingException thrown = act.Should().Throw<CsvFieldEncodingException>().Which;
        thrown.FieldIndex.Should().Be(1);
        thrown.EncodingName.Should().Be("windows-1252");
        thrown.Message.Should().NotContain("漢");
        output.ToArray().Should().Equal("kept"u8.ToArray());
    }

    [TestMethod]
    public void CsvWriter_ReplacementFallbackEncoding_StillRefuses()
    {
        using var output = new MemoryStream();
        Encoding lenient = Encoding.GetEncoding("us-ascii", EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

        Action act = () => Writer.WriteRow(output, ["ä"], new CsvDialect(',', lenient));

        act.Should().Throw<CsvFieldEncodingException>().Which.FieldIndex.Should().Be(0);
    }

    [TestMethod]
    [DataRow('"')]
    [DataRow('\r')]
    [DataRow('\n')]
    public void CsvWriter_ForbiddenDelimiter_Throws(char delimiter)
    {
        Action act = () => Writer.WriteRow(new MemoryStream(), ["a"], new CsvDialect(delimiter, Encoding.UTF8));

        act.Should().Throw<ArgumentException>().WithParameterName("dialect");
    }

    [TestMethod]
    public void CsvWriter_NullArguments_Throw()
    {
        var dialect = new CsvDialect(',', Encoding.UTF8);

        ((Action)(() => Writer.WriteRow(null!, ["a"], dialect))).Should().Throw<ArgumentNullException>();
        ((Action)(() => Writer.WriteRow(new MemoryStream(), null!, dialect))).Should().Throw<ArgumentNullException>();
        ((Action)(() => Writer.WriteRow(new MemoryStream(), ["a"], null!))).Should().Throw<ArgumentNullException>();
        ((Action)(() => Writer.WriteRow(new MemoryStream(), [null!], dialect))).Should().Throw<ArgumentNullException>();
    }

    [TestMethod]
    public void CsvWriter_ReadOnlyStream_Throws()
    {
        using var output = new MemoryStream([], writable: false);

        Action act = () => Writer.WriteRow(output, ["a"], new CsvDialect(',', Encoding.UTF8));

        act.Should().Throw<ArgumentException>().WithParameterName("output");
    }
}
