using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// Runs a <see cref="WorkflowGraph"/> one node at a time, recording every step through the
/// execution's <see cref="IStepJournal"/>.
/// </summary>
/// <remarks>
/// <para>
/// The visit order is a topological order of the graph, ties broken by the order of
/// <see cref="WorkflowGraph.Nodes"/>, so the same graph and the same node results visit the same
/// nodes in the same order. The trigger node runs first. Any other node runs when at least one
/// edge into it is taken: an edge from a node that ran and completed, which names no branch or
/// the branch that node took. A node that is not reached is skipped and not journaled.
/// </para>
/// <para>
/// A node named in <see cref="WorkflowExecution.Pins"/> is not executed: it is started with its
/// resolved input and journaled through <see cref="IStepJournal.StepPinnedAsync"/> with the pin's
/// output and branch, which downstream references and the reach rule then use; a pinned branch the
/// type does not declare fails the step like a node's own. A pinned trigger's output is the pin, while
/// <see cref="NodeContext.TriggerInput"/> stays what the product started the run with.
/// </para>
/// <para>
/// Before each node the interpreter resolves its configuration's data references against the
/// outputs so far: a missing value becomes JSON <c>null</c> (inside text: nothing). A node
/// starting beyond <see cref="WorkflowExecution.MaxSteps"/> fails with
/// <see cref="WorkflowFailureKind.StepBudgetExhausted"/>. Cancellation is checked before every
/// node; a cancellation while a node executes is journaled as
/// <see cref="WorkflowFailureKind.Cancelled"/> before the <see cref="OperationCanceledException"/>
/// propagates. The interpreter is stateless and thread-safe; concurrent runs share nothing.
/// </para>
/// </remarks>
public interface IWorkflowInterpreter
{
    /// <summary>
    /// Runs the graph from its trigger node with <paramref name="triggerInput"/>.
    /// </summary>
    /// <returns>How the run ended: completed, suspended at a node, or failed at a node.</returns>
    /// <exception cref="ArgumentException">The graph does not pass <see cref="IWorkflowValidator"/> with the execution's node types; the message lists the problems.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<WorkflowOutcome> RunAsync(WorkflowExecution execution, JsonElement triggerInput, CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues a run that suspended: replays <see cref="WorkflowSuspension.CompletedSteps"/>
    /// without executing them, resumes the suspended node with <paramref name="answer"/>, then
    /// runs on as <see cref="RunAsync"/> does. The step budget counts the replayed steps and the
    /// suspended one.
    /// </summary>
    /// <returns>How the run ended this time.</returns>
    /// <exception cref="ArgumentException">
    /// The graph is invalid; or the suspension does not fit the graph: a completed step the visit
    /// order would not have reached, one missing that it would have, or a suspended node that is not
    /// the next node to visit or not an <see cref="ISuspendingNodeType"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<WorkflowOutcome> ResumeAsync(WorkflowExecution execution, WorkflowSuspension suspension, JsonElement answer, CancellationToken cancellationToken = default);
}

/// <summary>
/// What one run executes: the graph, the catalogue, where steps are recorded and the limits.
/// </summary>
public sealed record WorkflowExecution
{
    /// <summary>
    /// Creates an execution.
    /// </summary>
    /// <param name="graph">The graph to run, as its version was stored.</param>
    /// <param name="nodeTypes">The catalogue; a product may bind its node instances to the run (its owner, its mode).</param>
    /// <param name="journal">Records the run's steps.</param>
    /// <param name="maxSteps">The most steps the run may start, counting resumed runs' earlier steps; at least 1.</param>
    /// <param name="dryRun">Whether nodes with side effects change nothing (<see cref="NodeContext.DryRun"/>).</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxSteps"/> is less than 1.</exception>
    public WorkflowExecution(WorkflowGraph graph, IReadOnlyCollection<INodeType> nodeTypes, IStepJournal journal, int maxSteps, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodeTypes);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSteps, 1);
        Graph = graph;
        NodeTypes = nodeTypes;
        Journal = journal;
        MaxSteps = maxSteps;
        DryRun = dryRun;
    }

    /// <summary>
    /// The graph to run.
    /// </summary>
    public WorkflowGraph Graph { get; }

    /// <summary>
    /// The node catalogue.
    /// </summary>
    public IReadOnlyCollection<INodeType> NodeTypes { get; }

    /// <summary>
    /// Records the run's steps.
    /// </summary>
    public IStepJournal Journal { get; }

    /// <summary>
    /// The most steps the run may start.
    /// </summary>
    public int MaxSteps { get; }

    /// <summary>
    /// Whether the run is a dry run.
    /// </summary>
    public bool DryRun { get; }

    /// <summary>
    /// The nodes that do not execute but complete with a given output, by node id; empty by default.
    /// A product's stubs for a test run.
    /// </summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public IReadOnlyDictionary<string, PinnedStep> Pins
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = new Dictionary<string, PinnedStep>(StringComparer.Ordinal);
}

