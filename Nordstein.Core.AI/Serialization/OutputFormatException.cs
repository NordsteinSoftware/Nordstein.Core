namespace Nordstein.Core.AI.Serialization;

/// <summary>
/// Why a model reply was rejected by a strict output format.
/// </summary>
public enum OutputFormatViolation
{
    /// <summary>
    /// The reply is <see langword="null"/>, empty, whitespace, or an empty code fence.
    /// </summary>
    Empty,

    /// <summary>
    /// The reply is the start of a JSON document that ends before the document is complete, as when
    /// the model ran out of output budget.
    /// </summary>
    Truncated,

    /// <summary>
    /// The reply is not one well-formed JSON document: a syntax error, content after the document, a
    /// duplicate property, nesting deeper than 64 levels, or text that is not valid Unicode.
    /// </summary>
    Malformed,

    /// <summary>
    /// A value's JSON type is not one the schema's <c>type</c> allows.
    /// </summary>
    WrongType,

    /// <summary>
    /// An object lacks a property the schema's <c>required</c> lists.
    /// </summary>
    MissingRequired,

    /// <summary>
    /// A value is not equal to any of the schema's <c>enum</c> values.
    /// </summary>
    NotInEnum,

    /// <summary>
    /// An object has a property its schema does not declare while the schema sets
    /// <c>additionalProperties</c> to <see langword="false"/>.
    /// </summary>
    UnexpectedProperty,
}

/// <summary>
/// Thrown when a model reply does not satisfy a strict output format. Carries the JSON path of the
/// offending value and the kind of violation, never the reply or any value from it.
/// </summary>
/// <remarks>
/// The message is fixed per <see cref="OutputFormatViolation"/> and names only <see cref="Path"/>,
/// and the exception carries no inner exception, so logging it cannot leak reply content.
/// </remarks>
public sealed class OutputFormatException : FormatException
{
    /// <summary>
    /// Initializes the exception with the fixed message for <paramref name="violation"/> at
    /// <paramref name="path"/>.
    /// </summary>
    /// <param name="path">
    /// The JSON path of the offending value, starting at the document root <c>$</c>: for example
    /// <c>$</c>, <c>$.total</c>, <c>$.lines[2].sku</c> or <c>$['line items'][0]</c>. Its property
    /// names come from the schema, never from the reply.
    /// </param>
    /// <param name="violation">The kind of violation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> does not start at the root <c>$</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="violation"/> is not a defined value.</exception>
    public OutputFormatException(string path, OutputFormatViolation violation)
        : base(Describe(path, violation))
    {
        Path = path;
        Violation = violation;
    }

    /// <summary>
    /// The JSON path of the offending value, starting at the document root <c>$</c>. For
    /// <see cref="OutputFormatViolation.Empty"/>, <see cref="OutputFormatViolation.Truncated"/> and
    /// <see cref="OutputFormatViolation.Malformed"/> it is always <c>$</c>; for
    /// <see cref="OutputFormatViolation.MissingRequired"/> it is the missing property's path; for
    /// <see cref="OutputFormatViolation.UnexpectedProperty"/> it is the object's path.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The kind of violation.
    /// </summary>
    public OutputFormatViolation Violation { get; }

    private static string Describe(string path, OutputFormatViolation violation)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!path.StartsWith('$'))
        {
            throw new ArgumentException("An output path starts at the document root '$'.", nameof(path));
        }

        string kind = violation switch
        {
            OutputFormatViolation.Empty => "the reply is empty",
            OutputFormatViolation.Truncated => "the reply ends before its JSON document is complete",
            OutputFormatViolation.Malformed => "the reply is not one well-formed JSON document",
            OutputFormatViolation.WrongType => "the value is not of a type the schema allows",
            OutputFormatViolation.MissingRequired => "a required property is missing",
            OutputFormatViolation.NotInEnum => "the value is not one of the schema's enum values",
            OutputFormatViolation.UnexpectedProperty => "the object has a property the schema does not declare",
            _ => throw new ArgumentOutOfRangeException(nameof(violation), violation, "Not an output format violation."),
        };
        return $"The model output violates its format at {path}: {kind}.";
    }
}
