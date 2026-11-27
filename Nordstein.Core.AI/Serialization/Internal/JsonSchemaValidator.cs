using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Nordstein.Core.AI.Serialization.Internal;

/// <summary>
/// Validates a JSON document against the subset of JSON Schema a strict output format checks:
/// <c>type</c> (one name or an array of names), <c>properties</c>, <c>required</c>, <c>items</c>
/// (one schema), <c>enum</c> and a boolean <c>additionalProperties</c>, recursively through nested
/// objects and arrays. The annotations <c>title</c>, <c>description</c>, <c>$comment</c>,
/// <c>$schema</c>, <c>default</c> and <c>examples</c> are accepted and not checked.
/// </summary>
/// <remarks>
/// Every other keyword (<c>$ref</c>, <c>$defs</c>, <c>anyOf</c>, <c>if</c>, <c>const</c>,
/// <c>pattern</c>, <c>format</c>, <c>minimum</c>, …) is rejected at construction, so a schema
/// never asks for a constraint the validator would silently skip. The schema is compiled once;
/// instances are immutable and <see cref="Validate(JsonElement)"/> is thread-safe.
/// </remarks>
internal sealed class JsonSchemaValidator
{
    private const string Supported =
        "type, properties, required, items, enum, a boolean additionalProperties, and the annotations title, description, $comment, $schema, default and examples";

    private readonly SchemaNode root;

    /// <summary>
    /// Compiles <paramref name="schema"/>.
    /// </summary>
    /// <param name="schema">
    /// The schema's root object. The validator keeps elements of it (the <c>enum</c> values), so the
    /// caller passes an element whose document outlives the validator, such as a
    /// <see cref="JsonElement.Clone"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The schema uses an unsupported keyword, a keyword with a malformed value, or a schema that is
    /// not a JSON object; an <c>enum</c> holds a number whose exponent is outside the 32-bit range or
    /// a value its schema's <c>type</c> excludes; or <c>required</c> names a property that
    /// <c>additionalProperties: false</c> forbids. The message names the keyword and its JSON
    /// Pointer in the schema; the parameter name is <paramref name="schema"/>.
    /// </exception>
    public JsonSchemaValidator(JsonElement schema)
    {
        root = Compile(schema, "#", nameof(schema));
    }

    [Flags]
    private enum JsonTypes
    {
        None = 0,
        Object = 1,
        Array = 2,
        String = 4,
        Number = 8,
        Integer = 16,
        Boolean = 32,
        Null = 64,
    }

    /// <summary>
    /// Validates <paramref name="instance"/>, the root of a reply, against the schema.
    /// </summary>
    /// <exception cref="OutputFormatException">
    /// The first violation found, depth first in schema order, with its JSON path.
    /// </exception>
    public void Validate(JsonElement instance) => Validate(instance, root, "$");

    private static void Validate(JsonElement instance, SchemaNode schema, string path)
    {
        if (schema.Types is not JsonTypes.None && !Allows(schema.Types, instance))
        {
            throw new OutputFormatException(path, OutputFormatViolation.WrongType);
        }

        if (schema.Enum is { } values && !values.Any(value => JsonEquals(value, instance)))
        {
            throw new OutputFormatException(path, OutputFormatViolation.NotInEnum);
        }

        switch (instance.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateObject(instance, schema, path);
                break;
            case JsonValueKind.Array when schema.Items is { } items:
                int index = 0;
                foreach (JsonElement item in instance.EnumerateArray())
                {
                    Validate(item, items, $"{path}[{index}]");
                    index++;
                }

                break;
        }
    }

    private static void ValidateObject(JsonElement instance, SchemaNode schema, string path)
    {
        foreach (string name in schema.Required)
        {
            if (!instance.TryGetProperty(name, out _))
            {
                throw new OutputFormatException(ChildPath(path, name), OutputFormatViolation.MissingRequired);
            }
        }

        if (!schema.AllowsAdditionalProperties
            && instance.EnumerateObject().Any(property => !schema.Properties.ContainsKey(property.Name)))
        {
            // The path is the object's: the undeclared name came from the reply, and the reply never
            // enters the exception.
            throw new OutputFormatException(path, OutputFormatViolation.UnexpectedProperty);
        }

        foreach ((string name, SchemaNode property) in schema.PropertyOrder)
        {
            if (instance.TryGetProperty(name, out JsonElement value))
            {
                Validate(value, property, ChildPath(path, name));
            }
        }
    }

    private static bool Allows(JsonTypes types, JsonElement instance) => instance.ValueKind switch
    {
        JsonValueKind.Object => types.HasFlag(JsonTypes.Object),
        JsonValueKind.Array => types.HasFlag(JsonTypes.Array),
        JsonValueKind.String => types.HasFlag(JsonTypes.String),
        JsonValueKind.Number => types.HasFlag(JsonTypes.Number)
                                || (types.HasFlag(JsonTypes.Integer) && IsInteger(instance.GetRawText())),
        JsonValueKind.True or JsonValueKind.False => types.HasFlag(JsonTypes.Boolean),
        JsonValueKind.Null => types.HasFlag(JsonTypes.Null),
        _ => false,
    };

