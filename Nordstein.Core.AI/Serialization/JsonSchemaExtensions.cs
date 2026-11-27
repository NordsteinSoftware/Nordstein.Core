using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nordstein.Core.AI.Serialization;

/// <summary>
/// Rewrites a JSON Schema for a model provider that decodes strictly against it.
/// </summary>
public static class JsonSchemaExtensions
{
    // System.Text.Json's default nesting limit, so a schema that parses also closes.
    private const int MaxDepth = 64;

    /// <summary>
    /// Returns a copy of <paramref name="schema"/> in which every object schema is closed: its
    /// <c>additionalProperties</c> is <see langword="false"/> and its <c>required</c> lists every property it declares,
    /// the order of an existing <c>required</c> kept and the missing names appended in declaration order. Opt-in: the
    /// schema-driven output format (<see cref="JsonSchemaOutputFormat"/>) and <see cref="Clients.JsonSchemaResponseFormat"/>
    /// send a schema as given, and a caller that needs a strict provider's dialect passes this copy instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An object schema is one with <c>properties</c> or whose <c>type</c> names <c>object</c> (alone or in an array, such
    /// as <c>["object","null"]</c>). One without <c>properties</c> is closed too and then accepts only <c>{}</c>. A property
    /// the schema left optional becomes required: to keep its absence expressible, let its schema accept <c>null</c> before
    /// closing. <c>additionalProperties: true</c> becomes <see langword="false"/>.
    /// </para>
    /// <para>
    /// Every subschema position is closed in turn: <c>properties</c>, <c>patternProperties</c>, <c>$defs</c>,
    /// <c>definitions</c> and <c>dependentSchemas</c> (each value), <c>anyOf</c>, <c>oneOf</c>, <c>allOf</c> and
    /// <c>prefixItems</c> (each item), <c>items</c> (one schema or an array of them), <c>not</c>, <c>if</c>, <c>then</c>,
    /// <c>else</c>, <c>contains</c>, <c>additionalItems</c>, <c>unevaluatedItems</c> and <c>propertyNames</c>. A
    /// <c>$ref</c> is not followed: the definitions it points into are closed where they stand. Values that are data rather
    /// than schemas (<c>enum</c>, <c>const</c>, <c>default</c>, <c>examples</c>, annotations) and boolean schemas are copied
    /// unchanged. Closing a closed schema returns an equal schema.
    /// </para>
    /// <para>
    /// The input is never changed and the copy has no parent. The method holds no state; concurrent calls on one input are
    /// as safe as concurrent reads of that <see cref="JsonNode"/>, which System.Text.Json does not promise for a node it
    /// materializes lazily, so give each thread its own node.
    /// </para>
    /// </remarks>
    /// <param name="schema">The JSON Schema: a JSON object or a boolean.</param>
    /// <returns>The closed copy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="schema"/> is neither an object nor a boolean, nests deeper than 64 levels, or cannot be closed
    /// without changing what it accepts or without contradicting itself: an <c>additionalProperties</c> that is a schema (a
    /// map would become an empty object), a <c>required</c> that is not an array of names or names a property the object
    /// does not declare, a subschema keyword of the wrong form. The message names the keyword's JSON Pointer.
    /// </exception>
    public static JsonNode WithObjectsClosed(this JsonNode schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.GetValueKind() is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
        {
            throw new ArgumentException("A JSON schema is a JSON object or a boolean.", nameof(schema));
        }

        EnsureDepth(schema);
        JsonNode copy = schema.DeepClone();
        Close(copy, string.Empty);
        return copy;
    }

    // Iterative, so a schema too deep to close never overflows the stack while it is measured.
    private static void EnsureDepth(JsonNode schema)
    {
        Stack<(JsonNode Node, int Depth)> pending = new([(schema, 1)]);
        while (pending.TryPop(out (JsonNode Node, int Depth) entry))
        {
            if (entry.Depth > MaxDepth)
            {
                throw new ArgumentException($"The schema nests deeper than {MaxDepth.ToString(CultureInfo.InvariantCulture)} levels.", nameof(schema));
            }

            IEnumerable<JsonNode?> children = entry.Node switch
            {
                JsonObject members => members.Select(member => member.Value),
                JsonArray items => items,
                _ => [],
            };
            foreach (JsonNode? child in children)
            {
                if (child is JsonObject or JsonArray)
                {
                    pending.Push((child, entry.Depth + 1));
                }
            }
        }
    }

