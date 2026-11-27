using System.Buffers;
using System.Globalization;
using System.Text.Json;

namespace Nordstein.Core.Workflows.Internal;

/// <inheritdoc cref="IForEachNodeType" />
internal sealed class ForEachNodeType : IForEachNodeType
{
    private const string MaxItemsProperty = "maxItems";

    private readonly string maxItemsProblem =
        $"config.{MaxItemsProperty} is a whole number from 1 to {IForEachNodeType.MaxItemsLimit.ToString(CultureInfo.InvariantCulture)}, not a data reference.";

    public ForEachNodeType()
    {
        InputSchema = Parse(
            """
            {"type":"object","required":["items"],"additionalProperties":false,"properties":{
              "items":{"type":"array","description":"The list to go through, as a rule a data reference such as {{steps.read.output.lines}}."},
              "maxItems":{"type":"integer","description":"The most items, 1 to 100; default 20. A longer list fails the step."}}}
            """);
        OutputSchema = Parse(
            """
            {"type":"object","required":["count","results"],"properties":{
              "count":{"type":"integer","description":"How many items the body ran for."},
              "results":{"type":"array","description":"The body's last step's output per item, in order."}}}
            """);
    }

    public string Type => IForEachNodeType.TypeName;

    public int Major => 1;

    public bool IsTrigger => false;

    public bool HasSideEffects => false;

    public IReadOnlyList<string> Branches { get; } = [LoopBranches.Body, LoopBranches.Done];

    public JsonElement InputSchema { get; }

    public string ItemsProperty => "items";

    private JsonElement OutputSchema { get; }

    public JsonElement GetOutputSchema(JsonElement config) => OutputSchema;

    public IReadOnlyList<string> ValidateConfig(JsonElement config) =>
        config.ValueKind == JsonValueKind.Object && config.TryGetProperty(MaxItemsProperty, out JsonElement max) && Limit(max) is null
            ? [maxItemsProblem]
            : [];

    public int MaxItems(JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("A for-each configuration is an object.", nameof(config));
        }

        if (!config.TryGetProperty(MaxItemsProperty, out JsonElement max))
        {
            return IForEachNodeType.DefaultMaxItems;
        }

        return Limit(max) ?? throw new ArgumentException(maxItemsProblem, nameof(config));
    }

    public JsonElement Collect(NodeContext context, IReadOnlyList<JsonElement> results)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(results);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("count", results.Count);
            writer.WriteStartArray("results");
            foreach (JsonElement result in results)
            {
                if (result.ValueKind == JsonValueKind.Undefined)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    result.WriteTo(writer);
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult<NodeResult>(new NodeResult.Failed("The kernel runs a for-each node's loop; the node never executes on its own."));

    // A literal whole number from 1 to the limit; null for anything else, a data reference included.
    private static int? Limit(JsonElement max) =>
        max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out int count) && count is >= 1 and <= IForEachNodeType.MaxItemsLimit ? count : null;

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
