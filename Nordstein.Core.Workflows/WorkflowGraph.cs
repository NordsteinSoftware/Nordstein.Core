using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// A workflow's definition: typed nodes and the edges between them. A valid graph (see
/// <see cref="IWorkflowValidator"/>) has exactly one trigger node, no cycle, and every node reachable
/// from the trigger.
/// </summary>
/// <remarks>
/// The graph is a value: two graphs are equal when their nodes and edges are equal in order, node
/// configurations compared by JSON content (<see cref="JsonElement.DeepEquals"/>). Instances are
/// immutable and thread-safe as long as the caller does not dispose the documents behind the
/// configurations; pass <see cref="JsonElement.Clone"/>d elements.
/// </remarks>
public sealed record WorkflowGraph
{
    /// <summary>
    /// Creates a graph.
    /// </summary>
    /// <param name="nodes">The nodes, in the order the definition lists them; that order breaks ties when the interpreter orders the visit.</param>
    /// <param name="edges">The edges.</param>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/>, <paramref name="edges"/> or one of their items is null.</exception>
    public WorkflowGraph(IReadOnlyList<NodeDefinition> nodes, IReadOnlyList<EdgeDefinition> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        if (nodes.Any(node => node is null))
        {
            throw new ArgumentNullException(nameof(nodes), "A graph holds no null node.");
        }

        if (edges.Any(edge => edge is null))
        {
            throw new ArgumentNullException(nameof(edges), "A graph holds no null edge.");
        }

        Nodes = [.. nodes];
        Edges = [.. edges];
    }

    /// <summary>
    /// The nodes, in definition order.
    /// </summary>
    public IReadOnlyList<NodeDefinition> Nodes { get; }

    /// <summary>
    /// The edges, in definition order.
    /// </summary>
    public IReadOnlyList<EdgeDefinition> Edges { get; }

    /// <inheritdoc />
    public bool Equals(WorkflowGraph? other) =>
        other is not null && Nodes.SequenceEqual(other.Nodes) && Edges.SequenceEqual(other.Edges);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Nodes.Count, Edges.Count);
}

/// <summary>
/// One node of a <see cref="WorkflowGraph"/>: its id, its node type as <c>name@major</c> and its configuration.
/// </summary>
/// <remarks>
/// The type names a catalogue entry by name and major (<see cref="NodeTypeReference"/>); the validator refuses a
/// text of another form or a (name, major) the catalogue lacks. Configuration strings may hold data references: <c>{{trigger.&lt;path&gt;}}</c> (the trigger
/// node's output) or <c>{{steps.&lt;nodeId&gt;.output.&lt;path&gt;}}</c> (an upstream node's output);
/// a path is dot-separated property names or array indexes and may be empty. A string that is
/// exactly one reference takes the referenced value with its JSON type; a reference inside longer
/// text is replaced by the value's text. There is no expression language.
/// Equality compares <see cref="Config"/> by JSON content and <see cref="Retry"/> by value.
/// </remarks>
public sealed record NodeDefinition
{
    /// <summary>
    /// Creates a node definition.
    /// </summary>
    /// <param name="id">The node's id, unique in its graph; the validator checks its form.</param>
    /// <param name="type">The node type as <c>name@major</c> (<see cref="INodeType.Type"/> at <see cref="INodeType.Major"/>); checked by the validator, not here.</param>
    /// <param name="config">The node's configuration; the validator checks it against <see cref="INodeType.InputSchema"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="id"/> or <paramref name="type"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="config"/> is an undefined (default) element.</exception>
    public NodeDefinition(string id, string type, JsonElement config)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(type);
        if (config.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("A node's configuration is a defined JSON value.", nameof(config));
        }

        Id = id;
        Type = type;
        Config = config;
    }

    /// <summary>
    /// The node's id, unique in its graph.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The node type as the graph names it, <c>name@major</c>.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// The node's configuration, with data references unresolved.
    /// </summary>
    public JsonElement Config { get; }

    /// <summary>
    /// How often the interpreter runs the node again after a failed try, or null: one try. The validator refuses a
    /// policy on a trigger and on a type with <see cref="INodeType.HasSideEffects"/>.
    /// </summary>
    public RetryPolicy? Retry { get; init; }

    /// <inheritdoc />
    public bool Equals(NodeDefinition? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && string.Equals(Type, other.Type, StringComparison.Ordinal)
        && JsonElement.DeepEquals(Config, other.Config)
        && Equals(Retry, other.Retry);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Type);
}

/// <summary>
/// A directed edge from one node to another. An edge leaving a node whose type declares
/// <see cref="INodeType.Branches"/> names the branch it follows; any other edge names none.
/// </summary>
public sealed record EdgeDefinition
{
    /// <summary>
    /// Creates an edge.
    /// </summary>
    /// <param name="from">The upstream node's id.</param>
    /// <param name="to">The downstream node's id.</param>
    /// <param name="branch">The branch of <paramref name="from"/> this edge follows, or null.</param>
    /// <exception cref="ArgumentNullException"><paramref name="from"/> or <paramref name="to"/> is null.</exception>
    public EdgeDefinition(string from, string to, string? branch = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        From = from;
        To = to;
        Branch = branch;
    }

    /// <summary>
    /// The upstream node's id.
    /// </summary>
    public string From { get; }

    /// <summary>
    /// The downstream node's id.
    /// </summary>
    public string To { get; }

    /// <summary>
    /// The branch of <see cref="From"/> this edge follows, or null.
    /// </summary>
    public string? Branch { get; }
}

/// <summary>
/// A node's retry policy (<see cref="NodeDefinition.Retry"/>): the interpreter runs the node up to
/// <see cref="Attempts"/> times, waiting <see cref="Backoff"/> after each try that ends in <see cref="NodeResult.Failed"/>
/// (an exception included); a cancellation never retries, and the last try's failure is the step's. One step is journaled,
/// each failed try through <see cref="IStepJournal.StepRetryingAsync"/>.
/// </summary>
public sealed record RetryPolicy
{
    /// <summary>
    /// Creates a policy.
    /// </summary>
    /// <param name="attempts">The most tries, at least 2.</param>
    /// <param name="backoff">The wait between two tries, zero or more.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempts"/> is less than 2, or <paramref name="backoff"/> is negative.</exception>
    public RetryPolicy(int attempts, TimeSpan backoff)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(backoff, TimeSpan.Zero);
        Attempts = attempts;
        Backoff = backoff;
    }

    /// <summary>
    /// The most tries.
    /// </summary>
    public int Attempts { get; }

    /// <summary>
    /// The wait between two tries.
    /// </summary>
    public TimeSpan Backoff { get; }
}
