using Nordstein.Core.AI.Clients;
using Nordstein.Core.AI.Messages;

namespace Nordstein.Core.AI.Agents;

/// <summary>
/// Runs an <see cref="IAgent"/> on an <see cref="IModelClient"/>: it asks the model, executes the
/// tool calls of the reply through an <see cref="IToolInvoker"/>, and repeats until the model gives
/// a final answer or <see cref="AgentLoopOptions.MaxSteps"/> model calls are made.
/// </summary>
/// <remarks>
/// <para>
/// Each step is one <see cref="IModelClient.CompleteAsync(Conversation, ModelOptions?, IReadOnlyDictionary{string, string}?, CancellationToken)"/>
/// call with the agent's <see cref="IAgent.Tools"/>, its <see cref="IAgent.ModelParameters"/>
/// as sampling and <see cref="AgentLoopOptions.ResponseFormat"/>, if any. A reply without tool calls is the final answer. Otherwise every tool call runs in
/// order and its result is appended as a <see cref="ToolMessage"/> before the next step; the tool
/// calls of the last allowed step run too, so a call such as "record this result" is never lost.
/// </para>
/// <para>
/// A tool call never ends the run with an exception. A call naming a tool the agent does not
/// offer, and a call whose invoker throws, become a failed tool result the model reads. Model
/// client exceptions propagate unchanged. Cancellation is checked before every model call and
/// every tool call and ends the run with an <see cref="OperationCanceledException"/>.
/// </para>
/// <para>
/// The implementation is stateless and thread-safe; concurrent runs share nothing. It does not
/// dispose the client.
/// </para>
/// </remarks>
public interface IAgentLoop
{
    /// <summary>
    /// Runs <paramref name="agent"/> on <paramref name="conversation"/> until a final answer or
    /// the step limit.
    /// </summary>
    /// <param name="agent">The agent: its system prompt, tools and sampling parameters.</param>
    /// <param name="client">The model client every step calls; the caller owns and disposes it.</param>
    /// <param name="tools">Executes the tool calls of the run.</param>
    /// <param name="conversation">
    /// The messages the run starts from, without a system message: the loop prepends the agent's
    /// rendered system prompt.
    /// </param>
    /// <param name="options">The model name, the step limit and the prompt variables.</param>
    /// <param name="cancellationToken">Cancels the run between and during steps.</param>
    /// <returns>The outcome, the final answer if any, the transcript, the step count and the usage.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="conversation"/> already holds a system message, or
    /// <see cref="AgentLoopOptions.ModelName"/> is empty or white space.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="AgentLoopOptions.MaxSteps"/> is less than 1.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="InferenceUnavailableException">A model call found the endpoint unavailable; propagated unchanged.</exception>
    /// <exception cref="InferenceContractException">A model call broke the contract; propagated unchanged.</exception>
    /// <exception cref="Nordstein.Core.AI.Serialization.OutputFormatException">A typed reply did not parse; propagated unchanged.</exception>
    Task<AgentLoopResult> RunAsync(
        IAgent agent,
        IModelClient client,
        IToolInvoker tools,
        Conversation conversation,
        AgentLoopOptions options,
        CancellationToken cancellationToken = default);
}
