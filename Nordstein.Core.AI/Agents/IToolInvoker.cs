using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Agents;

/// <summary>
/// Executes the tool calls an <see cref="IAgentLoop"/> receives from the model. The product
/// implements it: it maps a <see cref="ToolRequest.Name"/> to its own code and decides what the
/// tool may touch.
/// </summary>
/// <remarks>
/// The loop calls an invoker only for tools the agent offers (<see cref="IAgent.Tools"/>), one
/// call at a time and in the order the model asked for them. An exception thrown here does not
/// end the run: the loop turns it into a failed tool result the model reads. Only an
/// <see cref="OperationCanceledException"/> for the run's own cancellation token ends the run.
/// </remarks>
public interface IToolInvoker
{
    /// <summary>
    /// Executes <paramref name="request"/> and returns the content the model reads as its result.
    /// </summary>
    /// <param name="request">
    /// The tool call; <see cref="ToolRequest.Arguments"/> is the raw JSON text the model sent and
    /// is not validated against the tool's schema.
    /// </param>
    /// <param name="cancellationToken">The run's cancellation token.</param>
    /// <returns>The result contents; an empty list is a success without a result.</returns>
    Task<IReadOnlyList<Content>> InvokeAsync(ToolRequest request, CancellationToken cancellationToken);
}
