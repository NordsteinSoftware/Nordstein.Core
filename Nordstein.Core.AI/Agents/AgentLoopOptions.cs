using Nordstein.Core.AI.Clients;

namespace Nordstein.Core.AI.Agents;

/// <summary>
/// The per-run settings of an <see cref="IAgentLoop"/> run.
/// </summary>
/// <param name="ModelName">The model name every completion request of the run carries.</param>
/// <param name="MaxSteps">
/// The most model calls the run makes, at least 1. A run that reaches it without a final answer
/// ends <see cref="AgentLoopOutcome.StepLimitReached"/>.
/// </param>
/// <param name="PromptVariables">
/// The variables the agent's system prompt is rendered with, also passed on to every
/// completion request; <see langword="null"/> for none.
/// </param>
public sealed record AgentLoopOptions(
    string ModelName,
    int MaxSteps,
    IReadOnlyDictionary<string, string>? PromptVariables = null)
{
    /// <summary>
    /// The shape every completion request of the run asks the model to give its reply in
    /// (<see cref="ModelOptions.ResponseFormat"/>), or <see langword="null"/> (the default) for none.
    /// </summary>
    /// <remarks>
    /// Sent on every step, tool-calling steps included: a provider applies it to the final answer
    /// only. The loop does not check the answer against it; a provider that ignores the member
    /// leaves that check to the caller. Set it with an object initializer or <c>with</c>. Part of
    /// the record's value equality.
    /// </remarks>
    public ResponseFormat? ResponseFormat { get; init; }
}
