using System.Text.Json;

namespace Nordstein.Core.Workflows.Tests;

/// <summary>
/// A node type for tests: its behaviour is a delegate; by default it completes with its input as output.
/// </summary>
internal class TestNode : INodeType
{
    public TestNode(string type, string? inputSchema = null, string? outputSchema = null)
    {
        Type = type;
        InputSchema = Parse(inputSchema ?? "{}");
        OutputSchema = Parse(outputSchema ?? "{}");
    }

    public string Type { get; }

    public int Major { get; init; } = 1;

    public bool IsTrigger { get; init; }

    public bool HasSideEffects { get; init; }

    public bool ReadOnly { get; init; }

    public IReadOnlyList<string> Branches { get; init; } = [];

    public JsonElement InputSchema { get; }

    public JsonElement OutputSchema { get; }

    public Func<JsonElement, IReadOnlyList<string>> ConfigCheck { get; init; } = _ => [];

    public Func<NodeContext, CancellationToken, Task<NodeResult>> Execute { get; init; } =
        (context, _) => Task.FromResult<NodeResult>(new NodeResult.Completed(context.Input));

    public List<NodeContext> Calls { get; } = [];

    public JsonElement GetOutputSchema(JsonElement config) => OutputSchema;

    public IReadOnlyList<string> ValidateConfig(JsonElement config) => ConfigCheck(config);

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken cancellationToken)
    {
        Calls.Add(context);
        return Execute(context, cancellationToken);
    }

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>
/// A suspending node type for tests: suspends with its input as bookmark, resumes with the answer as output.
/// </summary>
internal sealed class TestSuspendingNode : TestNode, ISuspendingNodeType
{
    public TestSuspendingNode(string type)
        : base(type)
    {
        Execute = (context, _) => Task.FromResult<NodeResult>(new NodeResult.Suspended(context.Input));
    }

    public List<(JsonElement Bookmark, JsonElement Answer)> Resumes { get; } = [];

    public Task<NodeResult> ResumeAsync(NodeContext context, JsonElement bookmark, JsonElement answer, CancellationToken cancellationToken)
    {
        Resumes.Add((bookmark, answer));
        return Task.FromResult<NodeResult>(new NodeResult.Completed(answer));
    }
}

/// <summary>
/// An iterating node type for tests: iterates <c>items</c>, at most <c>max</c> (default 20) of them, collecting <c>{ count, results }</c>.
/// </summary>
internal sealed class TestLoopNode : TestNode, IIteratingNodeType
{
    public TestLoopNode(string type = "each", string? inputSchema = null)
        : base(type, inputSchema ?? """{"type":"object","required":["items"],"properties":{"items":{"type":"array"},"max":{"type":"integer"}}}""", """{"type":"object","properties":{"count":{"type":"integer"},"results":{"type":"array"}}}""")
    {
        Branches = [LoopBranches.Body, LoopBranches.Done];
        Execute = (_, _) => throw new InvalidOperationException("The kernel runs a loop; a loop node is never executed.");
    }

    public string ItemsProperty => "items";

    /// <summary>When set, <see cref="MaxItems"/> throws it: a product's hook that fails.</summary>
    public Exception? MaxItemsFailure { get; init; }

    /// <summary>When set, <see cref="Collect"/> throws it: a product's hook that fails.</summary>
    public Exception? CollectFailure { get; init; }

    public int MaxItems(JsonElement config) => MaxItemsFailure is null
        ? config.TryGetProperty("max", out JsonElement max) ? max.GetInt32() : 20
        : throw MaxItemsFailure;

    public JsonElement Collect(NodeContext context, IReadOnlyList<JsonElement> results) => CollectFailure is null
        ? JsonSerializer.SerializeToElement(new { count = results.Count, results })
        : throw CollectFailure;
}

/// <summary>
/// Records journal entries as text lines, such as <c>started a</c>, <c>completed a</c>, <c>failed a NodeFailed</c>.
/// </summary>
internal sealed class RecordingJournal : IStepJournal
{
    public List<string> Entries { get; } = [];

    public Dictionary<string, JsonElement> Inputs { get; } = [];

    public Dictionary<string, JsonElement> Outputs { get; } = [];

    public Dictionary<string, WorkflowFailure> Failures { get; } = [];