/// <summary>
/// What a pinned node completes with instead of executing (<see cref="WorkflowExecution.Pins"/>).
/// </summary>
public sealed record PinnedStep
{
    /// <summary>
    /// Creates a pin.
    /// </summary>
    /// <param name="output">The output the node stands for; it should match the type's output schema.</param>
    /// <param name="branch">The branch taken, one of the type's <see cref="INodeType.Branches"/>; null for a node that does not branch.</param>
    /// <exception cref="ArgumentException"><paramref name="output"/> is an undefined (default) element.</exception>
    public PinnedStep(JsonElement output, string? branch)
    {
        if (output.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("The output is a defined JSON value.", nameof(output));
        }

        Output = output;
        Branch = branch;
    }

    /// <summary>
    /// The output the node stands for.
    /// </summary>
    public JsonElement Output { get; }

    /// <summary>
    /// The branch taken, or null.
    /// </summary>
    public string? Branch { get; }

    /// <summary>
    /// A switched-off node: <see cref="Output"/> stands as given, unchecked against the type's output schema and
    /// branches, so a node that is off passes nothing on (the product refuses it on a trigger and a branching node).
    /// </summary>
    public bool Unchecked { get; init; }
}

/// <summary>
/// A suspended run as the product stored it, to resume it.
/// </summary>
public sealed record WorkflowSuspension
{
    /// <summary>
    /// Creates a suspension.
    /// </summary>
    /// <param name="triggerInput">The run's trigger input.</param>
    /// <param name="completedSteps">The steps that completed before the suspension, in the order they ran.</param>
    /// <param name="nodeId">The suspended node.</param>
    /// <param name="bookmark">What the suspended node returned with <see cref="NodeResult.Suspended"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="completedSteps"/>, one of its items or <paramref name="nodeId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="triggerInput"/> or <paramref name="bookmark"/> is an undefined (default) element.</exception>
    public WorkflowSuspension(JsonElement triggerInput, IReadOnlyList<CompletedStep> completedSteps, string nodeId, JsonElement bookmark)
    {
        ArgumentNullException.ThrowIfNull(completedSteps);
        ArgumentNullException.ThrowIfNull(nodeId);
        if (completedSteps.Any(step => step is null))
        {
            throw new ArgumentNullException(nameof(completedSteps), "A suspension holds no null step.");
        }

        if (triggerInput.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("The trigger input is a defined JSON value.", nameof(triggerInput));
        }

        if (bookmark.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("The bookmark is a defined JSON value.", nameof(bookmark));
        }

        TriggerInput = triggerInput;
        CompletedSteps = [.. completedSteps];
        NodeId = nodeId;
        Bookmark = bookmark;
    }

    /// <summary>
    /// The run's trigger input.
    /// </summary>
    public JsonElement TriggerInput { get; }

    /// <summary>
    /// The steps that completed before the suspension.
    /// </summary>
    public IReadOnlyList<CompletedStep> CompletedSteps { get; }

    /// <summary>
    /// The suspended node.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// What the suspended node returned.
    /// </summary>
    public JsonElement Bookmark { get; }
}

/// <summary>
/// A step that completed: its node, output and branch, as journaled.
/// </summary>
/// <param name="NodeId">The node.</param>
/// <param name="Output">Its output.</param>
/// <param name="Branch">The branch it took, or null.</param>
public sealed record CompletedStep(string NodeId, JsonElement Output, string? Branch);

/// <summary>
/// How a run (or one resumed pass of it) ended: <see cref="Completed"/>, <see cref="Suspended"/>
/// or <see cref="Failed"/>.
/// </summary>
public abstract record WorkflowOutcome
{
    private WorkflowOutcome()
    {
    }

    /// <summary>
    /// Every reached node completed.
    /// </summary>
    public sealed record Completed : WorkflowOutcome;

    /// <summary>
    /// A node suspended; resume it with <see cref="IWorkflowInterpreter.ResumeAsync"/>.
    /// </summary>
    /// <param name="NodeId">The suspended node.</param>
    /// <param name="Bookmark">What it returned.</param>
    public sealed record Suspended(string NodeId, JsonElement Bookmark) : WorkflowOutcome;

    /// <summary>
    /// A step failed; the run is over.
    /// </summary>
    /// <param name="NodeId">The failed node.</param>
    /// <param name="Failure">Why.</param>
    public sealed record Failed(string NodeId, WorkflowFailure Failure) : WorkflowOutcome;
}
