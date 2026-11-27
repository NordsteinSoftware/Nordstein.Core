using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nordstein.Core.AI.Clients;

/// <summary>
/// The shape a request asks the model to give its reply, carried by
/// <see cref="ModelOptions.ResponseFormat"/>. One of: <see cref="JsonSchemaResponseFormat"/>.
/// </summary>
/// <remarks>
/// The variants are the ones this package defines; a variant defined outside this package is
/// unsupported, and a client that renders a request handles only the variants listed here. A later
/// variant in this package is an additive change. Instances are immutable and thread-safe.
/// </remarks>
public abstract record ResponseFormat
{
    private protected ResponseFormat()
    {
    }

    /// <summary>
    /// Renders the value of the <c>response_format</c> member of an OpenAI-compatible chat
    /// completions request body.
    /// </summary>
    /// <remarks>
    /// A client that builds its request body by hand writes this value under
    /// <c>response_format</c> when <see cref="ModelOptions.ResponseFormat"/> is set, and writes no
    /// <c>response_format</c> member at all when it is <see langword="null"/>. Every call returns a
    /// new object the caller may mutate or attach to a parent node.
    /// </remarks>
    /// <returns>A new JSON object.</returns>
    public abstract JsonObject ToOpenAiCompatibleJson();
}

/// <summary>
/// Asks the model for a reply that is one JSON document matching <see cref="Schema"/>: the
/// OpenAI-compatible <c>response_format</c> of type <c>json_schema</c>.
/// </summary>
/// <remarks>
/// <para>
/// The schema travels as given. Providers that decode against it (<c>strict: true</c>) accept only a
/// subset of JSON Schema, for example every object with <c>additionalProperties: false</c> and every
/// property listed in <c>required</c>; meeting that subset is the caller's job, because the record
/// never rewrites the schema. A schema the provider does not accept fails at the provider.
/// </para>
/// <para>
/// Immutable and thread-safe. Equality is by value: <see cref="Name"/>, the compact
/// <see cref="Schema"/> text and <see cref="Strict"/>.
/// </para>
/// </remarks>
public sealed record JsonSchemaResponseFormat : ResponseFormat
{
    // The OpenAI-compatible wire limit on json_schema.name.
    private const int MaxNameLength = 64;

    /// <summary>
    /// Initializes the response format.
    /// </summary>
    /// <param name="name">
    /// The schema's name on the wire: 1 to 64 characters, each an ASCII letter or digit, <c>_</c>
    /// or <c>-</c>.
    /// </param>
    /// <param name="schema">The JSON Schema as JSON text: exactly one JSON object.</param>
    /// <param name="strict">
    /// Whether the provider is asked to decode strictly against the schema (<c>strict</c> on the
    /// wire). Defaults to <see langword="true"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="schema"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is outside the wire alphabet or length, or <paramref name="schema"/>
    /// is not exactly one JSON object (malformed, another JSON value, trailing content, or a
    /// duplicate property).
    /// </exception>
    public JsonSchemaResponseFormat(string name, string schema, bool strict = true)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(schema);
        if (name.Length is 0 or > MaxNameLength || !name.All(IsNameCharacter))
        {
            throw new ArgumentException(
                $"A JSON schema response format name is 1 to {MaxNameLength} characters, each an ASCII letter or digit, '_' or '-'.",
                nameof(name));
        }

        Name = name;
        Schema = Compact(schema);
        Strict = strict;
    }

    /// <summary>
    /// The schema's name on the wire (<c>json_schema.name</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The JSON Schema as compact JSON text (no insignificant whitespace, property order as given).
    /// </summary>
    public string Schema { get; }

    /// <summary>
    /// Whether the provider is asked to decode strictly against <see cref="Schema"/>
    /// (<c>json_schema.strict</c>).
    /// </summary>
    public bool Strict { get; }

    /// <summary>
    /// Renders <c>{"type":"json_schema","json_schema":{"name":…,"schema":…,"strict":…}}</c>, the
    /// schema as a JSON object.
    /// </summary>
    /// <inheritdoc />
    public override JsonObject ToOpenAiCompatibleJson()
        => new()
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = Name,
                ["schema"] = JsonNode.Parse(Schema),
                ["strict"] = Strict,
            },
        };

    private static bool IsNameCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '-';

    private static string Compact(string schema)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(schema, new JsonDocumentOptions { AllowDuplicateProperties = false });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("A JSON schema is a JSON object.", nameof(schema));
            }

            // Relaxed escaping keeps non-ASCII descriptions readable in a prompt; the text is never
            // embedded in HTML, which is the only context the default escaping protects.
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                document.RootElement.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
        // WHY: JsonException is malformed JSON or a duplicate property; InvalidOperationException is a
        // string whose escapes decode to invalid UTF-16. Both mean the text is not a usable schema.
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ArgumentException("A JSON schema is exactly one well-formed JSON object.", nameof(schema), exception);
        }
    }
}
