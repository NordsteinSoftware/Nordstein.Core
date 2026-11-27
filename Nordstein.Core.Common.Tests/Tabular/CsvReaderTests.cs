using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nordstein.Core.Common.Tabular;
using Nordstein.Core.Testing;

namespace Nordstein.Core.Common.Tests.Tabular;

/// <summary>
/// The delimited-text reader: dialect detection (charset, UTF-8 or Windows-1252; comma, semicolon or
/// tab) and the lenient RFC 4180 record parser.
/// </summary>
[TestClass]
public sealed class CsvReaderTests : BaseTest<Module>
{
    private static readonly Encoding Windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)
        ?? throw new InvalidOperationException("No Windows-1252 encoding.");

    private ICsvReader Reader => GetServices().GetRequiredService<ICsvReader>();

    [TestMethod]
    public async Task CsvReader_SemicolonWindows1252_ReadsCellsAndUmlauts()
    {
        using var payload = new MemoryStream(Windows1252.GetBytes("Größe;\"a;b\"\r\nÄpfel;12\r\n"));

        CsvDialect dialect = await Reader.DetectAsync(payload, null, CancellationToken.None);
        long afterDetect = payload.Position;
        List<IReadOnlyList<string>> records = await ReadAllAsync(payload, dialect);

        dialect.Delimiter.Should().Be(';');
        dialect.Encoding.CodePage.Should().Be(1252);
        afterDetect.Should().Be(0);
        records.Should().HaveCount(2);
        records[0].Should().Equal("Größe", "a;b");
        records[1].Should().Equal("Äpfel", "12");
    }

    [TestMethod]
    public async Task CsvReader_QuotedDelimiterAndLineBreak_KeepsOneCell()
    {
        List<IReadOnlyList<string>> records = await ReadAllAsync("name,note\nA,\"line one\r\nline, two\"\nB,\"say \"\"hi\"\"\"\n");

        records.Should().HaveCount(3);
        records[1].Should().Equal("A", "line one\r\nline, two");
        records[2].Should().Equal("B", "say \"hi\"");
    }

    [TestMethod]
    public async Task CsvReader_InvalidUtf8_FallsBackToWindows1252()
    {
        using var payload = new MemoryStream([.. "a,b\n"u8, 0x47, 0x72, 0xF6, 0xDF, 0x65, (byte)',', (byte)'1', (byte)'\n']);

        CsvDialect dialect = await Reader.DetectAsync(payload, null, CancellationToken.None);
        List<IReadOnlyList<string>> records = await ReadAllAsync(payload, dialect);

        dialect.Encoding.CodePage.Should().Be(1252);
        records[1].Should().Equal("Größe", "1");
    }

    [TestMethod]
    public async Task CsvReader_ValidUtf8WithBom_DetectsUtf8AndDropsTheBom()
    {
        using var payload = new MemoryStream([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("Größe,b\nx,y\n")]);

        CsvDialect dialect = await Reader.DetectAsync(payload, null, CancellationToken.None);
        List<IReadOnlyList<string>> records = await ReadAllAsync(payload, dialect);

        dialect.Encoding.CodePage.Should().Be(Encoding.UTF8.CodePage);
        records[0].Should().Equal("Größe", "b");
    }

    [TestMethod]
    public async Task CsvReader_Utf8SplitAcrossBufferReads_StaysUtf8()
    {
        // 'ö' is two bytes; the 4096-byte read boundary falls between them.
        string text = new string('a', 4095) + "ö,b\n";
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(text));

        CsvDialect dialect = await Reader.DetectAsync(payload, null, CancellationToken.None);

        dialect.Encoding.CodePage.Should().Be(Encoding.UTF8.CodePage);
    }

    [TestMethod]
    public async Task CsvReader_TruncatedUtf8AtEnd_FallsBackToWindows1252()
    {
        using var payload = new MemoryStream([.. "a,b\n"u8, 0xC3]);

        CsvDialect dialect = await Reader.DetectAsync(payload, null, CancellationToken.None);

        dialect.Encoding.CodePage.Should().Be(1252);
    }

    [TestMethod]
    [DataRow("windows-1252", 1252)]
    [DataRow("ISO-8859-1", 28591)]
    [DataRow("utf-8", 65001)]
    [DataRow("\"utf-8\"", 65001)]
    public async Task CsvReader_CharsetParameter_Wins(string charset, int codePage)
    {
        // Valid UTF-8 bytes; the charset still decides.
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes("Größe;x\n1;2\n"));

        CsvDialect dialect = await Reader.DetectAsync(payload, charset, CancellationToken.None);

        dialect.Encoding.CodePage.Should().Be(codePage);
        dialect.Delimiter.Should().Be(';');
    }

    [TestMethod]
    public async Task CsvReader_UnknownCharset_Throws()
    {
        using var payload = new MemoryStream("a,b\n"u8.ToArray());

        Func<Task> act = () => Reader.DetectAsync(payload, "x-no-such-charset", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("charset");
    }

    [TestMethod]
    public async Task CsvDialect_MixedDelimiters_PicksTheConsistentOne()
    {
        // Commas appear as decimal separators in some rows only; semicolons split every row in two.
        CsvDialect dialect = await DetectAsync("name;price\nApfel;1,50\nBirne;12\n\nKiwi;0,99\n");

        dialect.Delimiter.Should().Be(';');
    }

    [TestMethod]
    [DataRow("a\tb\tc\n1\t2\t3\n", '\t')]
    [DataRow("a,b\n1,2\n", ',')]
    [DataRow("a;b;c\n1;2\n", ';')]
    [DataRow("single\nvalue\n", ',')]
    [DataRow("", ',')]
    [DataRow("a;b,c\n", ',')]
    [DataRow("\"x;y\",z\n1,2\n", ',')]
    public async Task CsvDialect_Detect_PicksDelimiter(string text, char expected)
    {
        CsvDialect dialect = await DetectAsync(text);

        dialect.Delimiter.Should().Be(expected);
    }

    [TestMethod]
    public async Task CsvDialect_SampleLongerThan64KiB_IgnoresTheCutRecord()
    {
        var text = new StringBuilder();
        while (text.Length < 70_000)
        {
            text.Append("alpha;beta;gamma\n");
        }

        // A record cut by the sample boundary would have fewer fields; it must not break consistency.
        CsvDialect dialect = await DetectAsync(text.ToString());

        dialect.Delimiter.Should().Be(';');
    }

    [TestMethod]
    public async Task CsvReader_Detect_RestoresAnOffsetPosition()
    {
        using var payload = new MemoryStream("xxa;b\n1;2\n"u8.ToArray());
        payload.Position = 2;

        CsvDialect dialect = await Reader.DetectAsync(payload, null, CancellationToken.None);

        payload.Position.Should().Be(2);
        dialect.Delimiter.Should().Be(';');
    }

    [TestMethod]
    public async Task CsvReader_NonSeekable_Throws()
    {
        await using var payload = new ForwardOnlyStream("a,b\n"u8.ToArray());

        Func<Task> act = () => Reader.DetectAsync(payload, null, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("payload");
    }

    [TestMethod]
    public async Task CsvReader_NullArguments_Throw()
    {
        Func<Task> detect = () => Reader.DetectAsync(null!, null, CancellationToken.None);
        Func<Task> readNullStream = () => ReadAllAsync(null!, new CsvDialect(',', Encoding.UTF8));
        Func<Task> readNullDialect = () => ReadAllAsync(new MemoryStream(), null!);

        await detect.Should().ThrowAsync<ArgumentNullException>();
        await readNullStream.Should().ThrowAsync<ArgumentNullException>();
        await readNullDialect.Should().ThrowAsync<ArgumentNullException>();
    }

    [TestMethod]
    [DataRow('"')]
    [DataRow('\r')]
    [DataRow('\n')]
    public async Task CsvReader_ForbiddenDelimiter_Throws(char delimiter)
    {
        Func<Task> act = () => ReadAllAsync(new MemoryStream("a"u8.ToArray()), new CsvDialect(delimiter, Encoding.UTF8));

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("dialect");
    }

    [TestMethod]
    public async Task CsvReader_Cancelled_Throws()
    {
        using var payload = new MemoryStream("a,b\n"u8.ToArray());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> detect = () => Reader.DetectAsync(payload, null, cancellation.Token);
        Func<Task> read = async () =>
        {
            await foreach (IReadOnlyList<string> _ in Reader.ReadAsync(payload, new CsvDialect(',', Encoding.UTF8), cancellation.Token))
            {
            }
        };

        await detect.Should().ThrowAsync<OperationCanceledException>();
        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [TestMethod]
    [DataRow("", new string[0])]
    [DataRow("a,b", new[] { "a|b" })]
    [DataRow("a,b\n", new[] { "a|b" })]
    [DataRow("a,b\r\n\r\nc", new[] { "a|b", "", "c" })]
    [DataRow("a\rb\r", new[] { "a", "b" })]
    [DataRow("a,\n", new[] { "a|" })]
    [DataRow("a,", new[] { "a|" })]
    [DataRow("\"\"", new[] { "" })]
    [DataRow("\"\",x", new[] { "|x" })]
    [DataRow("\"ab\"cd,e", new[] { "abcd|e" })]
    [DataRow("ab\"cd\",e", new[] { "ab\"cd\"|e" })]
    [DataRow("\"open,\nnever closed", new[] { "open,\nnever closed" })]
    [DataRow("\"q\"\r\nx", new[] { "q", "x" })]
    [DataRow("\"q\"\nx", new[] { "q", "x" })]
    [DataRow("﻿a,b", new[] { "a|b" })]
    [DataRow("a,﻿b", new[] { "a|﻿b" })]
    public async Task CsvReader_Records_ParseLeniently(string text, string[] expected)
    {
        List<IReadOnlyList<string>> records = await ReadAllAsync(text);

        records.Select(record => string.Join('|', record)).Should().Equal(expected);
    }

    [TestMethod]
    public async Task CsvReader_RecordsAcrossBufferReads_StayWhole()
    {
        var text = new StringBuilder();
        for (int row = 0; row < 3_000; row++)
        {
            text.Append("\"quoted, ").Append(row).Append("\",\"x\"\"y\"\r\n");
        }

        List<IReadOnlyList<string>> records = await ReadAllAsync(text.ToString());

        records.Should().HaveCount(3_000);
        records[2_999].Should().Equal("quoted, 2999", "x\"y");
    }

    private async Task<CsvDialect> DetectAsync(string text)
    {
        using var payload = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await Reader.DetectAsync(payload, null, CancellationToken.None);
    }

    private Task<List<IReadOnlyList<string>>> ReadAllAsync(string text)
        => ReadAllAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), new CsvDialect(',', new UTF8Encoding(false)));

    private async Task<List<IReadOnlyList<string>>> ReadAllAsync(Stream payload, CsvDialect dialect)
    {
        var records = new List<IReadOnlyList<string>>();
        await foreach (IReadOnlyList<string> record in Reader.ReadAsync(payload, dialect, CancellationToken.None))
        {
            records.Add(record);
        }

        return records;
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
