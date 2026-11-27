using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// A data reference found in a configuration, with what its place expects.
/// </summary>
/// <param name="Reference">The reference.</param>
/// <param name="Location">Its place in the configuration, such as <c>config.all[0].left</c>.</param>
/// <param name="Expected">The types the input schema allows there; <see cref="JsonTypes.None"/> for any.</param>
/// <param name="InText">Whether the reference sits inside longer text, so its value must be a scalar.</param>
internal sealed record ReferenceSite(DataReference Reference, string Location, JsonTypes Expected, bool InText);

/// <summary>
/// Checks a node's configuration against its type's input schema (the kernel subset: <c>type</c>, <c>enum</c>,
/// <c>properties</c>, <c>required</c>, boolean <c>additionalProperties</c>, <c>items</c>) and collects its data
/// references. A string that is exactly one reference stands for a value of the type its place expects, so it is
/// not checked as a string; a schema validator that knows no references would reject it. Stateless and thread-safe.
/// </summary>
internal sealed class ConfigChecker
{
    private readonly ReferenceParser parser;
    private readonly SchemaReader schemas;

    public ConfigChecker(ReferenceParser parser, SchemaReader schemas)
    {
        this.parser = parser;
        this.schemas = schemas;
    }

    public void Check(JsonElement config, JsonElement schema, List<string> errors, List<ReferenceSite> sites)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(sites);
        Check(config, schema, "config", errors, sites);
    }

    private void Check(JsonElement value, JsonElement? schema, string location, List<string> errors, List<ReferenceSite> sites)
    {
        JsonTypes types = schema is { } known ? schemas.TypesOf(known) : JsonTypes.None;
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && (text.Contains("{{", StringComparison.Ordinal) || text.Contains("}}", StringComparison.Ordinal)))
        {
            CheckText(text, types, location, errors, sites);
            return;
        }

        if (!schemas.Allows(types, value))
        {
            errors.Add($"{location} must be {schemas.Describe(types)}.");
            return;
        }

        if (schema is not { ValueKind: JsonValueKind.Object } node)
        {
            CollectAnywhere(value, location, errors, sites);
            return;
        }

        if (node.TryGetProperty("enum", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(item => JsonElement.DeepEquals(item, value)))
        {
            errors.Add($"{location} must be one of {allowed.GetRawText()}.");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(value, node, location, errors, sites);
                break;
            case JsonValueKind.Array:
                JsonElement? items = node.TryGetProperty("items", out JsonElement itemSchema) && itemSchema.ValueKind == JsonValueKind.Object ? itemSchema : null;
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Check(item, items, $"{location}[{index}]", errors, sites);
                    index++;
                }

                break;
        }
    }

    private void CheckObject(JsonElement value, JsonElement schema, string location, List<string> errors, List<ReferenceSite> sites)
    {
        if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement name in required.EnumerateArray())
            {
                if (name.GetString() is { } property && !value.TryGetProperty(property, out _))
                {
                    errors.Add($"{location}.{property} is required.");
                }
            }
        }

        JsonElement? properties = schema.TryGetProperty("properties", out JsonElement declared) && declared.ValueKind == JsonValueKind.Object ? declared : null;
        bool closed = schema.TryGetProperty("additionalProperties", out JsonElement additional) && additional.ValueKind == JsonValueKind.False;
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string at = $"{location}.{property.Name}";
            if (properties is { } known && known.TryGetProperty(property.Name, out JsonElement propertySchema))
            {
                Check(property.Value, propertySchema, at, errors, sites);
            }
            else if (closed)
            {
                errors.Add($"{at} is not allowed.");
            }
            else
            {
                Check(property.Value, null, at, errors, sites);
            }
        }
    }

    private void CollectAnywhere(JsonElement value, string location, List<string> errors, List<ReferenceSite> sites)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    Check(property.Value, null, $"{location}.{property.Name}", errors, sites);
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Check(item, null, $"{location}[{index}]", errors, sites);
                    index++;
                }

                break;
        }
    }

    private void CheckText(string text, JsonTypes types, string location, List<string> errors, List<ReferenceSite> sites)
    {
        TemplateParse parse = parser.Parse(text);
        if (parse.IsMalformed)
        {
            errors.Add($"{location} has a '{{{{' or '}}}}' that is not a reference of the form {{{{trigger.<path>}}}}, {{{{steps.<nodeId>.output.<path>}}}}, {{{{item.<path>}}}} or {{{{index}}}}.");
            return;
        }

        if (parse.IsWhole)
        {
            sites.Add(new ReferenceSite(parse.References[0], location, types, InText: false));
            return;
        }

        if (types is not JsonTypes.None && !types.HasFlag(JsonTypes.String))
        {
            errors.Add($"{location} must be {schemas.Describe(types)}, not text with references.");
            return;
        }

        foreach (DataReference reference in parse.References)
        {
            sites.Add(new ReferenceSite(reference, location, JsonTypes.Scalar, InText: true));
        }
    }
}
