namespace Nordstein.Core.Common.Tabular;

/// <summary>
/// Reads delimited text (CSV and its semicolon and tab variants) as a sequence of records, after
/// detecting how the file was written.
/// </summary>
/// <remarks>
/// Parsing follows RFC 4180 and is lenient where the RFC is strict, never failing on content: a
/// field opened with a double quote runs to the matching quote, and a doubled quote inside it is one
/// quote; a delimiter, CR or LF inside quotes is field content, kept verbatim. Characters after a
/// closing quote, and a quote inside an unquoted field, are kept verbatim. An unterminated quote
/// runs to the end of the input. Outside quotes, CRLF, LF and a lone CR each end a record; a line
/// break at the very end adds no record, and an empty line in the middle is a record with one empty
/// field. Implementations are stateless and thread-safe.
/// </remarks>
public interface ICsvReader
{
    /// <summary>
    /// Detects the dialect of <paramref name="payload"/>, then restores its position.
    /// </summary>
    /// <param name="payload">
    /// A readable, seekable stream positioned at the first byte of the file; the caller owns it.
    /// </param>
    /// <param name="charset">
    /// A <c>charset</c> named by the file's media type, or <see langword="null"/>. When set, it wins
    /// over detection.
    /// </param>
    /// <param name="cancellationToken">Checked between buffer reads.</param>
    /// <returns>
    /// The encoding: <paramref name="charset"/> when set; else UTF-8 when the whole payload is valid
    /// UTF-8; else Windows-1252. The delimiter: of <c>,</c>, <c>;</c> and tab, the one that splits
    /// the first non-blank records (at most 20, within the first 64 KiB of text) into the same number
    /// of fields, more than one, and the most fields among such; ties go in that order. When none
    /// does, the one giving the first record the most fields; when none splits it, <c>,</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="payload"/> cannot read or seek, or <paramref name="charset"/> names no encoding
    /// .NET or its code-pages provider knows.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<CsvDialect> DetectAsync(Stream payload, string? charset, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the records of <paramref name="payload"/> from its position to its end, one at a time.
    /// </summary>
    /// <param name="payload">A readable stream; the caller owns it. It is read lazily, not seeked.</param>
    /// <param name="dialect">How the file was written, typically from <see cref="DetectAsync"/>.</param>
    /// <param name="cancellationToken">Checked between buffer reads.</param>
    /// <returns>
    /// Each record's fields in order, values verbatim (no trimming, no type conversion). A leading
    /// U+FEFF (a byte-order mark) is dropped. Records may have different field counts.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> or <paramref name="dialect"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="payload"/> cannot read, or the dialect's delimiter is a double quote, CR or LF.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    IAsyncEnumerable<IReadOnlyList<string>> ReadAsync(Stream payload, CsvDialect dialect, CancellationToken cancellationToken);
}
