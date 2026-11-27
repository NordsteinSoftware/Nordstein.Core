using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Serialization.Internal;
using Nordstein.Core.Common.Text;

namespace Nordstein.Core.AI.Serialization;

/// <summary>
/// A strict output format over a JSON Schema known only at runtime: the request carries the schema
/// as its <c>response_format</c> (<see cref="ResponseFormat"/>), and
/// <see cref="ParseAsync{TOutput}"/> accepts only a reply that is one complete JSON document
/// validating against it. A truncated or non-conforming reply is an
/// <see cref="OutputFormatException"/>, never a partial result.
/// </summary>
/// <remarks>
/// <para>
/// The schema is sent as given and never rewritten into a provider's strict dialect: a provider
/// that decodes strictly accepts only part of JSON Schema (for example, every object with
/// <c>additionalProperties: false</c> and every property in <c>required</c>, an optional value as a
/// type array such as <c>["string","null"]</c>), and building a schema it accepts is the caller's
/// job.
/// </para>
/// <para>
/// Locally the reply is checked against <c>type</c> (one name or an array of names; <c>integer</c>
/// is a number without a fractional part, so <c>1.0</c> counts), <c>required</c>, <c>enum</c>
/// (equality by JSON value), <c>properties</c>, <c>items</c> (one schema for every element) and a
/// boolean <c>additionalProperties</c> (<see langword="false"/> rejects an undeclared property;
/// absent or <see langword="true"/> keeps it unchecked), recursively. The annotations
/// <c>title</c>, <c>description</c>, <c>$comment</c>, <c>$schema</c>, <c>default</c> and
/// <c>examples</c> are allowed. Every other keyword — <c>$ref</c>, <c>$defs</c>, conditionals and
/// combinators, <c>const</c>, and value constraints such as <c>format</c>, <c>pattern</c> or
/// <c>minimum</c> — is rejected by the constructor, so the schema never promises a check this
/// format would skip; the caller validates values beyond the type.
/// </para>
/// <para>
/// Unlike the type-driven JSON format, this one never repairs a reply: no salvage of truncated
/// JSON, no prefix stripping. It removes surrounding whitespace and one surrounding Markdown code
/// fence whose opening line is three backticks plus at most a bare language tag (a letter, then
/// letters, digits, <c>_</c>, <c>-</c>, <c>+</c> or <c>.</c>, and not <c>true</c>, <c>false</c> or
/// <c>null</c>, such as <c>json</c>). Any other text on the opening line could be part of the
/// answer, which removing the fence would drop, so such a reply is
/// <see cref="OutputFormatViolation.Malformed"/>.
/// </para>
/// <para>
/// Immutable and thread-safe: the schema is copied and compiled at construction, so later changes
/// to the caller's <see cref="JsonNode"/> have no effect, and one instance may parse concurrently.
/// </para>
/// </remarks>
public sealed class JsonSchemaOutputFormat : IOutputFormat
{
    // System.Text.Json's default; stated so the reader classification below uses the same limit.
    private const int MaxDepth = 64;

    private const string Fence = "```";

    private const string LanguageTagCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-+.";

    private readonly JsonSchemaValidator validator;

    /// <summary>
    /// Initializes the format and compiles <paramref name="schema"/>.
    /// </summary>
    /// <param name="schema">The JSON Schema: a JSON object within the supported subset. It is copied.</param>
    /// <param name="name">
    /// The schema's name on the wire (<c>json_schema.name</c>): 1 to 64 characters, each an ASCII
    /// letter or digit, <c>_</c> or <c>-</c>.
    /// </param>
    /// <param name="strict">
    /// Whether the request asks the provider to decode strictly against the schema
    /// (<c>json_schema.strict</c>). It affects only the request: the local parse is strict either way.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is outside the wire alphabet or length; or <paramref name="schema"/>
    /// is not a JSON object, uses a keyword outside the supported subset, or gives a keyword a value
    /// of an unsupported form (the message names the keyword and its JSON Pointer in the schema).
    /// </exception>
    public JsonSchemaOutputFormat(JsonNode schema, string name, bool strict = true)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(name);
        if (schema.GetValueKind() != JsonValueKind.Object)
        {
            throw new ArgumentException("A JSON schema is a JSON object.", nameof(schema));
        }

