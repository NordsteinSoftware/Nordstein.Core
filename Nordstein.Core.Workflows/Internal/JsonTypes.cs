using System.Globalization;
using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// The JSON Schema type names as flags.
/// </summary>
[Flags]
internal enum JsonTypes
{
    None = 0,
    Object = 1,
    Array = 2,
    String = 4,
    Number = 8,
    Integer = 16,
    Boolean = 32,
    Null = 64,
    Scalar = String | Number | Integer | Boolean | Null,
}

/// <summary>
/// Reads the keywords of the kernel's JSON Schema subset. Stateless and thread-safe.
/// </summary>
internal sealed class SchemaReader
{
    /// <summary>
    /// The types <paramref name="schema"/> allows; <see cref="JsonTypes.None"/> when it names none (any type).
    /// </summary>
    public JsonTypes TypesOf(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out JsonElement type))
        {
            return JsonTypes.None;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => Parse(type.GetString()),
            JsonValueKind.Array => type.EnumerateArray().Aggregate(JsonTypes.None, (all, item) => all | Parse(item.ValueKind == JsonValueKind.String ? item.GetString() : null)),
            _ => JsonTypes.None,
        };
    }

    /// <summary>
    /// The schema of <paramref name="segment"/> under <paramref name="schema"/>: a declared property, an array's
    /// <c>items</c> for an index, or any value (null) when the schema describes neither properties nor items.
    /// <c>false</c> when the schema rules the segment out: a scalar type, or declared properties or items that do not
    /// cover it.
    /// </summary>
    public bool TryChild(JsonElement schema, string segment, out JsonElement? child)
    {
        child = null;
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        JsonTypes types = TypesOf(schema);
        bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object;
        bool hasItems = schema.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Object;
        if (hasProperties && properties.TryGetProperty(segment, out JsonElement property))
        {
            child = property;
            return true;
        }

        if (hasItems && IsIndex(segment))
        {
            child = items;
            return true;
        }

        if (types is not JsonTypes.None && (types & (JsonTypes.Object | JsonTypes.Array)) == JsonTypes.None)
        {
            return false;
        }

        // A schema that declares its properties or items describes its children; one that does not allows any.
        return !hasProperties && !hasItems;
    }

    /// <summary>
    /// Whether a value of <paramref name="actual"/> types fits where <paramref name="expected"/> types are allowed;
    /// either being <see cref="JsonTypes.None"/> (any) fits. <c>null</c> is ignored on the actual side: an absent
    /// optional value resolves to <c>null</c> anyway. An integer fits a number.
    /// </summary>
    public bool Fits(JsonTypes actual, JsonTypes expected)
    {
        JsonTypes values = actual & ~JsonTypes.Null;
        if (values is JsonTypes.None || expected is JsonTypes.None)
        {
            return true;
        }

        JsonTypes accepted = expected.HasFlag(JsonTypes.Number) ? expected | JsonTypes.Integer : expected;
        return (values & ~accepted) == JsonTypes.None;
    }

    /// <summary>
    /// Whether <paramref name="value"/> is of one of <paramref name="types"/>; <see cref="JsonTypes.None"/> allows any.
    /// </summary>
    public bool Allows(JsonTypes types, JsonElement value) => types is JsonTypes.None || value.ValueKind switch
    {
        JsonValueKind.Object => types.HasFlag(JsonTypes.Object),
        JsonValueKind.Array => types.HasFlag(JsonTypes.Array),
        JsonValueKind.String => types.HasFlag(JsonTypes.String),
        JsonValueKind.Number => types.HasFlag(JsonTypes.Number) || (types.HasFlag(JsonTypes.Integer) && IsInteger(value)),
        JsonValueKind.True or JsonValueKind.False => types.HasFlag(JsonTypes.Boolean),
        JsonValueKind.Null => types.HasFlag(JsonTypes.Null),
        _ => false,
    };

    public string Describe(JsonTypes types) =>
        types is JsonTypes.None
            ? "any"
            : string.Join(" or ", Enum.GetValues<JsonTypes>()
                .Where(flag => flag is not JsonTypes.None and not JsonTypes.Scalar && types.HasFlag(flag))
                .Select(flag => flag.ToString().ToLowerInvariant()));

    private static bool IsIndex(string segment) =>
        segment.Length > 0 && segment.All(char.IsAsciiDigit) && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsInteger(JsonElement value) =>
        value.TryGetDecimal(out decimal number)
            ? decimal.Truncate(number) == number
            : value.GetRawText().IndexOfAny(['.', 'e', 'E']) < 0;

    private static JsonTypes Parse(string? name) => name switch
    {
        "object" => JsonTypes.Object,
        "array" => JsonTypes.Array,
        "string" => JsonTypes.String,
        "number" => JsonTypes.Number,
        "integer" => JsonTypes.Integer,
        "boolean" => JsonTypes.Boolean,
        "null" => JsonTypes.Null,
        _ => JsonTypes.None,
    };
}
