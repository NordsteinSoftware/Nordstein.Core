using System.Text;

namespace Nordstein.Core.Common.Tabular;

/// <summary>
/// How a delimited text file is written: the field delimiter and the character encoding. Quoting
/// always follows RFC 4180 (a field holding the delimiter, a double quote, CR or LF is enclosed in
/// double quotes, its quotes doubled).
/// </summary>
/// <param name="Delimiter">
/// The field delimiter, such as <c>,</c>, <c>;</c> or a tab. Never a double quote, CR or LF.
/// </param>
/// <param name="Encoding">
/// The character encoding of the bytes. A reader decodes with it; a writer encodes with it and never
/// writes its preamble (byte-order mark).
/// </param>
public sealed record CsvDialect(char Delimiter, Encoding Encoding);
