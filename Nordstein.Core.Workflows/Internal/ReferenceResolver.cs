using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// Builds a node's input: its configuration with every data reference replaced by the referenced value. Stateless
/// and thread-safe.
/// </summary>
internal sealed class ReferenceResolver
{
    private readonly ReferenceParser parser;

    public ReferenceResolver(ReferenceParser parser)
    {
        this.parser = parser;
    }

    /// <param name="config">The configuration.</param>
    /// <param name="lookup">The value a reference's root stands for: the trigger's output, a step's output by id, the loop item or index; null when absent.</param>
    public JsonElement Resolve(JsonElement config, Func<DataReference, JsonElement?> lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Write(writer, config, lookup);
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement? Find(DataReference reference, Func<DataReference, JsonElement?> lookup)
    {
        JsonElement? current = lookup(reference);
        foreach (string segment in reference.Path)
        {
            current = current switch
            {
                { ValueKind: JsonValueKind.Object } value when value.TryGetProperty(segment, out JsonElement property) => property,
                { ValueKind: JsonValueKind.Array } value when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int at) && at < value.GetArrayLength() => value[at],
                _ => null,
            };
        }

        return current;
    }

    private static string Text(JsonElement? value) => value switch
    {
        null => string.Empty,
        { ValueKind: JsonValueKind.String } text => text.GetString() ?? string.Empty,
        { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => string.Empty,
        { ValueKind: JsonValueKind.True } => "true",
        { ValueKind: JsonValueKind.False } => "false",
        { } other => other.GetRawText(),
    };

    private void Write(Utf8JsonWriter writer, JsonElement value, Func<DataReference, JsonElement?> lookup)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value, lookup);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Write(writer, item, lookup);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String when value.GetString() is { } text && text.Contains("{{", StringComparison.Ordinal):
                TemplateParse parse = parser.Parse(text);
                if (parse.IsWhole)
                {
                    if (Find(parse.References[0], lookup) is { ValueKind: not JsonValueKind.Undefined } found)
                    {
                        found.WriteTo(writer);
                    }
                    else
                    {
                        writer.WriteNullValue();
                    }
                }
                else
                {
                    writer.WriteStringValue(parser.Render(text, reference => Text(Find(reference, lookup))));
                }

                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
