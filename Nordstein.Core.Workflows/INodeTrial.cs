using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// Executes one node of a graph against upstream outputs the caller supplies, without a run or a
/// journal, and checks a completed output against the node type's output schema. For a product
/// that lets a person try a node of a graph under construction.
/// </summary>
/// <remarks>
/// <para>
/// A try validates only what it needs: the node has a known type, its configuration passes the
/// type's <see cref="INodeType.InputSchema"/> and <see cref="INodeType.ValidateConfig"/>, and
/// every data reference in it names the trigger or a node present in the supplied outputs, with
/// a path that node's output schema has and a type the place accepts. The rest of the graph may
/// be invalid: a cycle or an unknown type elsewhere does not block the try. Problems come back in
/// <see cref="NodeTrialResult.Problems"/>, never as an exception.
/// </para>
/// <para>
/// The configuration is resolved as <see cref="IWorkflowInterpreter"/> resolves it (a missing
/// value becomes <c>null</c>), the node's <see cref="INodeType.ExecuteAsync"/> runs, and an
/// exception other than the token's cancellation, a branch the type does not declare, or a
/// suspension from a type that cannot suspend becomes <see cref="NodeResult.Failed"/>, as in a
/// run. An <see cref="IIteratingNodeType"/> node is not executed: the try reports its <see cref="IIteratingNodeType.Collect"/>
/// of no results on <c>done</c>. Stateless and thread-safe.
/// </para>
/// </remarks>
public interface INodeTrial
{
    /// <summary>
    /// Tries the node <paramref name="nodeId"/> of <paramref name="graph"/>.
    /// </summary>
    /// <param name="graph">The graph, valid or not.</param>
    /// <param name="nodeTypes">The catalogue; each (name, major) at most once.</param>
    /// <param name="nodeId">The node to try; it must be in the graph.</param>
    /// <param name="triggerOutput">The trigger's output, which <c>{{trigger.&lt;path&gt;}}</c> references read, and the node's <see cref="NodeContext.TriggerInput"/>.</param>
    /// <param name="outputs">The outputs of the upstream nodes by node id, which <c>{{steps.&lt;nodeId&gt;.output.&lt;path&gt;}}</c> references read.</param>
    /// <param name="dryRun">Whether the node executes as in a dry run (<see cref="NodeContext.DryRun"/>).</param>
    /// <param name="answer">For an <see cref="ISuspendingNodeType"/>: the answer to resume with when the node suspends; null reports the suspension.</param>
    /// <param name="iteration">For a node of a loop body: the item and index its <c>{{item…}}</c> and <c>{{index}}</c> references read; null makes such a reference a problem.</param>
    /// <param name="cancellationToken">The try's token; its cancellation propagates.</param>
    /// <returns>The resolved input, the node's result and every mismatch of a completed output against the output schema; or the problems that stopped the try.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/>, <paramref name="nodeTypes"/>, <paramref name="nodeId"/> or <paramref name="outputs"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeId"/> names no node of the graph; <paramref name="triggerOutput"/>, an output or <paramref name="answer"/> is an undefined (default) element; <paramref name="nodeTypes"/> holds a null item, a type with an invalid name or major, a read-only type that has side effects or suspends, or two types of one name and major.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<NodeTrialResult> TryAsync(
        WorkflowGraph graph,
        IReadOnlyCollection<INodeType> nodeTypes,
        string nodeId,
        JsonElement triggerOutput,
        IReadOnlyDictionary<string, JsonElement> outputs,
        bool dryRun,
        JsonElement? answer,
        IterationStub? iteration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How a try of one node ended.
/// </summary>
/// <param name="ResolvedInput">The node's configuration with every reference resolved; null when <paramref name="Problems"/> stopped the try.</param>
/// <param name="Result">The node's result; null when <paramref name="Problems"/> stopped the try.</param>
/// <param name="Problems">What stopped the try before the node executed: an unknown type, a configuration that fails its schema, a reference without an output. Empty when the node executed.</param>
/// <param name="OutputProblems">Every place where a <see cref="NodeResult.Completed"/> output does not fit the type's output schema; empty for any other result. A mismatch is a finding, not a failure.</param>
public sealed record NodeTrialResult(
    JsonElement? ResolvedInput,
    NodeResult? Result,
    IReadOnlyList<WorkflowValidationError> Problems,
    IReadOnlyList<OutputProblem> OutputProblems);

/// <summary>
/// One place where an output does not fit its schema.
/// </summary>
/// <param name="Path">The JSON path of the place, <c>$</c> for the output itself, such as <c>$.items[1].name</c>.</param>
/// <param name="Message">What is wrong, in English.</param>
public sealed record OutputProblem(string Path, string Message);
