using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nordstein.Core.Workflows.Internal;

/// <inheritdoc cref="IConditionNodeType" />
internal sealed class ConditionNodeType : IConditionNodeType
{
    private const string All = "all";
    private const string Any = "any";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private const string OpNames = "eq, neq, contains, gt, lt, in, matches";

    private readonly ReferenceParser references;
    private readonly JsonElement trueOutput = Parse("""{"result":true}""");
    private readonly JsonElement falseOutput = Parse("""{"result":false}""");

    public ConditionNodeType(ReferenceParser references)
    {
        this.references = references;
        const string rules =
            """{"type":"array","description":"The rules, each { left, op, right }.","items":{"type":"object","required":["left","op","right"],"additionalProperties":false,"properties":{"left":{"description":"The value to test, as a rule a data reference."},"op":{"type":"string","enum":["eq","neq","contains","gt","lt","in","matches"]},"right":{"description":"The value to compare with."}}}}""";
        InputSchema = Parse("""{"type":"object","additionalProperties":false,"properties":{"all":""" + rules + ""","any":""" + rules + "}}");
        OutputSchema = Parse("""{"type":"object","required":["result"],"properties":{"result":{"type":"boolean","description":"Whether the rules held."}}}""");
    }

    public string Type => IConditionNodeType.TypeName;

    public int Major => 1;

    public bool IsTrigger => false;

    public bool HasSideEffects => false;

    public bool ReadOnly => true;

    public IReadOnlyList<string> Branches { get; } = [IConditionNodeType.True, IConditionNodeType.False];

    public JsonElement InputSchema { get; }

    private JsonElement OutputSchema { get; }

    public JsonElement GetOutputSchema(JsonElement config) => OutputSchema;

    public IReadOnlyList<string> ValidateConfig(JsonElement config)
    {
        if (RulesOf(config) is not ({ } key, { } rules))
        {
            return ["config holds exactly one of all and any."];
        }

        if (rules.ValueKind != JsonValueKind.Array)
        {
            return [$"config.{key} is a list of rules, not a data reference."];
        }

        if (rules.GetArrayLength() == 0)
        {
            return [$"config.{key} has no rule."];
        }

        List<string> problems = [];
        int index = 0;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            string at = $"config.{key}[{index}]";
            index++;
            if (rule.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{at} is a rule, not a data reference.");
                continue;
            }

            // The input schema requires left, op and right and restricts op to the operators, unless op holds a reference.
            if (!rule.TryGetProperty("left", out _) || !rule.TryGetProperty("op", out JsonElement opValue) || !rule.TryGetProperty("right", out JsonElement right))
            {
                problems.Add($"{at} is a rule with left, op and right.");
                continue;
            }

            string op = opValue.ValueKind == JsonValueKind.String ? opValue.ToString() : string.Empty;
            if (!IsOp(op))
            {
                problems.Add(HoldsReference(opValue)
                    ? $"{at}.op is one of {OpNames}, not a data reference."
                    : $"{at}.op is one of {OpNames}.");
                continue;
            }

            if (op == "matches" && !HoldsReference(right) && (right.ValueKind != JsonValueKind.String || Compile(right.ToString()) is null))
            {
                problems.Add($"{at}.right is not a regular expression.");
            }

            if (op == "in" && !IsWholeReference(right) && right.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{at}.right is not an array.");
            }
        }

