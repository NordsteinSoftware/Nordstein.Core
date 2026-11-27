using System.Text;

namespace Nordstein.Core.Common.Tabular.Internal;

/// <inheritdoc cref="ICsvRowWriter"/>
internal sealed class CsvRowWriter : ICsvRowWriter
{
    private const string LineEnd = "\r\n";

    public void WriteRow(Stream output, IReadOnlyList<string> fields, CsvDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(dialect);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The output must be writable.", nameof(output));
        }

        CsvReader.EnsureDelimiter(dialect);

        // A clone with exception fallbacks: a replacement fallback would silently write '?'.
        var encoding = (Encoding)dialect.Encoding.Clone();
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        string delimiter = dialect.Delimiter.ToString();

        using var row = new MemoryStream();
        for (int index = 0; index < fields.Count; index++)
        {
            string field = fields[index] ?? throw new ArgumentNullException(nameof(fields), $"Field {index} is null.");
            if (index > 0)
            {
                row.Write(encoding.GetBytes(delimiter));
            }

            try
            {
                row.Write(encoding.GetBytes(Quoted(field, dialect.Delimiter)));
            }
            catch (EncoderFallbackException exception)
            {
                throw new CsvFieldEncodingException(index, dialect.Encoding.WebName, exception);
            }
        }

        row.Write(encoding.GetBytes(LineEnd));
        row.WriteTo(output);
    }

    private static string Quoted(string field, char delimiter)
        => field.AsSpan().IndexOfAny(delimiter, '"', '\r') >= 0 || field.Contains('\n', StringComparison.Ordinal)
            ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : field;
}