    // JSON Schema's integer is any number with a zero fractional part, so 1.0 and 1e2 count. Decided on
    // the literal's digits, exactly: double and decimal both round some literals to a whole number.
    private static bool IsInteger(string number)
    {
        JsonNumber parts = Decompose(number);
        return parts.IsZero || (parts.Power is { } power ? power >= 0 : parts.HasPositiveExponent);
    }

    // Equality by JSON value, as enum asks: numbers by value (1 equals 1.0 and 10e-1), strings by their
    // decoded text, objects regardless of property order. Hand-written because JsonElement.DeepEquals
    // throws on a number whose exponent is outside the 32-bit range, and the reply chooses its numbers.
    private static bool JsonEquals(JsonElement expected, JsonElement actual) => (expected.ValueKind, actual.ValueKind) switch
    {
        (JsonValueKind.Number, JsonValueKind.Number) => NumbersEqual(expected.GetRawText(), actual.GetRawText()),
        (JsonValueKind.String, JsonValueKind.String) => string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal),
        (JsonValueKind.Object, JsonValueKind.Object) =>
            expected.GetPropertyCount() == actual.GetPropertyCount()
            && expected.EnumerateObject().All(property => actual.TryGetProperty(property.Name, out JsonElement other) && JsonEquals(property.Value, other)),
        (JsonValueKind.Array, JsonValueKind.Array) =>
            expected.GetArrayLength() == actual.GetArrayLength()
            && expected.EnumerateArray().Zip(actual.EnumerateArray()).All(pair => JsonEquals(pair.First, pair.Second)),
        var (left, right) => left == right && left is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null,
    };

    private static bool NumbersEqual(string expected, string actual)
    {
        JsonNumber left = Decompose(expected);
        JsonNumber right = Decompose(actual);
        if (left.IsZero || right.IsZero)
        {
            return left.IsZero && right.IsZero;
        }

        // A power of null (an exponent beyond long) never equals an enum number: the constructor keeps
        // enum exponents within int.
        return left.IsNegative == right.IsNegative
               && string.Equals(left.Significant, right.Significant, StringComparison.Ordinal)
               && left.Power is { } leftPower
               && right.Power is { } rightPower
               && leftPower == rightPower;
    }

    // A JSON number literal as sign x Significant x 10^Power, Significant without leading or trailing zeros
    // (empty for zero). Power is null when the literal's exponent does not fit a long; HasPositiveExponent
    // then tells its direction. The literal is valid JSON, already checked by the parser.
    private static JsonNumber Decompose(string number)
    {
        bool isNegative = number.StartsWith('-');
        ReadOnlySpan<char> text = number.AsSpan(isNegative ? 1 : 0);
        int exponentAt = text.IndexOfAny('e', 'E');
        ReadOnlySpan<char> mantissa = exponentAt < 0 ? text : text[..exponentAt];
        int dotAt = mantissa.IndexOf('.');
        ReadOnlySpan<char> fraction = dotAt < 0 ? [] : mantissa[(dotAt + 1)..];
        string digits = string.Concat(dotAt < 0 ? mantissa : mantissa[..dotAt], fraction);
        string withoutTrailingZeros = digits.TrimEnd('0');
        string significant = withoutTrailingZeros.TrimStart('0');
        ReadOnlySpan<char> exponentText = exponentAt < 0 ? "0" : text[(exponentAt + 1)..];
        int trailingZeros = digits.Length - withoutTrailingZeros.Length;
        Int128? power = long.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long exponent)
            ? (Int128)exponent - fraction.Length + trailingZeros
            : null;
        return new JsonNumber(isNegative, significant, power, !exponentText.StartsWith('-'));
    }

    private static string ChildPath(string path, string name)
        => IsIdentifier(name) ? $"{path}.{name}" : $"{path}['{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";

    private static bool IsIdentifier(string name)
        => name.Length > 0
           && (char.IsAsciiLetter(name[0]) || name[0] == '_')
           && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static SchemaNode Compile(JsonElement schema, string pointer, string parameter)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw Unsupported(parameter, $"The schema at {pointer} is not a JSON object; only object schemas are supported.");
        }

        JsonTypes types = JsonTypes.None;
        IReadOnlyList<JsonElement>? values = null;
        Dictionary<string, SchemaNode> properties = new(StringComparer.Ordinal);
        List<KeyValuePair<string, SchemaNode>> propertyOrder = [];
        List<string> required = [];
        SchemaNode? items = null;
        bool allowsAdditionalProperties = true;
        foreach (JsonProperty keyword in schema.EnumerateObject())
        {
            string at = $"{pointer}/{Escape(keyword.Name)}";
            JsonElement value = keyword.Value;
            switch (keyword.Name)
            {
                case "type":
                    types = CompileTypes(value, at, parameter);
                    break;
                case "properties" when value.ValueKind == JsonValueKind.Object:
                    foreach (JsonProperty property in value.EnumerateObject())
                    {
                        SchemaNode node = Compile(property.Value, $"{at}/{Escape(property.Name)}", parameter);
                        properties.Add(property.Name, node);
                        propertyOrder.Add(KeyValuePair.Create(property.Name, node));
                    }

                    break;
                case "required" when value.ValueKind == JsonValueKind.Array
                                     && value.EnumerateArray().All(name => name.ValueKind == JsonValueKind.String):
                    required.AddRange(value.EnumerateArray().Select(name => name.GetString() ?? string.Empty));
                    break;
                case "items" when value.ValueKind == JsonValueKind.Object:
                    items = Compile(value, at, parameter);
                    break;
                case "enum" when value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0:
                    values = [.. value.EnumerateArray()];
                    if (!values.All(HasSmallExponents))
                    {
                        throw Unsupported(parameter, $"The schema keyword 'enum' at {at} holds a number whose exponent is outside the 32-bit range.");
                    }

                    break;
                case "additionalProperties" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    allowsAdditionalProperties = value.GetBoolean();
                    break;
                case "properties" or "required" or "items" or "enum" or "additionalProperties":
                    throw Unsupported(parameter, $"The schema keyword '{keyword.Name}' at {at} has a value of a form that is not supported: {Expected(keyword.Name)}.");
                case "title" or "description" or "$comment" or "$schema" or "default" or "examples":
                    break;
                default:
                    throw Unsupported(parameter, $"The schema keyword '{keyword.Name}' at {at} is not supported; supported are {Supported}.");
            }
        }

        if (types is not JsonTypes.None && values is not null && !values.All(value => Allows(types, value)))
        {
            throw Unsupported(parameter, $"The schema keyword 'enum' at {pointer}/enum holds a value its schema's type excludes, so that value could never match.");
        }

        if (!allowsAdditionalProperties && required.FirstOrDefault(name => !properties.ContainsKey(name)) is { } undeclared)
        {
            throw Unsupported(parameter, $"The schema keyword 'required' at {pointer}/required names the property '{undeclared}', which additionalProperties: false forbids because properties does not declare it.");
        }

        return new SchemaNode(types, values, properties, propertyOrder, required, items, allowsAdditionalProperties);
    }

    private static JsonTypes CompileTypes(JsonElement value, string at, string parameter)
    {
        IReadOnlyList<JsonElement> names = value.ValueKind switch
        {
            JsonValueKind.String => [value],
            JsonValueKind.Array when value.GetArrayLength() > 0 => [.. value.EnumerateArray()],
            _ => throw Unsupported(parameter, $"The schema keyword 'type' at {at} has a value of a form that is not supported: {Expected("type")}."),
        };

        JsonTypes types = JsonTypes.None;
        foreach (JsonElement name in names)
        {
            JsonTypes type = name.ValueKind == JsonValueKind.String ? Parse(name.GetString()) : JsonTypes.None;
            if (type is JsonTypes.None || types.HasFlag(type))
            {
                throw Unsupported(parameter, $"The schema keyword 'type' at {at} has a value of a form that is not supported: {Expected("type")}.");
            }

            types |= type;
        }

        return types;
    }

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

    private static string Expected(string keyword) => keyword switch
    {
        "type" => "one of object, array, string, number, integer, boolean, null, or a non-empty array of distinct ones",
        "properties" => "an object whose values are object schemas",
        "required" => "an array of strings",
        "items" => "one object schema",
        "enum" => "a non-empty array",
        "additionalProperties" => "true or false",
        _ => throw new ArgumentOutOfRangeException(nameof(keyword), keyword, "Not a checked keyword."),
    };

    // RFC 6901: '~' becomes '~0' and '/' becomes '~1'.
    private static string Escape(string name) => new StringBuilder(name).Replace("~", "~0").Replace("/", "~1").ToString();

    // Exponents within int keep every enum number comparable with any reply number (see NumbersEqual).
    private static bool HasSmallExponents(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => ExponentFitsInt(value.GetRawText()),
        JsonValueKind.Object => value.EnumerateObject().All(property => HasSmallExponents(property.Value)),
        JsonValueKind.Array => value.EnumerateArray().All(HasSmallExponents),
        _ => true,
    };

    private static bool ExponentFitsInt(string number)
    {
        int exponentAt = number.AsSpan().IndexOfAny('e', 'E');
        return exponentAt < 0
               || int.TryParse(number.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
    }

    private static ArgumentException Unsupported(string parameter, string message) => new(message, parameter);

    private readonly record struct JsonNumber(bool IsNegative, string Significant, Int128? Power, bool HasPositiveExponent)
    {
        public bool IsZero => Significant.Length == 0;
    }

    private sealed record SchemaNode(
        JsonTypes Types,
        IReadOnlyList<JsonElement>? Enum,
        IReadOnlyDictionary<string, SchemaNode> Properties,
        IReadOnlyList<KeyValuePair<string, SchemaNode>> PropertyOrder,
        IReadOnlyList<string> Required,
        SchemaNode? Items,
        bool AllowsAdditionalProperties);
}