    private static void Close(JsonNode? node, string pointer)
    {
        // A boolean schema, or anything but an object at a schema position, is copied as it is.
        if (node is not JsonObject schema)
        {
            return;
        }

        foreach (KeyValuePair<string, JsonNode?> keyword in schema)
        {
            string at = $"{pointer}/{Escape(keyword.Key)}";
            switch (keyword.Key)
            {
                case "properties" or "patternProperties" or "$defs" or "definitions" or "dependentSchemas":
                    if (keyword.Value is not JsonObject map)
                    {
                        throw Refused(at, "is not an object of schemas");
                    }

                    foreach (KeyValuePair<string, JsonNode?> entry in map)
                    {
                        Close(entry.Value, $"{at}/{Escape(entry.Key)}");
                    }

                    break;
                case "anyOf" or "oneOf" or "allOf" or "prefixItems":
                    if (keyword.Value is not JsonArray list)
                    {
                        throw Refused(at, "is not an array of schemas");
                    }

                    CloseEach(list, at);
                    break;
                case "items" when keyword.Value is JsonArray tuple:
                    CloseEach(tuple, at);
                    break;
                case "items" or "not" or "if" or "then" or "else" or "contains" or "additionalItems" or "unevaluatedItems" or "propertyNames":
                    Close(keyword.Value, at);
                    break;
            }
        }

        if (schema.ContainsKey("properties") || NamesObject(schema["type"]))
        {
            CloseObject(schema, pointer);
        }
    }

    private static void CloseEach(JsonArray list, string pointer)
    {
        for (int index = 0; index < list.Count; index++)
        {
            Close(list[index], $"{pointer}/{index.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    private static void CloseObject(JsonObject schema, string pointer)
    {
        if (!schema.TryGetPropertyValue("additionalProperties", out JsonNode? additional))
        {
            schema["additionalProperties"] = false;
        }
        else if (additional is null || additional.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Refused(
                $"{pointer}/additionalProperties",
                "is not a boolean; closing an object whose additional properties follow a schema would leave them no property to take, so close it by hand or drop it");
        }
        else if (additional.GetValueKind() == JsonValueKind.True)
        {
            schema["additionalProperties"] = false;
        }

        // The keyword loop already refused a properties that is not an object.
        List<string> declared = schema["properties"] is JsonObject properties ? [.. properties.Select(property => property.Key)] : [];
        List<string> required = [];
        if (schema.TryGetPropertyValue("required", out JsonNode? listed))
        {
            string at = $"{pointer}/required";
            if (listed is not JsonArray names)
            {
                throw Refused(at, "is not an array of property names");
            }

            foreach (JsonNode? name in names)
            {
                if (name is null || name.GetValueKind() != JsonValueKind.String)
                {
                    throw Refused(at, "is not an array of property names");
                }

                string text = name.GetValue<string>();
                if (!declared.Contains(text, StringComparer.Ordinal))
                {
                    throw Refused(at, $"names '{text}', which the object does not declare; once closed, no value could have it");
                }

                required.Add(text);
            }
        }

        if (schema.ContainsKey("properties"))
        {
            required.AddRange(declared.Where(name => !required.Contains(name, StringComparer.Ordinal)));
            schema["required"] = new JsonArray([.. required.Select(name => (JsonNode?)JsonValue.Create(name))]);
        }
    }

    private static bool NamesObject(JsonNode? type) => type switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>() == "object",
        JsonArray names => names.Any(name => name is JsonValue item && item.GetValueKind() == JsonValueKind.String && item.GetValue<string>() == "object"),
        _ => false,
    };

    // RFC 6901: '~' and '/' are escaped in a JSON Pointer's tokens.
    private static string Escape(string token) => token.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static ArgumentException Refused(string pointer, string problem) =>
        new($"The schema's '{pointer}' {problem}.", "schema");
}
