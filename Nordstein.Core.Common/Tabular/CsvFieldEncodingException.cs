namespace Nordstein.Core.Common.Tabular;

/// <summary>
/// Thrown by <see cref="ICsvRowWriter.WriteRow"/> when a field holds a character its dialect's
/// encoding cannot write. The message names the field's position and the encoding, never the
/// character or the field's content.
/// </summary>
public sealed class CsvFieldEncodingException : ArgumentException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CsvFieldEncodingException"/> class.
    /// </summary>
    /// <param name="fieldIndex">The 0-based index of the refused field in its record.</param>
    /// <param name="encodingName">The web name of the encoding that refused it.</param>
    /// <param name="innerException">The encoder's exception.</param>
    public CsvFieldEncodingException(int fieldIndex, string encodingName, Exception? innerException)
        : base($"Field {fieldIndex} holds a character {encodingName} cannot encode.", innerException)
    {
        FieldIndex = fieldIndex;
        EncodingName = encodingName;
    }

    /// <summary>
    /// Gets the 0-based index of the refused field in its record.
    /// </summary>
    public int FieldIndex { get; }

    /// <summary>
    /// Gets the web name of the encoding that refused the field.
    /// </summary>
    public string EncodingName { get; }
}
