using System.Runtime.CompilerServices;
using System.Text;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.Common.Tabular.Internal;

/// <inheritdoc cref="ICsvReader"/>
internal sealed class CsvReader : ICsvReader
{
    private const int ByteBufferSize = 4_096;
    private const int CharBufferSize = 16_384;
    private const int SampleCharacters = 64 * 1_024;
    private const int SampleRecords = 20;
    private const int Windows1252CodePage = 1252;

    // Candidates in tie-break order.
    private const string Candidates = ",;\t";

    public async Task<CsvDialect> DetectAsync(Stream payload, string? charset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.CanRead || !payload.CanSeek)
        {
            throw new ArgumentException("The payload must be readable and seekable.", nameof(payload));
        }

        long origin = payload.Position;
        try
        {
            Encoding encoding = charset is null
                ? await DetectEncodingAsync(payload, cancellationToken)
                : EncodingOf(charset);
            payload.Position = origin;
            (string sample, bool whole) = await ReadSampleAsync(payload, encoding, cancellationToken);
            return new CsvDialect(DetectDelimiter(sample, whole), encoding);
        }
        finally
        {
            payload.Position = origin;
        }
    }

    public IAsyncEnumerable<IReadOnlyList<string>> ReadAsync(Stream payload, CsvDialect dialect, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(dialect);
        if (!payload.CanRead)
        {
            throw new ArgumentException("The payload must be readable.", nameof(payload));
        }

        EnsureDelimiter(dialect);
        return ReadRecordsAsync(payload, dialect, cancellationToken);
    }

    /// <summary>
    /// Refuses a delimiter the RFC 4180 quoting cannot tell apart from content.
    /// </summary>
    internal static void EnsureDelimiter(CsvDialect dialect)
    {
        if (dialect.Delimiter is '"' or '\r' or '\n')
        {
            throw new ArgumentException("The delimiter cannot be a double quote, CR or LF.", nameof(dialect));
        }
    }

    private static async IAsyncEnumerable<IReadOnlyList<string>> ReadRecordsAsync(
        Stream payload, CsvDialect dialect, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(payload, dialect.Encoding, detectEncodingFromByteOrderMarks: false, CharBufferSize, leaveOpen: true);
        var tokenizer = new CsvTokenizer(dialect.Delimiter);
        var completed = new List<IReadOnlyList<string>>();
        char[] buffer = new char[CharBufferSize];
        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            tokenizer.Consume(buffer.AsSpan(0, read), completed);
            foreach (IReadOnlyList<string> record in completed)
            {
                yield return record;
            }

            completed.Clear();
        }

        tokenizer.Finish(completed);
        foreach (IReadOnlyList<string> record in completed)
        {
            yield return record;
        }
    }

    private static Encoding EncodingOf(string charset)
    {
        string name = charset.Trim().Trim('"');
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(name) ?? Encoding.GetEncoding(name);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("The charset names no known encoding.", nameof(charset), exception);
        }
    }

    // The whole payload must decode as UTF-8; the decoder keeps a sequence split across reads.
    private static async Task<Encoding> DetectEncodingAsync(Stream payload, CancellationToken cancellationToken)
    {
        UTF8Encoding utf8 = StrictEncoding.Utf8;
        Decoder decoder = utf8.GetDecoder();
        byte[] bytes = new byte[ByteBufferSize];
        char[] chars = new char[utf8.GetMaxCharCount(ByteBufferSize)];
        try
        {
            int read;
            while ((read = await payload.ReadAsync(bytes, cancellationToken)) > 0)
            {
                decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
            }

            decoder.GetChars(bytes, 0, 0, chars, 0, flush: true);
            return utf8;
        }
        catch (DecoderFallbackException)
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(Windows1252CodePage)
                ?? throw new InvalidOperationException("The code-pages provider has no Windows-1252 encoding.");
        }
    }

    private static async Task<(string Sample, bool Whole)> ReadSampleAsync(Stream payload, Encoding encoding, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(payload, encoding, detectEncodingFromByteOrderMarks: false, CharBufferSize, leaveOpen: true);
        char[] buffer = new char[SampleCharacters];
        int filled = 0;
        int read;
        while (filled < buffer.Length && (read = await reader.ReadAsync(buffer.AsMemory(filled), cancellationToken)) > 0)
        {
            filled += read;
        }

        bool whole = filled < buffer.Length || reader.Peek() < 0;
        return (new string(buffer, 0, filled), whole);
    }

    private static char DetectDelimiter(string sample, bool whole)
    {
        char best = Candidates[0];
        int bestFields = 1;
        bool bestConsistent = false;
        foreach (char candidate in Candidates)
        {
            List<IReadOnlyList<string>> records = SampleRecordsOf(sample, whole, candidate);
            if (records.Count == 0)
            {
                continue;
            }

            int fields = records[0].Count;
            bool consistent = fields > 1 && records.TrueForAll(record => record.Count == fields);
            if ((consistent && !bestConsistent) || (consistent == bestConsistent && fields > bestFields))
            {
                best = candidate;
                bestFields = fields;
                bestConsistent = consistent;
            }
        }

        return best;
    }

    // The first non-blank records; a record the sample cut off is not one of them.
    private static List<IReadOnlyList<string>> SampleRecordsOf(string sample, bool whole, char delimiter)
    {
        var tokenizer = new CsvTokenizer(delimiter);
        var records = new List<IReadOnlyList<string>>();
        tokenizer.Consume(sample, records);
        if (whole)
        {
            tokenizer.Finish(records);
        }

        records.RemoveAll(record => record is [""]);
        if (records.Count > SampleRecords)
        {
            records.RemoveRange(SampleRecords, records.Count - SampleRecords);
        }

        return records;
    }
}
