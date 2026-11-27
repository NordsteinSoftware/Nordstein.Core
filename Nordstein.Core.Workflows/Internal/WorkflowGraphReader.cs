using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nordstein.Core.Workflows.Internal;

/// <inheritdoc />
internal sealed class WorkflowGraphReader : IWorkflowGraphReader
{
    // System.Text.Json's default, stated so the text's parse and an element's walk hold the same limit.
    private const int MaxDepth = 64;

    // No duration the format accepts is longer; a longer text is refused before the pattern runs.
    private const int MaxDurationLength = 64;

    private const string Attempts = "a whole number from 2 to 2147483647";
    private const string Duration = "an ISO 8601 duration of days, hours, minutes and seconds such as PT5S or P1DT12H, at most 10675199 days";

    private readonly Regex duration = new(
        "^P(?:(?<days>[0-9]+)D)?(?:T(?=[0-9])(?:(?<hours>[0-9]+)H)?(?:(?<minutes>[0-9]+)M)?(?:(?<seconds>[0-9]+)(?:\\.(?<fraction>[0-9]{1,7}))?S)?)?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private readonly JsonElement emptyConfig = Parse("{}");

    public WorkflowGraph Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException exception)
        {
            string line = ((exception.LineNumber ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
            string position = ((exception.BytePositionInLine ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
            throw new WorkflowGraphFormatException(
                [new WorkflowGraphFormatError(exception.Path ?? "$", $"The text is not JSON (line {line}, byte {position}): {ParserMessage(exception)}")],
                exception);
        }

        using (document)
        {
            return Read(document.RootElement);
        }
    }

    public WorkflowGraph Read(JsonElement graph)
    {
        if (graph.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("The graph is a defined JSON element.", nameof(graph));
        }

        List<WorkflowGraphFormatError> errors = [];
        if (graph.ValueKind != JsonValueKind.Object)
        {
            throw new WorkflowGraphFormatException([new WorkflowGraphFormatError("$", $"The graph is a JSON object, not {Kind(graph)}.")]);
        }

        Dictionary<string, JsonElement> root = Members(graph, "$", errors);
        List<NodeDefinition> nodes = [];
        if (!root.TryGetValue("nodes", out JsonElement nodeArray))
        {
            errors.Add(new WorkflowGraphFormatError("$.nodes", "$.nodes is missing; it is an array of nodes."));
        }
        else if (nodeArray.ValueKind != JsonValueKind.Array)
        {
            errors.Add(new WorkflowGraphFormatError("$.nodes", $"$.nodes is {Kind(nodeArray)}, not an array of nodes."));
        }
        else
        {
            int index = 0;
            foreach (JsonElement node in nodeArray.EnumerateArray())
            {
                if (Node(node, $"$.nodes[{index.ToString(CultureInfo.InvariantCulture)}]", errors) is { } read)
                {
                    nodes.Add(read);
                }

                index++;
            }
        }

        List<EdgeDefinition> edges = [];
        if (root.TryGetValue("edges", out JsonElement edgeArray))
        {
            if (edgeArray.ValueKind != JsonValueKind.Array)
            {
                errors.Add(new WorkflowGraphFormatError("$.edges", $"$.edges is {Kind(edgeArray)}, not an array of edges."));
            }
            else
            {
                int index = 0;
                foreach (JsonElement edge in edgeArray.EnumerateArray())
                {
                    if (Edge(edge, $"$.edges[{index.ToString(CultureInfo.InvariantCulture)}]", errors) is { } read)
                    {
                        edges.Add(read);
                    }

                    index++;
                }
            }
        }

        return errors.Count > 0 ? throw new WorkflowGraphFormatException(errors) : new WorkflowGraph(nodes, edges);
    }

    private static Dictionary<string, JsonElement> Members(JsonElement value, string path, List<WorkflowGraphFormatError> errors)
    {
        Dictionary<string, JsonElement> members = new(StringComparer.Ordinal);
        HashSet<string> reported = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            // A name that does not decode is no key the format defines, so it is one the reader ignores.
            if (TryName(property, out string? name) && !members.TryAdd(name, property.Value) && reported.Add(name))
            {
                errors.Add(new WorkflowGraphFormatError(path, $"{path} names the property '{name}' twice."));
            }
        }

        return members;
    }

    private static string? RequiredString(Dictionary<string, JsonElement> members, string key, string path, List<WorkflowGraphFormatError> errors)
    {
        string at = $"{path}.{key}";
        if (!members.TryGetValue(key, out JsonElement value))
        {
            errors.Add(new WorkflowGraphFormatError(at, $"{at} is missing; it is a string."));
            return null;
        }

        return String(value, at, errors);
    }

    private static string? String(JsonElement value, string path, List<WorkflowGraphFormatError> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new WorkflowGraphFormatError(path, $"{path} is {Kind(value)}, not a string."));
            return null;
        }

        if (!TryText(value, out string? text))
        {
            errors.Add(Undecodable(path));
            return null;
        }

        return text;
    }

    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        _ => "null",
    };

    // A property name or string that escapes an unpaired surrogate parses as JSON but decodes to no .NET string.
    private static bool TryName(JsonProperty property, [NotNullWhen(true)] out string? name)
    {
        try
        {
            name = property.Name;
            return true;
        }
        catch (InvalidOperationException)
        {
            name = null;
            return false;
        }
    }

    private static bool TryText(JsonElement value, [NotNullWhen(true)] out string? text)
    {
        try
        {
            // ToString of a string element is GetString, never null; both throw for text that does not decode.
            text = value.ToString();
            return true;
        }
        catch (InvalidOperationException)
        {
            text = null;
            return false;
        }
    }

    private static WorkflowGraphFormatError Undecodable(string path) => new(path, $"{path} holds an escaped character that is not valid text.");

    // The path of a property: dotted for a plain name, bracketed and quoted for any other.
    private static string Child(string path, string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? $"{path}.{name}"
            : $"{path}['{name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)}']";

    // The configuration is the node type's: only its names and strings must decode, a name appear once per object, and
    // the nesting stay within the limit, so the validator and the interpreter never meet a value they cannot read.
    private static void CheckConfig(JsonElement value, string path, int depth, List<WorkflowGraphFormatError> errors)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object or JsonValueKind.Array when depth > MaxDepth:
                errors.Add(new WorkflowGraphFormatError(path, $"{path} nests deeper than {MaxDepth.ToString(CultureInfo.InvariantCulture)} levels."));
                break;
            case JsonValueKind.Object:
                HashSet<string> names = new(StringComparer.Ordinal);
                HashSet<string> reported = new(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!TryName(property, out string? name))
                    {
                        errors.Add(Undecodable(path));
                        continue;
                    }

                    if (!names.Add(name))
                    {
                        if (reported.Add(name))
                        {
                            errors.Add(new WorkflowGraphFormatError(path, $"{path} names the property '{name}' twice."));
                        }

                        continue;
                    }

                    CheckConfig(property.Value, Child(path, name), depth + 1, errors);
                }

                break;
            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in value.EnumerateArray())
                {
                    CheckConfig(item, $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]", depth + 1, errors);
                    index++;
                }

                break;
            case JsonValueKind.String when !TryText(value, out _):
                errors.Add(Undecodable(path));
                break;
        }
    }

    // The first sentence of the parser's message, without the position it appends (reported separately).
    private static string ParserMessage(JsonException exception)
    {
        string message = exception.Message;
        int position = message.IndexOf(" LineNumber: ", StringComparison.Ordinal);
        return (position < 0 ? message : message[..position]).Trim();
    }

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private NodeDefinition? Node(JsonElement node, string path, List<WorkflowGraphFormatError> errors)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new WorkflowGraphFormatError(path, $"{path} is {Kind(node)}, not a node object."));
            return null;
        }

        int before = errors.Count;
        Dictionary<string, JsonElement> members = Members(node, path, errors);
        string? id = RequiredString(members, "id", path, errors);
        string? type = RequiredString(members, "type", path, errors);
        JsonElement config = emptyConfig;
        if (members.TryGetValue("config", out JsonElement given))
        {
            // The graph object, the nodes array and the node are the first three levels.
            CheckConfig(given, $"{path}.config", 4, errors);
            config = given.Clone();
        }

        RetryPolicy? retry = members.TryGetValue("retry", out JsonElement policy) ? Retry(policy, $"{path}.retry", errors) : null;
        return id is { } nodeId && type is { } nodeType && errors.Count == before ? new NodeDefinition(nodeId, nodeType, config) { Retry = retry } : null;
    }

    private RetryPolicy? Retry(JsonElement retry, string path, List<WorkflowGraphFormatError> errors)
    {
        if (retry.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new WorkflowGraphFormatError(path, $"{path} is {Kind(retry)}, not a retry object."));
            return null;
        }

        Dictionary<string, JsonElement> members = Members(retry, path, errors);
        int? attempts = null;
        string at = $"{path}.attempts";
        if (!members.TryGetValue("attempts", out JsonElement count))
        {
            errors.Add(new WorkflowGraphFormatError(at, $"{at} is missing; it is {Attempts}."));
        }
        else if (count.ValueKind != JsonValueKind.Number)
        {
            errors.Add(new WorkflowGraphFormatError(at, $"{at} is {Kind(count)}, not {Attempts}."));
        }
        else if (!count.TryGetInt32(out int tries) || tries < 2)
        {
            errors.Add(new WorkflowGraphFormatError(at, $"{at} is {Attempts}."));
        }
        else
        {
            attempts = tries;
        }

        TimeSpan? backoff = TimeSpan.Zero;
        if (members.TryGetValue("backoff", out JsonElement wait))
        {
            backoff = Backoff(wait, $"{path}.backoff", errors);
        }

        return attempts is { } valid && backoff is { } delay ? new RetryPolicy(valid, delay) : null;
    }

    private TimeSpan? Backoff(JsonElement wait, string path, List<WorkflowGraphFormatError> errors)
    {
        if (wait.ValueKind != JsonValueKind.String)
        {
            errors.Add(new WorkflowGraphFormatError(path, $"{path} is {Kind(wait)}, not {Duration}."));
            return null;
        }

        if (!TryText(wait, out string? text))
        {
            errors.Add(Undecodable(path));
            return null;
        }

        if (ToDuration(text) is { } parsed)
        {
            return parsed;
        }

        errors.Add(new WorkflowGraphFormatError(path, $"{path} is {Duration}."));
        return null;
    }

    // PnDTnHnMnS, every part optional but at least one present, seconds with up to seven decimals (one tick); null for any
    // other text or a duration beyond TimeSpan.MaxValue.
    private TimeSpan? ToDuration(string text)
    {
        if (text.Length > MaxDurationLength || text is "P" || duration.Match(text) is not { Success: true } match)
        {
            return null;
        }

        try
        {
            long ticks = checked(
                (Part(match, "days") * TimeSpan.TicksPerDay)
                + (Part(match, "hours") * TimeSpan.TicksPerHour)
                + (Part(match, "minutes") * TimeSpan.TicksPerMinute)
                + (Part(match, "seconds") * TimeSpan.TicksPerSecond)
                + long.Parse(match.Groups["fraction"].Value.PadRight(7, '0'), NumberStyles.None, CultureInfo.InvariantCulture));
            return TimeSpan.FromTicks(ticks);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    // A matched part's value, 0 when absent; an OverflowException for more digits than a long holds.
    private static long Part(Match match, string name)
    {
        Group group = match.Groups[name];
        return group.Success ? long.Parse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture) : 0;
    }

    private EdgeDefinition? Edge(JsonElement edge, string path, List<WorkflowGraphFormatError> errors)
    {
        if (edge.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new WorkflowGraphFormatError(path, $"{path} is {Kind(edge)}, not an edge object."));
            return null;
        }

        int before = errors.Count;
        Dictionary<string, JsonElement> members = Members(edge, path, errors);
        string? from = RequiredString(members, "from", path, errors);
        string? to = RequiredString(members, "to", path, errors);
        string? branch = members.TryGetValue("branch", out JsonElement named) ? String(named, $"{path}.branch", errors) : null;
        return from is { } source && to is { } target && errors.Count == before ? new EdgeDefinition(source, target, branch) : null;
    }
}
