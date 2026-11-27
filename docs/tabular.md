# Tabular — delimited text and spreadsheet number formats

`Nordstein.Core.Common.Tabular` reads and writes delimited text (CSV and its semicolon and tab
variants) and renders a spreadsheet cell's number as the spreadsheet shows it. No package
dependency: the BCL's `CodePagesEncodingProvider` supplies Windows-1252.

## `ICsvReader`

Injected (`Module` registers a singleton; stateless, thread-safe).

| Member | Contract |
|--------|----------|
| `DetectAsync(stream, charset, ct)` → `CsvDialect` | Seekable stream required; its position is restored. Encoding: `charset` when given (quotes trimmed; unknown: `ArgumentException`), else UTF-8 if the whole stream decodes strictly, else Windows-1252. Delimiter: of `,` `;` tab, the one splitting the first ≤ 20 non-blank records (within 64 KiB of text, a record cut by the sample excluded) into the same count of more than one field, the most fields winning; ties in that order; none consistent: the most fields in the first record; none splits: `,`. |
| `ReadAsync(stream, dialect, ct)` → records | Lazy, forward-only. RFC 4180, lenient: quoted fields hold delimiters and line breaks verbatim, `""` is one quote; text after a closing quote and quotes inside an unquoted field are kept; an unterminated quote runs to the end. CRLF, LF and CR end a record; a final line break adds none; an empty line is a record of one empty field. A leading U+FEFF is dropped. A delimiter `"`, CR or LF: `ArgumentException`. |

The token is checked between buffer reads. Values come back verbatim: no trimming, no type
conversion.

## `ICsvRowWriter`

`WriteRow(output, fields, dialect)` writes one record ending in CRLF; a field holding the
delimiter, a quote, CR or LF is quoted with its quotes doubled. The record is encoded whole before
anything is written, so a refusal leaves the stream untouched: a character the encoding cannot write
throws `CsvFieldEncodingException` (an `ArgumentException`) with the field's index and the
encoding's name, never the character. The encoding's replacement fallback is never used, and no
byte-order mark is written.

## `SpreadsheetNumberFormatExtensions`

Pure extension methods.

| Member | Contract |
|--------|----------|
| `double.ToSpreadsheetText(builtInFormatId, culture, date1904 = false)` | ECMA-376 built-in formats 0–49: General (≤ 10 decimals, scientific from 10¹¹ or below 10⁻⁹), integers, decimals, grouping, currency and accounting (culture currency, half away from zero), percent, scientific, engineering (48), fractions (12: ≤ 9, 13: ≤ 99 as denominator), dates (14 and 22 use the culture's short date), times, elapsed `[h]:mm:ss`, `mm:ss.0`. Ids 23–36, 49 and any id above 49 (custom codes) render as General; a negative id throws. Dates: the 1900 system (serial 60, the phantom 29 Feb 1900, renders as 28 Feb) or 1904; outside 1900/1904–9999: General. |
| `CultureInfo.ToSpreadsheetCulture()` | `de-AT`: a read-only copy grouping with `.` (a spreadsheet's `1.234,50`, where CLDR gives a no-break space); any other culture as is. |
| `string.TryParseSpreadsheetLocale(out culture)` | The first usable `[$…-LCID]` or `[$-name]` tag of a format code (`[$-407]`, `[$€-C07]`, `[$-de-AT]`); the LCID's low 16 bits; system tags (`F400`, `F800`), LCID 0 and unknown names are skipped. |

Format a workbook's cells with `ToSpreadsheetCulture()` of the culture its tags name, once per
workbook, not per cell.
