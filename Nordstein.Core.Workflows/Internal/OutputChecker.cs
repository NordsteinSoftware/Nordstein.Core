using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <summary>
/// Checks a node's output against its type's output schema (the kernel subset: <c>type</c>, <c>enum</c>,
/// <c>properties</c>, <c>required</c>, boolean <c>additionalProperties</c>, <c>items</c>). Unlike
/// <see cref="ConfigChecker"/> it knows no references: a string is a string. Stateless and thread-safe.
/// </summary>
internal sealed class OutputChecker
{
    private readonly SchemaReader schemas;

    public OutputChecker(SchemaReader schemas)
    {
        this.schemas = schemas;
    }

    /// <summary>
    /// Every place where <paramref name="output"/> does not fit <paramref name="schema"/>, in document order.
    /// </summary>
    public IReadOnlyList<OutputProblem> Check(JsonElement output, JsonElement schema)
    {
        List<OutputProblem> problems = [];
        Check(output, schema, "$", problems);
        return problems;
    }

    private void Check(JsonElement value, JsonElement schema, string path, List<OutputProblem> problems)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        JsonTypes types = schemas.TypesOf(schema);
        if (!schemas.Allows(types, value))
        {
            problems.Add(new OutputProblem(path, $"{path} must be {schemas.Describe(types)}."));
            return;
        }

        if (schema.TryGetProperty("enum", out JsonElement allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(item => JsonElement.DeepEquals(item, value)))
        {
            problems.Add(new OutputProblem(path, $"{path} must be one of {allowed.GetRawText()}."));
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(value, schema, path, problems);
                break;
            case JsonValueKind.Array when schema.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Object:
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    Check(item, items, $"{path}[{index}]", problems);
                    index++;
                }

                break;
        }
    }

    private void CheckObject(JsonElement value, JsonElement schema, string path, List<OutputProblem> problems)
    {
        if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement name in required.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String && name.GetString() is { } property && !value.TryGetProperty(property, out _))
                {
                    problems.Add(new OutputProblem($"{path}.{property}", $"{path}.{property} is required."));
                }
            }
        }

        JsonElement? properties = schema.TryGetProperty("properties", out JsonElement declared) && declared.ValueKind == JsonValueKind.Object ? declared : null;
        bool closed = schema.TryGetProperty("additionalProperties", out JsonElement additional) && additional.ValueKind == JsonValueKind.False;
        foreach (JsonProperty property in value.EnumerateObject())
        {
            string at = $"{path}.{property.Name}";
            if (properties is { } known && known.TryGetProperty(property.Name, out JsonElement propertySchema))
            {
                Check(property.Value, propertySchema, at, problems);
            }
            else if (closed)
            {
                problems.Add(new OutputProblem(at, $"{at} is not allowed."));
            }
        }
    }
}