        ResponseFormat = new JsonSchemaResponseFormat(name, schema.ToJsonString(), strict);
        using JsonDocument compiled = JsonDocument.Parse(ResponseFormat.Schema);
        validator = new JsonSchemaValidator(compiled.RootElement.Clone());
    }

    /// <summary>
    /// The response format a request carries for this schema: put it on
    /// <see cref="ModelOptions.ResponseFormat"/>, or write its
    /// <see cref="Clients.ResponseFormat.ToOpenAiCompatibleJson"/> under <c>response_format</c>.
    /// </summary>
    public JsonSchemaResponseFormat ResponseFormat { get; }

    /// <summary>
    /// Parses <paramref name="output"/> strictly into a <see cref="JsonNode"/> that validates
    /// against the schema.
    /// </summary>
    /// <typeparam name="TOutput">Must be <see cref="JsonNode"/>.</typeparam>
    /// <param name="output">The model's raw reply text.</param>
    /// <param name="cancellationToken">Observed before the parse starts; the parse itself is bounded CPU work.</param>
    /// <returns>
    /// The document as a new <see cref="JsonNode"/> the caller owns, or <see langword="null"/> when
    /// the reply is the JSON literal <c>null</c> and the schema allows it.
    /// </returns>
    /// <exception cref="InvalidOperationException"><typeparamref name="TOutput"/> is not <see cref="JsonNode"/> (thrown synchronously).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled (in the returned task).</exception>
    /// <exception cref="OutputFormatException">
    /// The reply is empty, truncated, malformed, or violates the schema (in the returned task). The
    /// exception names the JSON path and the violation, never the reply.
    /// </exception>
    public Task<TOutput?> ParseAsync<TOutput>(string? output, CancellationToken cancellationToken = default)
    {
        if (typeof(TOutput) != typeof(JsonNode))
        {
            throw new InvalidOperationException(
                $"{nameof(JsonSchemaOutputFormat)} parses to {nameof(JsonNode)} only, not to {typeof(TOutput).FullName}.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<TOutput?>(cancellationToken);
        }

        try
        {
            return Task.FromResult((TOutput?)(object?)Parse(output));
        }
        catch (OutputFormatException exception)
        {
            return Task.FromException<TOutput?>(exception);
        }
    }

    /// <summary>
    /// Returns an instruction to answer with one JSON document that adheres to the schema, followed
    /// by the compact schema, for a prompt to a backend that may ignore <c>response_format</c>.
    /// </summary>
    public string ToPromptString()
        => $"Respond only with one JSON document that adheres to the following JSON schema definition:\n{ResponseFormat.Schema}";

    /// <summary>
    /// Returns no errors: the constructor already rejected every schema this format cannot use.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => [];

    private JsonNode? Parse(string? output)
    {
        string text = string.IsNullOrWhiteSpace(output) ? string.Empty : output.Trim();
        if (text.StartsWith(Fence, StringComparison.Ordinal))
        {
            // StripCodeFence drops the whole opening line as the language tag; anything but a bare tag there
            // ("```[1]" or "```{...}") would be answer text lost without a trace.
            int lineBreak = text.IndexOf('\n', StringComparison.Ordinal);
            if (lineBreak >= 0 && !IsLanguageTag(text.AsSpan(Fence.Length, lineBreak - Fence.Length).Trim()))
            {
                throw new OutputFormatException("$", OutputFormatViolation.Malformed);
            }

            text = text.StripCodeFence().Trim();
        }

        if (text.Length == 0)
        {
            throw new OutputFormatException("$", OutputFormatViolation.Empty);
        }

        byte[] bytes;
        try
        {
            // Strict: throws on an unpaired surrogate instead of silently writing U+FFFD into the document.
            bytes = StrictEncoding.Utf8.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            // WHY no inner exception: its message quotes the offending character.
            throw new OutputFormatException("$", OutputFormatViolation.Malformed);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowDuplicateProperties = false, MaxDepth = MaxDepth });
        }
        catch (JsonException)
        {
            // WHY no inner exception: the parser's message can quote reply text (a duplicate property's name,
            // an invalid character).
            throw new OutputFormatException("$", IsTruncated(bytes) ? OutputFormatViolation.Truncated : OutputFormatViolation.Malformed);
        }
        catch (InvalidOperationException)
        {
            // The duplicate-property check decodes every property name, so a name escaping an unpaired
            // surrogate fails here rather than in EnsureStringsDecode; the reply is complete but not valid text.
            throw new OutputFormatException("$", OutputFormatViolation.Malformed);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            EnsureStringsDecode(root);
            validator.Validate(root);
            JsonElement owned = root.Clone();
            return owned.ValueKind switch
            {
                JsonValueKind.Object => JsonObject.Create(owned),
                JsonValueKind.Array => JsonArray.Create(owned),
                JsonValueKind.Null => null,
                _ => JsonValue.Create(owned),
            };
        }
    }

    // Empty, or a letter followed by letters, digits, '_', '-', '+' or '.', and not a JSON literal: a tag
    // such as json, JSON5 or c++ can never be the start of a JSON document.
    private static bool IsLanguageTag(ReadOnlySpan<char> info)
        => info.IsEmpty
           || (char.IsAsciiLetter(info[0])
               && !info.ContainsAnyExcept(LanguageTagCharacters)
               && info is not "true" and not "false" and not "null");

    // A reply the parser rejected is truncated when a non-final reader reads all of it without an error and
    // without finishing the top-level value: the text is a valid prefix of a document that never ended.
    private static bool IsTruncated(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, isFinalBlock: false, new JsonReaderState(new JsonReaderOptions { MaxDepth = MaxDepth }));
        bool complete = false;
        try
        {
            while (reader.Read())
            {
                complete |= reader.CurrentDepth == 0 && reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray
                    or JsonTokenType.String or JsonTokenType.Number or JsonTokenType.True or JsonTokenType.False or JsonTokenType.Null;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return !complete;
    }

    // JSON allows an escaped unpaired surrogate ("\uD800"); a .NET string cannot hold it, so reading the value
    // later would throw. Decoding every string and property name once rejects such a reply here instead.
    private static void EnsureStringsDecode(JsonElement root)
    {
        try
        {
            Decode(root);
        }
        catch (InvalidOperationException)
        {
            // WHY no inner exception: kept free of reply text like every other violation.
            throw new OutputFormatException("$", OutputFormatViolation.Malformed);
        }
    }

    private static void Decode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                _ = element.GetString();
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    _ = property.Name;
                    Decode(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Decode(item);
                }

                break;
        }
    }
}
