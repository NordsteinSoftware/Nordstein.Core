using System.Text.Json;

namespace Nordstein.Core.Workflows;

/// <summary>
/// Records a run step by step; the product implements it over its storage. The interpreter calls
/// it in order for one run at a time and never concurrently for one run.
/// </summary>
/// <remarks>
/// Every step the interpreter starts ends with exactly one of
/// <see cref="StepCompletedAsync"/>, <see cref="StepPinnedAsync"/>, <see cref="StepRecoveredAsync"/>,
/// <see cref="StepSuspendedAsync"/> or <see cref="StepFailedAsync"/>, even when the run's token is cancelled: the closing call then
/// gets <see cref="CancellationToken.None"/>. A step resumed through
/// <see cref="IWorkflowInterpreter.ResumeAsync"/> is not started again; its next entry follows
/// its suspension. An exception from the journal ends the run and propagates to the caller.
/// </remarks>
public interface IStepJournal
{
    /// <summary>
    /// A node starts with its resolved input.
    /// </summary>
    Task StepStartedAsync(string nodeId, string nodeType, JsonElement input, CancellationToken cancellationToken = default);

    /// <summary>
    /// A node of a loop body starts with its resolved input in iteration <paramref name="iteration"/> (from 0) of its
    /// <see cref="IIteratingNodeType"/>; the same node id starts once per iteration. The default records it as
    /// <see cref="StepStartedAsync(string, string, JsonElement, CancellationToken)"/>.
    /// </summary>
    Task StepStartedAsync(string nodeId, string nodeType, JsonElement input, int iteration, CancellationToken cancellationToken = default) =>
        StepStartedAsync(nodeId, nodeType, input, cancellationToken);

    /// <summary>
    /// The started or resumed node completed with <paramref name="output"/>, taking
    /// <paramref name="branch"/> (null for a node that does not branch).
    /// </summary>
    Task StepCompletedAsync(string nodeId, JsonElement output, string? branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// The started node was not executed: it completed with the output and branch of its pin
    /// (<see cref="WorkflowExecution.Pins"/>). The default records it as <see cref="StepCompletedAsync"/>; a
    /// product that tells a pinned step from an executed one overrides it.
    /// </summary>
    Task StepPinnedAsync(string nodeId, JsonElement output, string? branch, CancellationToken cancellationToken = default) =>
        StepCompletedAsync(nodeId, output, branch, cancellationToken);

    /// <summary>
    /// The node failed with <paramref name="failure"/> (<see cref="WorkflowFailureKind.NodeFailed"/>) but has an edge on
    /// <see cref="ReservedBranches.Error"/>: it completed on that branch with <paramref name="output"/>
    /// (<c>{ message, nodeType }</c>) and the run goes on. The default records it as <see cref="StepCompletedAsync"/>
    /// on the error branch; a product that keeps the failure's text overrides it.
    /// </summary>
    Task StepRecoveredAsync(string nodeId, WorkflowFailure failure, JsonElement output, CancellationToken cancellationToken = default) =>
        StepCompletedAsync(nodeId, output, ReservedBranches.Error, cancellationToken);

    /// <summary>
    /// The started node's try number <paramref name="attempt"/> (from 1) failed with <paramref name="failure"/> and the
    /// node's <see cref="NodeDefinition.Retry"/> grants another try after its backoff; the step stays open. The default
    /// records nothing.
    /// </summary>
    Task StepRetryingAsync(string nodeId, int attempt, WorkflowFailure failure, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// The node suspended with <paramref name="bookmark"/>; the run stops.
    /// </summary>
    Task StepSuspendedAsync(string nodeId, JsonElement bookmark, CancellationToken cancellationToken = default);

    /// <summary>
    /// The node failed, or the run failed while at it; the run stops.
    /// </summary>
    Task StepFailedAsync(string nodeId, WorkflowFailure failure, CancellationToken cancellationToken = default);
}

/// <summary>
/// Why a step failed.
/// </summary>
/// <param name="Kind">The cause.</param>
/// <param name="Message">A text for the trace; for <see cref="WorkflowFailureKind.NodeFailed"/> the node's reason.</param>
public sealed record WorkflowFailure(WorkflowFailureKind Kind, string Message);

/// <summary>
/// The cause of a failed step.
/// </summary>
public enum WorkflowFailureKind
{
    /// <summary>
    /// The node returned <see cref="NodeResult.Failed"/>, threw, chose a branch its type does not
    /// declare, or suspended without being an <see cref="ISuspendingNodeType"/>.
    /// </summary>
    NodeFailed,

    /// <summary>
    /// The run would start more steps than <see cref="WorkflowExecution.MaxSteps"/>; the step is
    /// started and failed at once without executing.
    /// </summary>
    StepBudgetExhausted,

    /// <summary>
    /// The run's token was cancelled while the node executed.
    /// </summary>
    Cancelled,
}