        return problems;
    }

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (RulesOf(context.Input) is not ({ } key, { } rules))
        {
            return Failed("config holds exactly one of all and any.");
        }

        if (rules.ValueKind != JsonValueKind.Array)
        {
            return Failed($"config.{key} is a list of rules.");
        }

        if (rules.GetArrayLength() == 0)
        {
            return Failed($"config.{key} has no rule.");
        }

        bool isAll = key == All;
        bool result = isAll;
        int index = 0;
        foreach (JsonElement rule in rules.EnumerateArray())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<NodeResult>(cancellationToken);
            }

            string at = $"config.{key}[{index}]";
            index++;
            if (!TryEvaluate(rule, at, out bool value, out string? problem))
            {
                return Failed(problem);
            }

            if (value != isAll)
            {
                result = value;
                break;
            }
        }

        return Task.FromResult<NodeResult>(result
            ? new NodeResult.Completed(trueOutput, IConditionNodeType.True)
            : new NodeResult.Completed(falseOutput, IConditionNodeType.False));
    }

    // The rules' key and value when exactly one of all and any is present; null otherwise.
    private static (string Key, JsonElement Rules)? RulesOf(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        bool hasAll = config.TryGetProperty(All, out JsonElement all);
        bool hasAny = config.TryGetProperty(Any, out JsonElement any);
        return hasAll == hasAny ? null : hasAll ? (All, all) : (Any, any);
    }

    // Whether the rule holds, or why it cannot be evaluated.
    private static bool TryEvaluate(JsonElement rule, string at, out bool holds, [NotNullWhen(false)] out string? problem)
    {
        holds = false;
        problem = null;
        if (rule.ValueKind != JsonValueKind.Object
            || !rule.TryGetProperty("left", out JsonElement left)
            || !rule.TryGetProperty("op", out JsonElement op)
            || !rule.TryGetProperty("right", out JsonElement right))
        {
            problem = $"{at} is a rule with left, op and right.";
            return false;
        }

        switch (op.ValueKind == JsonValueKind.String ? op.GetString() : null)
        {
            case "eq":
                holds = AreEqual(left, right);
                return true;
            case "neq":
                holds = !AreEqual(left, right);
                return true;
            case "contains":
                holds = left.ValueKind == JsonValueKind.Array
                    ? left.EnumerateArray().Any(item => AreEqual(item, right))
                    : Text(left).Contains(Text(right), StringComparison.OrdinalIgnoreCase);
                return true;
            case "gt":
                holds = Number(left) is { } greater && Number(right) is { } than && greater > than;
                return true;
            case "lt":
                holds = Number(left) is { } less && Number(right) is { } limit && less < limit;
                return true;
            case "in":
                holds = right.ValueKind == JsonValueKind.Array && right.EnumerateArray().Any(item => AreEqual(left, item));
                return true;
            case "matches":
                return TryMatch(left, right, at, out holds, out problem);
            default:
                problem = $"{at}.op is one of {OpNames}.";
                return false;
        }
    }

    // A right side that is no string matches nothing; one that does not compile, or a match beyond the timeout, is a problem.
    private static bool TryMatch(JsonElement left, JsonElement right, string at, out bool holds, [NotNullWhen(false)] out string? problem)
    {
        holds = false;
        problem = null;
        if (right.ValueKind != JsonValueKind.String)
        {
            return true;
        }

        if (Compile(right.ToString()) is not { } pattern)
        {
            problem = $"{at}.right is not a regular expression.";
            return false;
        }

        try
        {
            holds = pattern.IsMatch(Text(left));
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            problem = $"{at}.right took longer than {MatchTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} second to match.";
            return false;
        }
    }

    private static bool IsOp(string op) => op is "eq" or "neq" or "contains" or "gt" or "lt" or "in" or "matches";

    private static Regex? Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool AreEqual(JsonElement left, JsonElement right)
    {
        if (Number(left) is { } l && Number(right) is { } r)
        {
            return l == r;
        }

        if (left.ValueKind is JsonValueKind.Object or JsonValueKind.Array || right.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            return JsonElement.DeepEquals(left, right);
        }

        return string.Equals(Text(left), Text(right), StringComparison.OrdinalIgnoreCase);
    }

    private static decimal? Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
        _ => null,
    };

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.ToString(),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => value.GetRawText(),
    };

    // JsonElement.ToString of a string element is its text, as GetString, but never null.
    private static Task<NodeResult> Failed(string reason) => Task.FromResult<NodeResult>(new NodeResult.Failed(reason));

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // A configuration value the run replaces: a string holding a data reference, whole or inside text.
    private bool HoldsReference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && references.Parse(value.ToString()).References.Count > 0;

    // A configuration value that is exactly one data reference, so it takes the referenced value with its JSON type.
    private bool IsWholeReference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && references.Parse(value.ToString()).IsWhole;
}