    public List<JsonElement> InputsOf(string nodeId) => AllInputs.Where(pair => pair.NodeId == nodeId).Select(pair => pair.Input).ToList();

    public List<(string NodeId, JsonElement Input)> AllInputs { get; } = [];

    public Task StepStartedAsync(string nodeId, string nodeType, JsonElement input, CancellationToken cancellationToken)
    {
        Entries.Add($"started {nodeId}");
        Inputs[nodeId] = input;
        AllInputs.Add((nodeId, input));
        return Task.CompletedTask;
    }

    public Task StepStartedAsync(string nodeId, string nodeType, JsonElement input, int iteration, CancellationToken cancellationToken)
    {
        Entries.Add($"started {nodeId} #{iteration}");
        Inputs[nodeId] = input;
        AllInputs.Add((nodeId, input));
        return Task.CompletedTask;
    }

    public Task StepCompletedAsync(string nodeId, JsonElement output, string? branch, CancellationToken cancellationToken)
    {
        Entries.Add(branch is null ? $"completed {nodeId}" : $"completed {nodeId} {branch}");
        Outputs[nodeId] = output;
        return Task.CompletedTask;
    }

    public Task StepPinnedAsync(string nodeId, JsonElement output, string? branch, CancellationToken cancellationToken)
    {
        Entries.Add(branch is null ? $"pinned {nodeId}" : $"pinned {nodeId} {branch}");
        Outputs[nodeId] = output;
        return Task.CompletedTask;
    }

    public Task StepSuspendedAsync(string nodeId, JsonElement bookmark, CancellationToken cancellationToken)
    {
        Entries.Add($"suspended {nodeId}");
        return Task.CompletedTask;
    }

    public Task StepFailedAsync(string nodeId, WorkflowFailure failure, CancellationToken cancellationToken)
    {
        Entries.Add($"failed {nodeId} {failure.Kind}");
        Failures[nodeId] = failure;
        return Task.CompletedTask;
    }

    public Task StepRecoveredAsync(string nodeId, WorkflowFailure failure, JsonElement output, CancellationToken cancellationToken)
    {
        Entries.Add($"recovered {nodeId}");
        Failures[nodeId] = failure;
        Outputs[nodeId] = output;
        return Task.CompletedTask;
    }

    public Task StepRetryingAsync(string nodeId, int attempt, WorkflowFailure failure, CancellationToken cancellationToken)
    {
        Entries.Add($"retrying {nodeId} {attempt} {failure.Message}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// A journal without <see cref="IStepJournal.StepPinnedAsync"/> of its own: the interface's default routes a pinned
/// step to <see cref="IStepJournal.StepCompletedAsync"/>.
/// </summary>
internal sealed class LegacyJournal : IStepJournal
{
    public List<string> Entries { get; } = [];

    public Task StepStartedAsync(string nodeId, string nodeType, JsonElement input, CancellationToken cancellationToken)
    {
        Entries.Add($"started {nodeId}");
        return Task.CompletedTask;
    }

    public Task StepCompletedAsync(string nodeId, JsonElement output, string? branch, CancellationToken cancellationToken)
    {
        Entries.Add(branch is null ? $"completed {nodeId}" : $"completed {nodeId} {branch}");
        return Task.CompletedTask;
    }

    public Task StepSuspendedAsync(string nodeId, JsonElement bookmark, CancellationToken cancellationToken)
    {
        Entries.Add($"suspended {nodeId}");
        return Task.CompletedTask;
    }

    public Task StepFailedAsync(string nodeId, WorkflowFailure failure, CancellationToken cancellationToken)
    {
        Entries.Add($"failed {nodeId} {failure.Kind}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// A node type that implements only the members <see cref="INodeType"/> requires, to observe the defaults.
/// </summary>
internal sealed class MinimalNode : INodeType
{
    private static readonly JsonElement Empty = JsonDocument.Parse("{}").RootElement.Clone();

    public string Type => "minimal";

    public int Major => 1;

    public bool IsTrigger => false;

    public bool HasSideEffects => false;

    public IReadOnlyList<string> Branches => [];

    public JsonElement InputSchema => Empty;

    public JsonElement GetOutputSchema(JsonElement config) => Empty;

    public IReadOnlyList<string> ValidateConfig(JsonElement config) => [];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken cancellationToken) =>
        Task.FromResult<NodeResult>(new NodeResult.Completed(context.Input));
}
