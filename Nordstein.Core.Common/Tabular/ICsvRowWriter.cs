namespace Nordstein.Core.Common.Tabular;

/// <summary>
/// Writes records as RFC 4180 delimited text: fields joined by the dialect's delimiter, a field
/// holding the delimiter, a double quote, CR or LF enclosed in double quotes with its quotes
/// doubled, and every record ended by CRLF. No preamble (byte-order mark) is ever written.
/// Implementations are stateless and thread-safe.
/// </summary>
public interface ICsvRowWriter
{
    /// <summary>
    /// Encodes one record and writes it, line end included, to <paramref name="output"/>.
    /// </summary>
    /// <param name="output">A writable stream; the caller owns it.</param>
    /// <param name="fields">The record's fields in order; an empty list writes only the line end.</param>
    /// <param name="dialect">The delimiter and the encoding.</param>
    /// <remarks>
    /// The whole record is encoded before any byte is written, so a refused record leaves
    /// <paramref name="output"/> untouched.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument or a field is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="output"/> cannot write, or the dialect's delimiter is a double quote, CR or LF.
    /// </exception>
    /// <exception cref="CsvFieldEncodingException">
    /// A field holds a character the dialect's encoding cannot write; the message never quotes it.
    /// </exception>
    void WriteRow(Stream output, IReadOnlyList<string> fields, CsvDialect dialect);
}
