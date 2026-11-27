namespace Nordstein.Core.AI.Agents;

/// <summary>
/// How an <see cref="IAgentLoop"/> run ended.
/// </summary>
public enum AgentLoopOutcome
{
    /// <summary>
    /// The model gave a final answer: an assistant message without tool calls.
    /// </summary>
    Completed,

    /// <summary>
    /// The run made <see cref="AgentLoopOptions.MaxSteps"/> model calls and the last one still
    /// asked for tools. Those tools ran, but the model never read their results. The run is
    /// incomplete.
    /// </summary>
    StepLimitReached,
}
